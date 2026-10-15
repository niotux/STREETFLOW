using System.Text.Json;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

// The registry stores folder references. Project inputs are read from those folders on demand.
public sealed partial class ProjectCatalog
{
  private static readonly StringComparer FolderComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
  private readonly SemaphoreSlim importGate = new(1, 1);
  private readonly string root;
  private readonly string stagingRoot;
  private readonly long maximumBytes;
  private readonly int maximumFiles;
  private ProjectFolder[] folders;

  public string RegistryPath { get; }

  public ProjectCatalog(SimulatorOptions options)
  {
    maximumBytes = options.MaximumProjectBytes;
    maximumFiles = options.MaximumProjectFiles;
    if (maximumBytes <= 0 || maximumFiles <= 0)
    {
      throw new InvalidOperationException("Project upload limits must be positive");
    }

    string dataRoot = Path.GetFullPath(options.DataRoot);
    root = Path.Combine(dataRoot, "projects");
    stagingRoot = Path.Combine(dataRoot, ".project-imports");
    RegistryPath = Path.Combine(dataRoot, "project-folders.json");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(stagingRoot);
    folders = ReadRegistry();
  }

  public ProjectInfo[] List()
  {
    List<ProjectInfo> result = [];
    foreach (ProjectFolderStatus folder in ListFolders())
    {
      if (folder.Project is not null)
      {
        result.Add(folder.Project);
      }
    }

    result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Manifest.Id, right.Manifest.Id));
    return result.ToArray();
  }

  public ProjectFolderStatus[] ListFolders()
  {
    List<ProjectFolderStatus> result = [];
    HashSet<string> ids = new(StringComparer.Ordinal);
    foreach (ProjectFolder folder in Volatile.Read(ref folders))
    {
      ProjectFolderStatus status = ReadFolder(folder.Path);
      if (status.Project is not null)
      {
        string id = status.Project.Manifest.Id;
        if (!ids.Add(id))
        {
          status = new ProjectFolderStatus(folder.Path, null, $"Duplicate project ID: {id}");
        }
      }

      result.Add(status);
    }

    return result.ToArray();
  }

  public CityProject Get(string id)
  {
    foreach (ProjectFolder folder in Volatile.Read(ref folders))
    {
      CityProject? project = TryReadProject(folder.Path, out _);
      if (project is null)
      {
        continue;
      }

      if (project.Manifest.Id == id)
      {
        return project;
      }
    }

    throw new ApiException(404, "unknown_project", $"No readable registered folder has project ID '{id}'. Refresh the folder list to inspect missing or invalid files.");
  }

  public async Task<ProjectInfo> ImportFolderAsync(string source, CancellationToken token)
  {
    string path = NormalizeFolder(source);
    await importGate.WaitAsync(token);
    try
    {
      CityProject project = LoadFolder(path);
      EnsureUniqueId(project.Manifest.Id, path);
      ProjectFolder[] current = Volatile.Read(ref folders);
      foreach (ProjectFolder folder in current)
      {
        if (FolderComparer.Equals(folder.Path, path))
        {
          return project.Info;
        }
      }

      List<ProjectFolder> next = new(current);
      next.Add(new ProjectFolder(path));
      token.ThrowIfCancellationRequested();
      SaveFolders(next.ToArray());
      return project.Info;
    }
    finally
    {
      importGate.Release();
    }
  }

  public async Task RemoveFolderAsync(string source, CancellationToken token)
  {
    string path = NormalizeFolder(source);
    await importGate.WaitAsync(token);
    try
    {
      List<ProjectFolder> remaining = [];
      bool found = false;
      foreach (ProjectFolder folder in Volatile.Read(ref folders))
      {
        if (FolderComparer.Equals(folder.Path, path))
        {
          found = true;
        }
        else
        {
          remaining.Add(folder);
        }
      }

      if (!found)
      {
        throw new ApiException(404, "unknown_project_folder", "This folder is not registered");
      }

      token.ThrowIfCancellationRequested();
      SaveFolders(remaining.ToArray());
    }
    finally
    {
      importGate.Release();
    }
  }

  private ProjectFolder[] ReadRegistry()
  {
    if (File.Exists(RegistryPath))
    {
      ProjectFolder[] saved = JsonSerializer.Deserialize<ProjectFolder[]>(File.ReadAllText(RegistryPath), JsonDefaults.Options) ?? throw new InvalidDataException("Project folder registry must contain an array");
      List<ProjectFolder> normalized = [];
      foreach (ProjectFolder folder in saved)
      {
        if (folder is null)
        {
          throw new InvalidDataException("Project folder registry cannot contain null entries");
        }

        normalized.Add(new ProjectFolder(NormalizeFolder(folder.Path)));
      }

      return normalized.ToArray();
    }

    // Keep existing on-disk imports usable without copying or rewriting their inputs.
    List<ProjectFolder> migrated = [];
    foreach (string stored in Directory.EnumerateDirectories(root))
    {
      if (File.Exists(Path.Combine(stored, "import.json")))
      {
        migrated.Add(new ProjectFolder(Path.Combine(stored, "package")));
      }
    }

    ProjectFolder[] initial = migrated.ToArray();
    WriteRegistry(initial);
    return initial;
  }

  private void SaveFolders(ProjectFolder[] next)
  {
    WriteRegistry(next);
    Volatile.Write(ref folders, next);
  }

  private void WriteRegistry(ProjectFolder[] next)
  {
    string temporary = RegistryPath + ".tmp";
    File.WriteAllText(temporary, JsonSerializer.Serialize(next, JsonDefaults.Options));
    File.Move(temporary, RegistryPath, overwrite: true);
  }

  private static ProjectFolderStatus ReadFolder(string path)
  {
    CityProject? project = TryReadProject(path, out string? error);
    return new ProjectFolderStatus(path, project?.Info, error);
  }

  private static CityProject? TryReadProject(string path, out string? error)
  {
    error = null;
    try
    {
      return LoadFolder(path);
    }
    catch (ApiException exception)
    {
      error = exception.Message;
    }
    catch (IOException exception)
    {
      error = exception.Message;
    }
    catch (UnauthorizedAccessException)
    {
      error = "Project folder is not readable by the simulator service";
    }

    return null;
  }

  private static CityProject LoadFolder(string path)
  {
    if (!Directory.Exists(path))
    {
      throw ProjectPaths.Invalid($"Project folder does not exist: {path}");
    }

    ProjectPaths.RejectLink(path);
    return CityProjectLoader.Load(path);
  }

  private static string NormalizeFolder(string source)
  {
    if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source))
    {
      throw ProjectPaths.Invalid("path must be an absolute project folder path visible to the simulator");
    }

    try
    {
      return Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
    }
    catch (ArgumentException)
    {
      throw ProjectPaths.Invalid("Invalid project folder path");
    }
  }

  private void EnsureUniqueId(string id, string path)
  {
    foreach (ProjectFolder folder in Volatile.Read(ref folders))
    {
      if (FolderComparer.Equals(folder.Path, path))
      {
        continue;
      }

      CityProject? project = TryReadProject(folder.Path, out _);
      if (project is null)
      {
        continue;
      }

      if (project.Manifest.Id == id)
      {
        throw new ApiException(409, "duplicate_project", $"Another registered folder has project ID '{id}'");
      }
    }
  }
}

using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

// Lists the projects folder. Project folders are read in place; ZIP files are extracted once into a cache.
public sealed partial class ProjectCatalog
{
  private const string ManifestFile = "experiment.json";
  private readonly SemaphoreSlim scanGate = new(1, 1);
  private readonly string projectsRoot;
  private readonly string cacheRoot;
  private readonly string stagingRoot;
  private readonly long maximumBytes;
  private readonly int maximumFiles;

  public ProjectCatalog(SimulatorOptions options)
  {
    maximumBytes = options.MaximumProjectBytes;
    maximumFiles = options.MaximumProjectFiles;
    if (maximumBytes <= 0 || maximumFiles <= 0)
    {
      throw new InvalidOperationException("Project ZIP limits must be positive");
    }

    if (string.IsNullOrWhiteSpace(options.ProjectsRoot))
    {
      throw new InvalidOperationException("ProjectsRoot must name the projects folder");
    }

    projectsRoot = Path.GetFullPath(options.ProjectsRoot);
    string dataRoot = Path.GetFullPath(options.DataRoot);
    cacheRoot = Path.Combine(dataRoot, "project-cache");
    stagingRoot = Path.Combine(dataRoot, ".project-imports");
    Directory.CreateDirectory(cacheRoot);
    Directory.CreateDirectory(stagingRoot);
  }

  public async Task<ProjectInfo[]> ListAsync(CancellationToken token)
  {
    List<ProjectInfo> result = [];
    foreach (ScannedSource scanned in await ScanAsync(token))
    {
      if (scanned.Project is not null)
      {
        result.Add(scanned.Project.Info);
      }
    }

    result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Manifest.Id, right.Manifest.Id));
    return result.ToArray();
  }

  public async Task<ProjectSource[]> ListSourcesAsync(CancellationToken token)
  {
    List<ProjectSource> result = [];
    foreach (ScannedSource scanned in await ScanAsync(token))
    {
      result.Add(scanned.Source);
    }

    return result.ToArray();
  }

  public async Task<CityProject> GetAsync(string id, CancellationToken token)
  {
    foreach (ScannedSource scanned in await ScanAsync(token))
    {
      if (scanned.Project?.Manifest.Id == id)
      {
        return scanned.Project;
      }
    }

    throw new ApiException(404, "unknown_project", $"No readable project in the projects folder has ID '{id}'. Refresh the list to see unavailable entries.");
  }

  private async Task<ScannedSource[]> ScanAsync(CancellationToken token)
  {
    if (!Directory.Exists(projectsRoot))
    {
      return [];
    }

    await scanGate.WaitAsync(token);
    try
    {
      List<string> names = [];
      foreach (string entry in Directory.EnumerateFileSystemEntries(projectsRoot))
      {
        string name = Path.GetFileName(entry);
        bool isZip = File.Exists(entry) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (!name.StartsWith('.') && (Directory.Exists(entry) || isZip))
        {
          names.Add(name);
        }
      }

      names.Sort(StringComparer.Ordinal);
      List<ScannedSource> result = [];
      HashSet<string> ids = new(StringComparer.Ordinal);
      HashSet<string> cacheKeys = new(StringComparer.Ordinal);
      foreach (string name in names)
      {
        string path = Path.Combine(projectsRoot, name);
        bool isZip = File.Exists(path);
        ScannedSource scanned = isZip ? await ReadZipAsync(name, path, cacheKeys, token) : ReadFolder(name, path);
        string? id = scanned.Project?.Manifest.Id;
        if (id is not null && !ids.Add(id))
        {
          scanned = Unavailable(name, scanned.Source.Kind, $"Duplicate project ID '{id}'; another entry in the projects folder already uses it");
        }

        result.Add(scanned);
      }

      PruneCache(cacheKeys);
      return result.ToArray();
    }
    finally
    {
      scanGate.Release();
    }
  }

  private static ScannedSource ReadFolder(string name, string path)
  {
    if (!File.Exists(Path.Combine(path, ManifestFile)))
    {
      return Unavailable(name, ProjectSource.FolderKind, $"No {ManifestFile} in this folder");
    }

    try
    {
      ProjectPaths.RejectLink(path);
      CityProject project = CityProjectLoader.Load(path);
      return new ScannedSource(new ProjectSource(name, ProjectSource.FolderKind, project.Info, null), project);
    }
    catch (Exception exception) when (IsReadError(exception))
    {
      return Unavailable(name, ProjectSource.FolderKind, ReadErrorMessage(exception));
    }
  }

  private static ScannedSource Unavailable(string name, string kind, string error) => new(new ProjectSource(name, kind, null, error), null);

  private static bool IsReadError(Exception exception) => exception is ApiException or IOException or UnauthorizedAccessException or InvalidDataException;

  private static string ReadErrorMessage(Exception exception) => exception is UnauthorizedAccessException ? "Project files are not readable by the simulator service" : exception.Message;

  private sealed record ScannedSource(ProjectSource Source, CityProject? Project);
}

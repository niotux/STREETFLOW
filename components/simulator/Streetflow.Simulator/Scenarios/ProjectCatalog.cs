using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

// Immutable package copies are committed by an atomic directory rename after validation.
// This catalog does not create runs, launch SUMO, or activate legacy observations.
public sealed class ProjectCatalog
{
  private const int CopyBufferSize = 65536;
  private const int ZipFileKindMask = 0xf000;
  private const int ZipRegularFileKind = 0x8000;
  private const int ZipDirectoryKind = 0x4000;
  private readonly ConcurrentDictionary<string, CityProject> projects = new(StringComparer.Ordinal);
  private readonly SemaphoreSlim importGate = new(1, 1);
  private readonly string root;
  private readonly string stagingRoot;
  private readonly long maximumBytes;
  private readonly int maximumFiles;
  public ProjectCatalog(SimulatorOptions options)
  {
    maximumBytes = options.MaximumProjectBytes;
    maximumFiles = options.MaximumProjectFiles;
    if (maximumBytes <= 0 || maximumFiles <= 0)
    {
      throw new InvalidOperationException("Project import limits must be positive");
    }

    root = Path.Combine(Path.GetFullPath(options.DataRoot), "projects");
    stagingRoot = Path.Combine(Path.GetFullPath(options.DataRoot), ".project-imports");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(stagingRoot);
    foreach (string stored in Directory.EnumerateDirectories(root))
    {
      string metadataPath = Path.Combine(stored, "import.json");
      if (!File.Exists(metadataPath))
      {
        throw new InvalidDataException($"Missing imported project metadata: {stored}");
      }

      StoredProject metadata = JsonSerializer.Deserialize<StoredProject>(File.ReadAllText(metadataPath), JsonDefaults.Options) ?? throw new InvalidDataException($"Invalid imported project metadata: {stored}");
      CityProject project = CityProjectLoader.Load(Path.Combine(stored, "package"));
      if (metadata.FormatVersion != 1 || metadata.Id != project.Manifest.Id || metadata.Revision != project.Revision)
      {
        throw new InvalidDataException($"Imported project inputs changed: {project.Manifest.Id}");
      }

      if (!projects.TryAdd(project.Manifest.Id, project))
      {
        throw new InvalidDataException($"Duplicate stored project ID: {project.Manifest.Id}");
      }
    }
  }

  public ProjectInfo[] List()
  {
    List<ProjectInfo> result = new(projects.Count);
    foreach (CityProject project in projects.Values)
    {
      result.Add(project.Info);
    }

    result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Manifest.Id, right.Manifest.Id));
    return result.ToArray();
  }

  public CityProject Get(string id) => projects.GetValueOrDefault(id) ?? throw new ApiException(404, "unknown_project", $"Unknown project: {id}");
  public async Task<ProjectInfo> ImportFolderAsync(string source, CancellationToken token)
  {
    if (string.IsNullOrWhiteSpace(source))
    {
      throw ProjectPaths.Invalid("path is required for folder import");
    }

    return await ImportAsync(stage => PrepareFolderAsync(source, stage, token), token);
  }

  public Task<ProjectInfo> ImportZipAsync(Stream input, CancellationToken token)
  {
    return ImportAsync(stage => PrepareZipAsync(input, stage, token), token);
  }

  private async Task PrepareFolderAsync(string source, string stage, CancellationToken token)
  {
    string full;
    try
    {
      full = Path.GetFullPath(source);
    }
    catch (ArgumentException)
    {
      throw ProjectPaths.Invalid("Invalid folder path");
    }

    if (!Directory.Exists(full))
    {
      throw ProjectPaths.Invalid($"Project folder does not exist: {source}");
    }

    string stageRelative = Path.GetRelativePath(full, stage);
    if (!Path.IsPathRooted(stageRelative) && stageRelative != ".." && !stageRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    {
      throw ProjectPaths.Invalid("Folder import must not include simulator storage; select the project folder itself");
    }

    ProjectPaths.RejectLink(full);
    await CopyFolderAsync(full, Path.Combine(stage, "contents"), token);
  }

  private async Task PrepareZipAsync(Stream input, string stage, CancellationToken token)
  {
    string upload = Path.Combine(stage, "upload.zip");
    await using (FileStream spool = File.Create(upload))
    {
      await CopyLimitedAsync(input, spool, maximumBytes, token);
    }

    string contents = Path.Combine(stage, "contents");
    Directory.CreateDirectory(contents);
    using ZipArchive archive = ZipFile.OpenRead(upload);
    if (archive.Entries.Count > maximumFiles * 2L)
    {
      throw Limit();
    }

    HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    long total = 0;
    int files = 0;
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      token.ThrowIfCancellationRequested();
      int kind = (entry.ExternalAttributes >> 16) & ZipFileKindMask;
      if (kind != 0 && kind != ZipRegularFileKind && kind != ZipDirectoryKind)
      {
        throw ProjectPaths.Invalid($"ZIP entry is not a regular file or directory: {entry.FullName}");
      }

      bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
      string name = ProjectPaths.Normalize(isDirectory ? entry.FullName[..^1] : entry.FullName);
      if (!paths.Add(name))
      {
        throw ProjectPaths.Invalid($"Duplicate ZIP path: {entry.FullName}");
      }

      string target = ProjectPaths.Resolve(contents, name, mustExist: false);
      if (isDirectory)
      {
        Directory.CreateDirectory(target);
        continue;
      }

      if (++files > maximumFiles || entry.Length > maximumBytes - total)
      {
        throw Limit();
      }

      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      await using Stream source = entry.Open();
      await using FileStream destination = new(target, FileMode.CreateNew, FileAccess.Write);
      total += await CopyLimitedAsync(source, destination, maximumBytes - total, token);
    }
  }

  private async Task<ProjectInfo> ImportAsync(Func<string, Task> prepare, CancellationToken token)
  {
    await importGate.WaitAsync(token);
    string stage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(stage);
      await prepare(stage);
      string contents = Path.Combine(stage, "contents");
      string[] manifests = Directory.GetFiles(contents, "experiment.json", SearchOption.AllDirectories);
      if (manifests.Length != 1)
      {
        throw ProjectPaths.Invalid("Package must contain exactly one experiment.json at its root or inside a single project folder");
      }

      string packageRoot = Path.GetDirectoryName(manifests[0])!;
      CityProject project = CityProjectLoader.Load(packageRoot);
      if (projects.ContainsKey(project.Manifest.Id))
      {
        throw new ApiException(409, "duplicate_project", $"Project ID already imported: {project.Manifest.Id}");
      }

      token.ThrowIfCancellationRequested();
      return await StoreProjectAsync(stage, packageRoot, project, token);
    }
    catch (InvalidDataException ex)
    {
      throw ProjectPaths.Invalid($"Unreadable ZIP package: {ex.Message}");
    }
    catch (IOException ex)
    {
      throw ProjectPaths.Invalid($"Project files could not be read or copied: {ex.Message}");
    }
    catch (UnauthorizedAccessException)
    {
      throw ProjectPaths.Invalid("Project files are not readable by the simulator service");
    }
    finally
    {
      try
      {
        if (Directory.Exists(stage))
        {
          Directory.Delete(stage, recursive: true);
        }
      }
      finally
      {
        importGate.Release();
      }
    }
  }

  private async Task<ProjectInfo> StoreProjectAsync(string stage, string packageRoot, CityProject project, CancellationToken token)
  {
    // Place all public project files below package/. Private catalog metadata stays outside it.
    string commit = Path.Combine(stage, "commit");
    Directory.CreateDirectory(commit);
    string package = Path.Combine(commit, "package");
    Directory.Move(packageRoot, package);
    StoredProject metadata = new(1, project.Manifest.Id, project.Revision, DateTimeOffset.UtcNow);
    string metadataJson = JsonSerializer.Serialize(metadata, JsonDefaults.Options);
    await File.WriteAllTextAsync(Path.Combine(commit, "import.json"), metadataJson, token);
    string destination = Path.Combine(root, Guid.NewGuid().ToString("N"));
    Directory.Move(commit, destination);
    CityProject stored = new()
    {
      Directory = Path.Combine(destination, "package"),
      Manifest = project.Manifest,
      Demand = project.Demand,
      Edges = project.Edges,
      Junctions = project.Junctions,
      Connections = project.Connections,
      SignalIds = project.SignalIds,
      Terminals = project.Terminals,
      Revision = project.Revision
    };
    projects[stored.Manifest.Id] = stored;
    return stored.Info;
  }

  private async Task CopyFolderAsync(string source, string target, CancellationToken token)
  {
    Directory.CreateDirectory(target);
    Stack<string> pending = new();
    pending.Push(source);
    HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    long total = 0;
    int files = 0, directories = 0;
    while (pending.TryPop(out string? directory))
    {
      foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
      {
        token.ThrowIfCancellationRequested();
        ProjectPaths.RejectLink(entry);
        string relative = ProjectPaths.Normalize(Path.GetRelativePath(source, entry));
        if (!paths.Add(relative))
        {
          throw ProjectPaths.Invalid($"Duplicate project path: {relative}");
        }

        string destination = ProjectPaths.Resolve(target, relative, mustExist: false);
        if (Directory.Exists(entry))
        {
          if (++directories > maximumFiles)
          {
            throw Limit();
          }

          Directory.CreateDirectory(destination);
          pending.Push(entry);
          continue;
        }

        if (++files > maximumFiles || new FileInfo(entry).Length > maximumBytes - total)
        {
          throw Limit();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream input = File.OpenRead(entry);
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write);
        total += await CopyLimitedAsync(input, output, maximumBytes - total, token);
      }
    }
  }

  private async Task<long> CopyLimitedAsync(Stream source, Stream destination, long remaining, CancellationToken token)
  {
    byte[] buffer = new byte[CopyBufferSize];
    long copied = 0;
    int count;
    while ((count = await source.ReadAsync(buffer, token)) > 0)
    {
      if (count > remaining - copied)
      {
        throw Limit();
      }

      await destination.WriteAsync(buffer.AsMemory(0, count), token);
      copied += count;
    }

    return copied;
  }

  private ApiException Limit() => new(413, "project_too_large", $"Project import exceeds limits: {maximumBytes} bytes and {maximumFiles} files");
}

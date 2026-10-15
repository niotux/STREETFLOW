using System.IO.Compression;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

// Compatibility uploads extract once to a normal folder and register its path.
public sealed partial class ProjectCatalog
{
  private const int CopyBufferSize = 65536;
  private const int ZipFileKindMask = 0xf000;
  private const int ZipRegularFileKind = 0x8000;
  private const int ZipDirectoryKind = 0x4000;

  public async Task<ProjectInfo> ImportZipAsync(Stream input, CancellationToken token)
  {
    string stage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
    string? destination = null;
    bool registered = false;
    try
    {
      Directory.CreateDirectory(stage);
      await PrepareZipAsync(input, stage, token);
      string contents = Path.Combine(stage, "contents");
      string[] manifests = Directory.GetFiles(contents, "experiment.json", SearchOption.AllDirectories);
      if (manifests.Length != 1)
      {
        throw ProjectPaths.Invalid("Package must contain exactly one experiment.json at its root or inside a single project folder");
      }

      string packageRoot = Path.GetDirectoryName(manifests[0])!;
      CityProject project = LoadFolder(packageRoot);
      destination = Path.Combine(root, project.Manifest.Id + "-" + Guid.NewGuid().ToString("N"));
      Directory.Move(packageRoot, destination);
      ProjectInfo info = await ImportFolderAsync(destination, token);
      registered = true;
      return info;
    }
    catch (InvalidDataException exception)
    {
      throw ProjectPaths.Invalid($"Unreadable ZIP package: {exception.Message}");
    }
    catch (IOException exception)
    {
      throw ProjectPaths.Invalid($"Project files could not be read: {exception.Message}");
    }
    finally
    {
      if (!registered && destination is not null && Directory.Exists(destination))
      {
        Directory.Delete(destination, recursive: true);
      }

      if (Directory.Exists(stage))
      {
        Directory.Delete(stage, recursive: true);
      }
    }
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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

// A ZIP in the projects folder is extracted once per file version; replacing or removing the ZIP retires its cache entry.
public sealed partial class ProjectCatalog
{
  private const int CopyBufferSize = 65536;
  private const int ZipFileKindMask = 0xf000;
  private const int ZipRegularFileKind = 0x8000;
  private const int ZipDirectoryKind = 0x4000;
  private readonly Dictionary<string, string> zipFailures = new(StringComparer.Ordinal);

  private async Task<ScannedSource> ReadZipAsync(string name, string path, HashSet<string> cacheKeys, CancellationToken token)
  {
    string key;
    try
    {
      FileInfo file = new(path);
      key = CacheKey(name, file.Length, file.LastWriteTimeUtc.Ticks);
    }
    catch (Exception exception) when (IsReadError(exception))
    {
      return Unavailable(name, ProjectSource.ZipKind, ReadErrorMessage(exception));
    }

    cacheKeys.Add(key);
    if (zipFailures.TryGetValue(key, out string? failure))
    {
      return Unavailable(name, ProjectSource.ZipKind, failure);
    }

    string extracted = Path.Combine(cacheRoot, key);
    try
    {
      if (!Directory.Exists(extracted))
      {
        await ExtractAsync(path, extracted, token);
      }

      CityProject project = CityProjectLoader.Load(extracted);
      return new ScannedSource(new ProjectSource(name, ProjectSource.ZipKind, project.Info, null), project);
    }
    catch (Exception exception) when (IsReadError(exception))
    {
      string message = exception is InvalidDataException ? $"Unreadable ZIP file: {exception.Message}" : ReadErrorMessage(exception);
      zipFailures[key] = message;
      return Unavailable(name, ProjectSource.ZipKind, message);
    }
  }

  private static string CacheKey(string name, long length, long modified)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{name}\n{length}\n{modified}"));
    return Convert.ToHexStringLower(hash.AsSpan(0, 16));
  }

  private async Task ExtractAsync(string zip, string destination, CancellationToken token)
  {
    string stage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(stage);
      await PrepareZipAsync(zip, stage, token);
      string[] manifests = Directory.GetFiles(stage, ManifestFile, SearchOption.AllDirectories);
      if (manifests.Length != 1)
      {
        throw ProjectPaths.Invalid($"ZIP must contain exactly one {ManifestFile} at its root or inside a single project folder");
      }

      Directory.Move(Path.GetDirectoryName(manifests[0])!, destination);
    }
    finally
    {
      if (Directory.Exists(stage))
      {
        Directory.Delete(stage, recursive: true);
      }
    }
  }

  private void PruneCache(HashSet<string> current)
  {
    foreach (string directory in Directory.EnumerateDirectories(cacheRoot))
    {
      if (!current.Contains(Path.GetFileName(directory)))
      {
        Directory.Delete(directory, recursive: true);
      }
    }

    foreach (string key in zipFailures.Keys.ToArray())
    {
      if (!current.Contains(key))
      {
        zipFailures.Remove(key);
      }
    }
  }

  private async Task PrepareZipAsync(string zip, string contents, CancellationToken token)
  {
    if (new FileInfo(zip).Length > maximumBytes)
    {
      throw Limit();
    }

    using ZipArchive archive = ZipFile.OpenRead(zip);
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

  private ApiException Limit() => new(413, "project_too_large", $"Project ZIP exceeds limits: {maximumBytes} bytes and {maximumFiles} files");
}

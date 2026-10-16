using System.Net.Http.Json;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

// Tests add projects the way users do: by placing a folder or ZIP in the projects folder.
public static class ProjectsFolder
{
  public static async Task<ProjectInfo> AddProjectAsync(this BackendFactory factory, HttpClient client, string source)
  {
    ProjectSource added = await factory.AddSourceAsync(client, source);
    Assert.True(added.Project is not null, added.Error);
    return added.Project;
  }

  public static async Task<ProjectSource> AddSourceAsync(this BackendFactory factory, HttpClient client, string source, string? name = null)
  {
    name ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
    CopyDirectory(source, Path.Combine(factory.ProjectsRoot, name));
    return await FindAsync(client, name);
  }

  public static async Task<ProjectSource> AddZipAsync(this BackendFactory factory, HttpClient client, byte[] zip, string name)
  {
    Directory.CreateDirectory(factory.ProjectsRoot);
    await File.WriteAllBytesAsync(Path.Combine(factory.ProjectsRoot, name), zip);
    return await FindAsync(client, name);
  }

  public static async Task<ProjectSource> FindAsync(HttpClient client, string name)
  {
    ProjectSource[] sources = (await client.GetFromJsonAsync<ProjectSource[]>("/api/v1/projects/sources", JsonDefaults.Options))!;
    return Assert.Single(sources, source => source.Name == name);
  }

  private static void CopyDirectory(string source, string target)
  {
    Directory.CreateDirectory(target);
    foreach (string file in Directory.EnumerateFiles(source))
    {
      File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }

    foreach (string directory in Directory.EnumerateDirectories(source))
    {
      CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
  }
}

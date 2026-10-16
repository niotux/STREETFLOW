using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class ProjectSourcesTests
{
  [Theory]
  [InlineData("tiny-graph", 1)]
  [InlineData("four-way-transformer-routes", 2)]
  [InlineData("four-way-neural-routes", 3)]
  public async Task EveryFormatIsListedFromTheProjectsFolderInPlace(string id, int version)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    ProjectSource source = await factory.AddSourceAsync(client, Draft(id));
    Assert.Equal(ProjectSource.FolderKind, source.Kind);
    Assert.Null(source.Error);
    Assert.Equal(version, source.Project!.Manifest.FormatVersion);
    Assert.Equal(Path.Combine(factory.ProjectsRoot, id), source.Project.Path);
    Assert.Equal(id, Assert.Single((await client.GetFromJsonAsync<ProjectInfo[]>("/api/v1/projects", JsonDefaults.Options))!).Manifest.Id);
  }

  [Fact]
  public async Task FolderEditsAreReadOnRefreshAndInvalidFoldersStayVisible()
  {
    string dataRoot;
    string name;
    await using (BackendFactory factory = new())
    {
      using ProjectFixture project = new(factory.ProjectsRoot);
      name = Path.GetFileName(project.Path);
      using HttpClient client = factory.CreateClient();
      Assert.NotNull((await ProjectsFolder.FindAsync(client, name)).Project);
      project.ChangeManifest(manifest => manifest["network"]!["file"] = "missing.net.xml");
      ProjectSource invalid = await ProjectsFolder.FindAsync(client, name);
      Assert.Null(invalid.Project);
      Assert.Contains("missing.net.xml", invalid.Error);
      Assert.Empty((await client.GetFromJsonAsync<ProjectInfo[]>("/api/v1/projects", JsonDefaults.Options))!);
      project.ChangeManifest(manifest => manifest["network"]!["file"] = "network.net.xml");
      Assert.NotNull((await ProjectsFolder.FindAsync(client, name)).Project);
      project.ChangeManifest(manifest => manifest["id"] = "renamed-project");
      Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/projects/tiny-graph")).StatusCode);
      Assert.Equal(project.Path, (await client.GetFromJsonAsync<ProjectInfo>("/api/v1/projects/renamed-project", JsonDefaults.Options))!.Path);
      dataRoot = factory.DataRoot;
      // Keep the folder after this scope so the restarted service reads the same projects folder.
      Directory.Move(project.Path, project.Path + "-kept");
      name += "-kept";
    }

    await using BackendFactory restarted = new(dataRoot);
    using HttpClient reopened = restarted.CreateClient();
    Assert.Equal("renamed-project", (await ProjectsFolder.FindAsync(reopened, name)).Project!.Manifest.Id);
  }

  [Fact]
  public async Task NonProjectEntriesAreReportedOrIgnored()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    Assert.Empty((await client.GetFromJsonAsync<ProjectSource[]>("/api/v1/projects/sources", JsonDefaults.Options))!);
    Directory.CreateDirectory(Path.Combine(factory.ProjectsRoot, "notes"));
    Directory.CreateDirectory(Path.Combine(factory.ProjectsRoot, ".hidden"));
    File.WriteAllText(Path.Combine(factory.ProjectsRoot, "README.md"), "guide");
    ProjectSource notes = Assert.Single((await client.GetFromJsonAsync<ProjectSource[]>("/api/v1/projects/sources", JsonDefaults.Options))!);
    Assert.Equal("notes", notes.Name);
    Assert.Contains("No experiment.json", notes.Error);
  }

  [Fact]
  public async Task DuplicateIdsKeepTheFirstEntryInNameOrder()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddSourceAsync(client, Draft("tiny-graph"), "b-copy");
    ProjectSource first = await factory.AddSourceAsync(client, Draft("tiny-graph"), "a-copy");
    ProjectSource second = await ProjectsFolder.FindAsync(client, "b-copy");
    Assert.NotNull(first.Project);
    Assert.Null(second.Project);
    Assert.Contains("Duplicate project ID", second.Error);
    Assert.Equal(first.Project.Path, Assert.Single((await client.GetFromJsonAsync<ProjectInfo[]>("/api/v1/projects", JsonDefaults.Options))!).Path);
  }

  [Fact]
  public async Task ZipFilesAreExtractedOnceAndRetiredWhenReplacedOrRemoved()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    string cache = Path.Combine(factory.DataRoot, "project-cache");
    ProjectSource zipped = await factory.AddZipAsync(client, Zip(Draft("tiny-graph"), ""), "tiny.zip");
    Assert.Equal(ProjectSource.ZipKind, zipped.Kind);
    Assert.Equal("tiny-graph", zipped.Project!.Manifest.Id);
    string extracted = Assert.Single(Directory.GetDirectories(cache));
    await ProjectsFolder.FindAsync(client, "tiny.zip");
    Assert.Equal(extracted, Assert.Single(Directory.GetDirectories(cache)));

    File.Delete(Path.Combine(factory.ProjectsRoot, "tiny.zip"));
    ProjectSource wrapped = await factory.AddZipAsync(client, Zip(Draft("four-way-transformer-routes"), "wrapped/"), "tiny.zip");
    Assert.Equal("four-way-transformer-routes", wrapped.Project!.Manifest.Id);
    Assert.NotEqual(extracted, Assert.Single(Directory.GetDirectories(cache)));

    File.Delete(Path.Combine(factory.ProjectsRoot, "tiny.zip"));
    Assert.Empty((await client.GetFromJsonAsync<ProjectSource[]>("/api/v1/projects/sources", JsonDefaults.Options))!);
    Assert.Empty(Directory.GetDirectories(cache));

    ProjectSource corrupt = await factory.AddZipAsync(client, [1, 2, 3], "corrupt.zip");
    Assert.Null(corrupt.Project);
    Assert.Contains("ZIP", corrupt.Error);
  }

  [Fact]
  public async Task LoadingReplacesTheLoadedSimulationAndUnloadLeavesNoneLoaded()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, Draft("tiny-graph"));
    await factory.AddProjectAsync(client, Draft("four-way-transformer-routes"));
    Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/v1/city-runs/loaded")).StatusCode);

    CityRunStatus first = await Load(client, "tiny-graph");
    (await client.PostAsJsonAsync($"/api/v1/city-runs/{first.Id}/resume", new CityRunCommand(), JsonDefaults.Options)).EnsureSuccessStatusCode();
    CityRunStatus second = await Load(client, "four-way-transformer-routes");
    Assert.Equal("paused", second.State);
    Assert.Equal("stopped", (await client.GetFromJsonAsync<CityRunStatus>($"/api/v1/city-runs/{first.Id}", JsonDefaults.Options))!.State);
    Assert.Equal(second.Id, (await client.GetFromJsonAsync<CityRunStatus>("/api/v1/city-runs/loaded", JsonDefaults.Options))!.Id);

    // Stopping keeps the project loaded, so Runner can still replay it.
    (await client.PostAsJsonAsync($"/api/v1/city-runs/{second.Id}/stop", new CityRunCommand(), JsonDefaults.Options)).EnsureSuccessStatusCode();
    Assert.Equal("stopped", (await client.GetFromJsonAsync<CityRunStatus>("/api/v1/city-runs/loaded", JsonDefaults.Options))!.State);

    CityRunStatus third = await Load(client, "tiny-graph");
    Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/city-runs/loaded")).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/v1/city-runs/loaded")).StatusCode);
    Assert.Equal("stopped", (await client.GetFromJsonAsync<CityRunStatus>($"/api/v1/city-runs/{third.Id}", JsonDefaults.Options))!.State);
    Assert.Equal(3, (await client.GetFromJsonAsync<CityRunStatus[]>("/api/v1/city-runs", JsonDefaults.Options))!.Length);
    Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/city-runs/loaded")).StatusCode);
  }

  private static string Draft(string id) => Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", id);

  private static async Task<CityRunStatus> Load(HttpClient client, string id)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/projects/{id}/runs", new CreateCityRunRequest(42, StartPaused: true), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(status.State == "paused", status.Failure);
    return status;
  }

  private static byte[] Zip(string folder, string prefix)
  {
    using MemoryStream buffer = new();
    using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (string file in Directory.GetFiles(folder))
      {
        archive.CreateEntryFromFile(file, prefix + Path.GetFileName(file));
      }
    }

    return buffer.ToArray();
  }
}

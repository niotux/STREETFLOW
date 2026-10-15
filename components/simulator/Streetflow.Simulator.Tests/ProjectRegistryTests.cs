using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class ProjectRegistryTests
{
  [Theory]
  [InlineData("tiny-graph", 1)]
  [InlineData("four-way-transformer-routes", 2)]
  [InlineData("four-way-neural-routes", 3)]
  public async Task EveryFormatRegistersItsOriginalFolderWithoutCopying(string id, int version)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    string path = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", id);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(path), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    ProjectInfo project = (await response.Content.ReadFromJsonAsync<ProjectInfo>(JsonDefaults.Options))!;
    Assert.Equal(path, project.Path);
    Assert.Equal(version, project.Manifest.FormatVersion);
    ProjectFolder[] registry = JsonSerializer.Deserialize<ProjectFolder[]>(File.ReadAllText(Path.Combine(factory.DataRoot, "project-folders.json")), JsonDefaults.Options)!;
    Assert.Equal(path, Assert.Single(registry).Path);
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(factory.DataRoot, "projects")));
    response = await client.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(path + Path.DirectorySeparatorChar), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    ProjectFolderStatus[] folders = (await client.GetFromJsonAsync<ProjectFolderStatus[]>("/api/v1/projects/folders", JsonDefaults.Options))!;
    Assert.Null(Assert.Single(folders).Error);
  }

  [Fact]
  public async Task InvalidOrMissingFoldersRemainVisibleAndCanBeRemovedWithoutDeletingFiles()
  {
    using ProjectFixture source = new();
    string dataRoot;
    await using (BackendFactory factory = new())
    {
      dataRoot = factory.DataRoot;
      using HttpClient client = factory.CreateClient();
      (await client.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(source.Path), JsonDefaults.Options)).EnsureSuccessStatusCode();
      source.ChangeManifest(manifest => manifest["network"]!["file"] = "missing.net.xml");
      ProjectFolderStatus folder = Assert.Single((await client.GetFromJsonAsync<ProjectFolderStatus[]>("/api/v1/projects/folders", JsonDefaults.Options))!);
      Assert.Null(folder.Project);
      Assert.Contains("missing.net.xml", folder.Error);
      Assert.Empty((await client.GetFromJsonAsync<ProjectInfo[]>("/api/v1/projects", JsonDefaults.Options))!);
    }

    await using BackendFactory restarted = new(dataRoot);
    using HttpClient reopened = restarted.CreateClient();
    ProjectFolderStatus invalid = Assert.Single((await reopened.GetFromJsonAsync<ProjectFolderStatus[]>("/api/v1/projects/folders", JsonDefaults.Options))!);
    Assert.NotNull(invalid.Error);
    source.ChangeManifest(manifest => manifest["network"]!["file"] = "network.net.xml");
    Assert.Single((await reopened.GetFromJsonAsync<ProjectInfo[]>("/api/v1/projects", JsonDefaults.Options))!);
    string moved = source.Path + "-moved";
    Directory.Move(source.Path, moved);
    try
    {
      ProjectFolderStatus missing = Assert.Single((await reopened.GetFromJsonAsync<ProjectFolderStatus[]>("/api/v1/projects/folders", JsonDefaults.Options))!);
      Assert.Contains("does not exist", missing.Error);
      await Remove(reopened, source.Path);
      Assert.Empty((await reopened.GetFromJsonAsync<ProjectFolderStatus[]>("/api/v1/projects/folders", JsonDefaults.Options))!);
    }
    finally
    {
      Directory.Move(moved, source.Path);
    }

    (await reopened.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(source.Path), JsonDefaults.Options)).EnsureSuccessStatusCode();
    await Remove(reopened, source.Path);
    Assert.True(File.Exists(Path.Combine(source.Path, "experiment.json")));
    ProjectCatalog persisted = Catalog(dataRoot);
    Assert.Empty(persisted.ListFolders());
  }

  [Fact]
  public async Task ExistingDiskImportsMigrateToFolderReferencesOnlyOnce()
  {
    using ProjectFixture source = new();
    string dataRoot = Path.Combine(Path.GetTempPath(), "streetflow-registry-tests", Guid.NewGuid().ToString("N"));
    string stored = Path.Combine(dataRoot, "projects", "legacy-import");
    string package = Path.Combine(stored, "package");
    Directory.CreateDirectory(package);
    try
    {
      foreach (string file in Directory.GetFiles(source.Path))
      {
        File.Copy(file, Path.Combine(package, Path.GetFileName(file)));
      }

      File.WriteAllText(Path.Combine(stored, "import.json"), "{}");
      ProjectCatalog catalog = Catalog(dataRoot);
      Assert.Equal(package, Assert.Single(catalog.List()).Path);
      Assert.True(File.Exists(Path.Combine(package, "experiment.json")));
      await catalog.RemoveFolderAsync(package, CancellationToken.None);
      Assert.Empty(Catalog(dataRoot).List());
      Assert.True(Directory.Exists(package));
    }
    finally
    {
      Directory.Delete(dataRoot, recursive: true);
    }
  }

  [Fact]
  public async Task ManifestIdentityChangesAreReadFromTheRegisteredFolder()
  {
    using ProjectFixture source = new();
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    (await client.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(source.Path), JsonDefaults.Options)).EnsureSuccessStatusCode();
    source.ChangeManifest(manifest => manifest["id"] = "renamed-project");
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/projects/tiny-graph")).StatusCode);
    ProjectInfo project = (await client.GetFromJsonAsync<ProjectInfo>("/api/v1/projects/renamed-project", JsonDefaults.Options))!;
    Assert.Equal(source.Path, project.Path);
  }

  [Fact]
  public async Task ExternalIdentityCollisionsAreReportedAndCanBeUnregistered()
  {
    using ProjectFixture first = new();
    using ProjectFixture second = new();
    second.ChangeManifest(manifest => manifest["id"] = "other-project");
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    foreach (string path in new[] { first.Path, second.Path })
    {
      (await client.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(path), JsonDefaults.Options)).EnsureSuccessStatusCode();
    }

    second.ChangeManifest(manifest => manifest["id"] = "tiny-graph");
    ProjectFolderStatus[] folders = (await client.GetFromJsonAsync<ProjectFolderStatus[]>("/api/v1/projects/folders", JsonDefaults.Options))!;
    Assert.NotNull(folders[0].Project);
    Assert.Null(folders[1].Project);
    Assert.Contains("Duplicate project ID", folders[1].Error);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/folders", new FolderImportRequest(second.Path), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    await Remove(client, second.Path);
    Assert.Equal(first.Path, Assert.Single((await client.GetFromJsonAsync<ProjectInfo[]>("/api/v1/projects", JsonDefaults.Options))!).Path);
  }

  private static ProjectCatalog Catalog(string dataRoot) => new(new SimulatorOptions { DataRoot = dataRoot, MaximumProjectBytes = 256L * 1024 * 1024, MaximumProjectFiles = 4096 });

  private static async Task Remove(HttpClient client, string path)
  {
    using HttpRequestMessage request = new(HttpMethod.Delete, "/api/v1/projects/folders");
    request.Content = JsonContent.Create(new FolderImportRequest(path), options: JsonDefaults.Options);
    using HttpResponseMessage response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
  }
}

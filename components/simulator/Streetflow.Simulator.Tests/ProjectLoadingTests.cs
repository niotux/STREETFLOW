using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class ProjectLoadingTests
{
  internal static string Workspace
  {
    get
    {
      DirectoryInfo? directory = new(AppContext.BaseDirectory);
      while (directory is not null)
      {
        if (File.Exists(Path.Combine(directory.FullName, "Streetflow.slnx")))
        {
          break;
        }

        directory = directory.Parent;
      }

      return directory?.FullName ?? throw new InvalidOperationException("Workspace not found");
    }
  }

  private static string Draft(string id) => Path.Combine(Workspace, "experiments", "drafts", id);
  private static async Task<JsonElement> Body(HttpResponseMessage response)
  {
    string text = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {text}");
    return JsonSerializer.Deserialize<JsonElement>(text);
  }

  private static Task<HttpResponseMessage> ImportFolder(HttpClient client, string path) => client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(path), JsonDefaults.Options);
  private static async Task<ApiError> Error(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.BadRequest)
  {
    Assert.Equal(status, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<ApiError>(JsonDefaults.Options))!;
  }

  [Fact]
  public async Task AllThreeProjectsLoadMapsWithoutStartingRuns()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    foreach (ProjectExpectation expected in new ProjectExpectation[]
    {
      new("tiny-graph", 8),
      new("iasi-central-day", 713),
      new("podu-ros", 106)
    }

    )
    {
      HttpResponseMessage response = await ImportFolder(client, Draft(expected.Id));
      Assert.Equal(HttpStatusCode.Created, response.StatusCode);
      JsonElement info = await Body(response);
      Assert.Equal(expected.EdgeCount, info.GetProperty("usable_edge_count").GetInt32());
      Assert.Equal(64, info.GetProperty("revision").GetString()!.Length);
      JsonElement map = await Body(await client.GetAsync($"/api/v1/projects/{expected.Id}/map?limit=2"));
      Assert.Equal(2, map.GetProperty("lanes").GetArrayLength());
      Assert.True(map.GetProperty("lanes")[0].GetProperty("shape").GetArrayLength() >= 2);
      Assert.True(map.GetProperty("total_lanes").GetInt32() > 2);
      JsonElement demand = await Body(await client.GetAsync($"/api/v1/projects/{expected.Id}/demand"));
      Assert.Equal(expected.EdgeCount, demand.GetProperty("profiles").GetArrayLength());
    }

    Assert.Equal(3, (await Body(await client.GetAsync("/api/v1/projects"))).GetArrayLength());
    Assert.Equal(0, (await Body(await client.GetAsync("/api/v1/runs"))).GetArrayLength());
    Assert.Equal(2, (await Body(await client.GetAsync("/api/v1/scenarios"))).GetArrayLength());
    Assert.Null(factory.Services.GetRequiredService<RunCoordinator>().EngineProcessId);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/projects/tiny-graph/map?limit=0")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/projects/absent")).StatusCode);
  }

  [Fact]
  public async Task FolderReferencesReadExternalEditsAndSurviveServiceRestart()
  {
    using ProjectFixture source = new();
    string dataRoot;
    string revision;
    await using (BackendFactory factory = new())
    {
      dataRoot = factory.DataRoot;
      using HttpClient client = factory.CreateClient();
      JsonElement imported = await Body(await ImportFolder(client, source.Path));
      Assert.Equal(source.Path, imported.GetProperty("path").GetString());
      source.ChangeDemand(d => d["profiles"]![0]!["intervals"]![0]!["weight"] = 10);
      JsonElement stored = await Body(await client.GetAsync("/api/v1/projects/tiny-graph/demand"));
      Assert.Equal(10, stored.GetProperty("profiles")[0].GetProperty("intervals")[0].GetProperty("weight").GetInt32());
      JsonElement changed = await Body(await client.GetAsync("/api/v1/projects/tiny-graph"));
      revision = changed.GetProperty("revision").GetString()!;
      Assert.NotEqual(imported.GetProperty("revision").GetString(), revision);
      Assert.Empty(Directory.EnumerateDirectories(Path.Combine(dataRoot, "projects")));
    }

    await using BackendFactory restarted = new(dataRoot);
    using HttpClient reopened = restarted.CreateClient();
    JsonElement project = await Body(await reopened.GetAsync("/api/v1/projects/tiny-graph"));
    Assert.Equal(revision, project.GetProperty("revision").GetString());
    Assert.Equal(source.Path, project.GetProperty("path").GetString());
    Assert.Equal(0, (await Body(await reopened.GetAsync("/api/v1/runs"))).GetArrayLength());
  }

  [Theory]
  [InlineData("")]
  [InlineData("wrapped-project/")]
  public async Task ZipImportsAcceptFlatAndWrappedPackages(string prefix)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    using MemoryStream zip = PackageZip(Draft("podu-ros"), prefix);
    using StreamContent content = new(zip);
    HttpResponseMessage response = await client.PostAsync("/api/v1/projects/import/zip", content);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal(106, (await Body(response)).GetProperty("usable_edge_count").GetInt32());
    Assert.Equal(68940, (await Body(await client.GetAsync("/api/v1/projects/podu-ros"))).GetProperty("manifest").GetProperty("defaults").GetProperty("start_time_seconds").GetInt32());
  }

  [Fact]
  public async Task DuplicateIdsNeverOverwriteAnImportedProject()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    JsonElement original = await Body(await ImportFolder(client, Draft("tiny-graph")));
    using ProjectFixture changed = new();
    changed.ChangeDemand(d => d["profiles"]![0]!["intervals"]![0]!["weight"] = 10);
    ApiError error = await Error(await ImportFolder(client, changed.Path), HttpStatusCode.Conflict);
    Assert.Equal("duplicate_project", error.Code);
    JsonElement after = await Body(await client.GetAsync("/api/v1/projects/tiny-graph"));
    Assert.Equal(original.GetProperty("revision").GetString(), after.GetProperty("revision").GetString());
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(factory.DataRoot, ".project-imports")));
  }

  [Theory]
  [InlineData("missing", "missing traffic profile")]
  [InlineData("duplicate", "duplicate profile")]
  [InlineData("unknown", "unknown or unusable")]
  [InlineData("gap", "contiguous")]
  [InlineData("overlap", "contiguous")]
  [InlineData("short-day", "full day")]
  [InlineData("negative", "nonnegative")]
  [InlineData("all-zero", "all-zero")]
  [InlineData("null-profile", "null edge profile")]
  [InlineData("null-interval", "null interval")]
  [InlineData("null-settings", "cannot be null")]
  [InlineData("wrong-version", "format_version")]
  [InlineData("unknown-setting", "invalid JSON")]
  [InlineData("bad-class", "vehicle_class")]
  [InlineData("zero-types", "positive total")]
  [InlineData("bad-distances", "trip distances")]
  [InlineData("bad-rate", "mean_departures_per_hour")]
  [InlineData("no-candidates", "candidate_limit")]
  public async Task DemandErrorsIdentifyTheFileAndRelevantEdge(string mutation, string expected)
  {
    using ProjectFixture source = new();
    source.ChangeDemand(d =>
    {
      JsonArray profiles = d["profiles"]!.AsArray();
      switch (mutation)
      {
        case "missing":
          profiles.RemoveAt(0);
          break;
        case "duplicate":
          profiles.Add(profiles[0]!.DeepClone());
          break;
        case "unknown":
          profiles[0]!["edge"] = "does-not-exist";
          break;
        case "gap":
          profiles[0]!["intervals"]![0]!["start_seconds"] = 1;
          break;
        case "overlap":
          profiles[0]!["intervals"]!.AsArray().Add(profiles[0]!["intervals"]![0]!.DeepClone());
          break;
        case "short-day":
          profiles[0]!["intervals"]![0]!["end_seconds"] = 86399;
          break;
        case "negative":
          profiles[0]!["intervals"]![0]!["weight"] = -1;
          break;
        case "all-zero":
          foreach (JsonNode? profile in profiles)
          {
            profile!["intervals"]![0]!["weight"] = 0;
          }

          break;
        case "null-profile":
          profiles[0] = null;
          break;
        case "null-interval":
          profiles[0]!["intervals"]![0] = null;
          break;
        case "null-settings":
          d["generation"] = null;
          break;
        case "wrong-version":
          d["format_version"] = 2;
          break;
        case "unknown-setting":
          d["spatial_prior"] = "invent-a-profile";
          break;
        case "bad-class":
          d["vehicle_types"]![0]!["vehicle_class"] = "invented";
          break;
        case "zero-types":
          d["vehicle_types"]![0]!["weight"] = 0;
          break;
        case "bad-distances":
          d["generation"]!["max_trip_distance_m"] = 10;
          break;
        case "bad-rate":
          d["generation"]!["mean_departures_per_hour"] = -1;
          break;
        case "no-candidates":
          d["generation"]!["candidate_limit"] = 0;
          break;
      }
    });
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    ApiError error = await Error(await ImportFolder(client, source.Path));
    Assert.Equal("invalid_project", error.Code);
    Assert.Contains("demand.json", error.Message);
    Assert.Contains(expected, error.Message);
    Assert.Equal(0, (await Body(await client.GetAsync("/api/v1/projects"))).GetArrayLength());
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(factory.DataRoot, ".project-imports")));
  }

  [Theory]
  [InlineData("../outside.xml")]
  [InlineData("/tmp/outside.xml")]
  [InlineData("C:\\outside.xml")]
  [InlineData("nested/../../outside.xml")]
  public async Task UnsafeManifestReferencesAreRejected(string reference)
  {
    using ProjectFixture source = new();
    source.ChangeManifest(m => m["network"]!["file"] = reference);
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    Assert.Contains("Unsafe project path", (await Error(await ImportFolder(client, source.Path))).Message);
  }

  [Theory]
  [InlineData("../escape.json")]
  [InlineData("/escape.json")]
  [InlineData("C:\\escape.json")]
  [InlineData("..\\escape.json")]
  public async Task UnsafeZipEntriesCannotWriteOutsideStaging(string name)
  {
    using MemoryStream zip = new();
    using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
    {
      using StreamWriter writer = new(archive.CreateEntry(name).Open());
      writer.Write("bad");
    }

    zip.Position = 0;
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    using StreamContent content = new(zip);
    Assert.Contains("Unsafe project path", (await Error(await client.PostAsync("/api/v1/projects/import/zip", content))).Message);
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(factory.DataRoot, ".project-imports")));
    Assert.False(File.Exists(Path.Combine(factory.DataRoot, "escape.json")));
  }

  [Fact]
  public async Task ZipSymlinksAndCaseCollisionsAreRejected()
  {
    foreach (bool symlink in new[]
    {
      true,
      false
    }

    )
    {
      using MemoryStream zip = new();
      using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
      {
        ZipArchiveEntry first = archive.CreateEntry("experiment.json");
        if (symlink)
        {
          first.ExternalAttributes = unchecked((int)0xa1ff0000);
        }
        else
        {
          archive.CreateEntry("EXPERIMENT.json");
        }
      }

      zip.Position = 0;
      await using BackendFactory factory = new();
      using HttpClient client = factory.CreateClient();
      using StreamContent content = new(zip);
      ApiError error = await Error(await client.PostAsync("/api/v1/projects/import/zip", content));
      Assert.Contains(symlink ? "not a regular file" : "Duplicate ZIP path", error.Message);
    }
  }

  [Theory]
  [InlineData("truncated-json")]
  [InlineData("duplicate-property")]
  [InlineData("missing-required-field")]
  [InlineData("missing-file")]
  [InlineData("detector-data")]
  [InlineData("bad-xml")]
  [InlineData("external-entity")]
  [InlineData("bad-connection")]
  [InlineData("bad-shape")]
  [InlineData("bad-elevation")]
  [InlineData("no-permitted-lanes")]
  [InlineData("signal-file")]
  public async Task InvalidPackageFilesFailWithUsefulErrors(string mutation)
  {
    using ProjectFixture source = new();
    string networkFile = Path.Combine(source.Path, "network.net.xml");
    switch (mutation)
    {
      case "truncated-json":
        File.WriteAllText(Path.Combine(source.Path, "experiment.json"), "{");
        break;
      case "duplicate-property":
        string manifest = File.ReadAllText(Path.Combine(source.Path, "experiment.json"));
        File.WriteAllText(Path.Combine(source.Path, "experiment.json"), "{\"format_version\":1," + manifest.TrimStart()[1..]);
        break;
      case "missing-required-field":
        source.ChangeManifest(m => m.AsObject().Remove("network"));
        break;
      case "missing-file":
        File.Delete(networkFile);
        break;
      case "detector-data":
        File.WriteAllText(Path.Combine(source.Path, "detectors.json"), "[{}]");
        break;
      case "bad-xml":
        File.WriteAllText(networkFile, "<net>");
        break;
      case "external-entity":
        File.WriteAllText(networkFile, "<!DOCTYPE net [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><net>&x;</net>");
        break;
      case "signal-file":
        File.WriteAllText(Path.Combine(source.Path, "extra.xml"), "<additional><vehicle id='authored'/></additional>");
        source.ChangeManifest(m => m["additional_files"] = new JsonArray("extra.xml"));
        break;
      default:
        XDocument net = XDocument.Load(networkFile);
        if (mutation == "bad-connection")
        {
          net.Root!.Element("connection")!.SetAttributeValue("from", "absent");
        }

        if (mutation == "bad-shape")
        {
          FirstLane(net).SetAttributeValue("shape", "NaN,0 1,1");
        }

        if (mutation == "bad-elevation")
        {
          FirstLane(net).SetAttributeValue("shape", "0,0,NaN 1,1,0");
        }

        if (mutation == "no-permitted-lanes")
        {
          foreach (XElement lane in net.Descendants("lane"))
          {
            lane.SetAttributeValue("disallow", null);
            lane.SetAttributeValue("allow", "bus");
          }
        }

        net.Save(networkFile);
        break;
    }

    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    ApiError error = await Error(await ImportFolder(client, source.Path));
    Assert.Equal("invalid_project", error.Code);
    Assert.True(error.Message.Contains(".json") || error.Message.Contains(".xml"), error.Message);
  }

  [Fact]
  public void InternalAndPedestrianOnlyEdgesDoNotRequireProfiles()
  {
    using ProjectFixture source = new();
    XDocument network = XDocument.Load(Path.Combine(source.Path, "network.net.xml"));
    XElement edge = new(FirstOrdinaryEdge(network));
    edge.SetAttributeValue("id", "walkway");
    foreach (XElement lane in edge.Elements("lane"))
    {
      lane.SetAttributeValue("id", "walkway_" + lane.Attribute("index")!.Value);
      lane.SetAttributeValue("allow", "pedestrian");
      lane.SetAttributeValue("disallow", null);
    }

    network.Root!.AddFirst(edge);
    network.Save(Path.Combine(source.Path, "network.net.xml"));
    CityProject project = CityProjectLoader.Load(source.Path);
    Assert.Equal(8, project.Info.UsableEdgeCount);
    Assert.Equal(9, project.Info.EdgeCount);
  }

  [Fact]
  public async Task ZipExpansionAndFileCountLimitsRejectBeforeRegistering()
  {
    string root = Path.Combine(Path.GetTempPath(), "streetflow-project-limit", Guid.NewGuid().ToString("N"));
    ProjectCatalog catalog = new(new SimulatorOptions { DataRoot = root, MaximumProjectBytes = 10000, MaximumProjectFiles = 4096 });
    using MemoryStream zip = PackageZip(Draft("tiny-graph"));
    Assert.True(zip.Length < 10000);
    ApiException ex = await Assert.ThrowsAsync<ApiException>(() => catalog.ImportZipAsync(zip, CancellationToken.None));
    Assert.Equal(413, ex.StatusCode);
    Assert.Empty(catalog.List());
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, ".project-imports")));
    ProjectCatalog fileLimited = new(new SimulatorOptions { DataRoot = root, MaximumProjectFiles = 1, MaximumProjectBytes = 256L * 1024 * 1024 });
    using MemoryStream secondZip = PackageZip(Draft("tiny-graph"));
    ex = await Assert.ThrowsAsync<ApiException>(() => fileLimited.ImportZipAsync(secondZip, CancellationToken.None));
    Assert.Equal(413, ex.StatusCode);
    Directory.Delete(root, true);
  }

  [Fact]
  public async Task CorruptZipAndMissingFolderReturnValidationErrors()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    using StringContent content = new("not a ZIP", Encoding.UTF8, "application/zip");
    Assert.Contains("ZIP", (await Error(await client.PostAsync("/api/v1/projects/import/zip", content))).Message);
    Assert.Contains("does not exist", (await Error(await ImportFolder(client, Path.Combine(factory.DataRoot, "absent")))).Message);
  }

  private static MemoryStream PackageZip(string source, string prefix = "")
  {
    MemoryStream stream = new();
    using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
      {
        ZipArchiveEntry entry = archive.CreateEntry(prefix + Path.GetRelativePath(source, file).Replace('\\', '/'), CompressionLevel.Optimal);
        using Stream output = entry.Open();
        using FileStream input = File.OpenRead(file);
        input.CopyTo(output);
      }
    }

    stream.Position = 0;
    return stream;
  }

  private static XElement FirstLane(XDocument network)
  {
    foreach (XElement lane in network.Descendants("lane"))
    {
      return lane;
    }

    throw new InvalidOperationException("No lane in fixture");
  }

  private static XElement FirstOrdinaryEdge(XDocument network)
  {
    foreach (XElement edge in network.Root!.Elements("edge"))
    {
      if (edge.Attribute("function") is null)
      {
        return edge;
      }
    }

    throw new InvalidOperationException("No ordinary edge in fixture");
  }
}

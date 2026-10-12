using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Streetflow.Simulator.Api;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class TerminalLoadingTests
{
  [Fact]
  public async Task ImportedTerminalsRemainVisibleAfterServiceRestartAndOnPagedMaps()
  {
    using ProjectFixture source = new();
    WriteTerminals(source, ValidTerminals());
    string dataRoot;
    TerminalMapPoint[] points;
    await using (BackendFactory factory = new())
    {
      dataRoot = factory.DataRoot;
      using HttpClient client = factory.CreateClient();
      HttpResponseMessage imported = await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(source.Path), JsonDefaults.Options);
      Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
      ProjectMapResponse? map = await client.GetFromJsonAsync<ProjectMapResponse>("/api/v1/projects/tiny-graph/map?limit=1", JsonDefaults.Options);
      Assert.NotNull(map);
      Assert.Single(map.Lanes);
      Assert.Equal(2, map.Terminals.Length);
      points = map.Terminals;
    }

    await using BackendFactory restarted = new(dataRoot);
    using HttpClient reopened = restarted.CreateClient();
    ProjectMapResponse? restored = await reopened.GetFromJsonAsync<ProjectMapResponse>("/api/v1/projects/tiny-graph/map", JsonDefaults.Options);
    Assert.NotNull(restored);
    Assert.Equal(points, restored.Terminals);
  }

  [Fact]
  public void TerminalsPersistInRunCopiesAndChangeRevision()
  {
    using ProjectFixture source = new();
    CityProject original = CityProjectLoader.Load(source.Path);
    Assert.Empty(original.Terminals);
    WriteTerminals(source, ValidTerminals());
    CityProject project = CityProjectLoader.Load(source.Path);
    Assert.NotEqual(original.Revision, project.Revision);
    TerminalMapPoint entry = project.TerminalPoints[0];
    Assert.Equal("west_in", entry.EdgeId);
    Assert.Equal(0, entry.X);
    Assert.Equal(198.4, entry.Y);
    TerminalMapPoint exit = project.TerminalPoints[1];
    Assert.Equal(400, exit.X);
    Assert.Equal(198.4, exit.Y);

    string storageRoot = Path.Combine(source.Path, "run-storage");
    CityRunStorage storage = new(new SimulatorOptions { DataRoot = storageRoot });
    CityProject copied = storage.CopyProject(project, "copied-run");
    Assert.Equal(project.Revision, copied.Revision);
    Assert.Equal(project.Terminals, copied.Terminals);
    Assert.Equal(project.TerminalPoints, copied.TerminalPoints);
  }

  [Fact]
  public void TerminalPositionFollowsPolylineWithScaledLaneLength()
  {
    using ProjectFixture source = new();
    ProjectTerminal[] terminals = ValidTerminals();
    terminals[0] = terminals[0] with { PositionM = 96.4 };
    WriteTerminals(source, terminals);
    TerminalMapPoint entry = CityProjectLoader.Load(source.Path).TerminalPoints[0];
    Assert.Equal(96.4, entry.X, 8);
    Assert.Equal(198.4, entry.Y, 8);
  }

  [Theory]
  [InlineData("unknown-lane")]
  [InlineData("internal-lane")]
  [InlineData("negative-position")]
  [InlineData("past-lane-end")]
  [InlineData("bad-role")]
  [InlineData("duplicate-id")]
  [InlineData("duplicate-lane-role")]
  [InlineData("missing-exit")]
  [InlineData("null-terminal")]
  public void InvalidTerminalDefinitionsFailValidation(string mutation)
  {
    using ProjectFixture source = new();
    ProjectTerminal[] terminals = ValidTerminals();
    switch (mutation)
    {
      case "unknown-lane":
        terminals[0] = terminals[0] with { LaneId = "missing" };
        break;
      case "internal-lane":
        terminals[0] = terminals[0] with { LaneId = ":center_0_0" };
        break;
      case "negative-position":
        terminals[0] = terminals[0] with { PositionM = -1 };
        break;
      case "past-lane-end":
        terminals[0] = terminals[0] with { PositionM = 193 };
        break;
      case "bad-role":
        terminals[0] = terminals[0] with { Role = "both" };
        break;
      case "duplicate-id":
        terminals[1] = terminals[1] with { Id = terminals[0].Id };
        break;
      case "duplicate-lane-role":
        terminals = [terminals[0], terminals[1], terminals[0] with { Id = "another-entry" }];
        break;
      case "missing-exit":
        terminals = [terminals[0]];
        break;
      case "null-terminal":
        terminals[0] = null!;
        break;
    }

    WriteTerminals(source, terminals);
    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(source.Path));
    Assert.Equal("invalid_project", error.Code);
    Assert.Contains("terminals.json", error.Message);
  }

  private static ProjectTerminal[] ValidTerminals()
  {
    return
    [
      new ProjectTerminal { Id = "entry-west", LaneId = "west_in_0", Role = "entry", PositionM = 0, Label = "West entrance" },
      new ProjectTerminal { Id = "exit-east", LaneId = "east_out_0", Role = "exit", PositionM = 192.8 }
    ];
  }

  private static void WriteTerminals(ProjectFixture source, ProjectTerminal[] terminals)
  {
    source.ChangeManifest(manifest => manifest["terminals_file"] = "terminals.json");
    string path = Path.Combine(source.Path, "terminals.json");
    File.WriteAllText(path, JsonSerializer.Serialize(terminals, JsonDefaults.Options));
  }
}

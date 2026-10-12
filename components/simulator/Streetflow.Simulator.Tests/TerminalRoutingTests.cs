using System.Net;
using System.Net.Http.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class TerminalRoutingTests
{
  private static string PoduRos => Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "podu-ros");

  [Theory]
  [InlineData(1)]
  [InlineData(42)]
  [InlineData(1337)]
  public void EveryApproachLaneHasTerminalCandidatesWithMatchingInsertionAndExitPositions(int seed)
  {
    CityProject project = CityProjectLoader.Load(PoduRos);
    CityDemandGenerator generator = new(project, seed, project.Manifest.Defaults.StartTimeSeconds);
    Dictionary<string, ProjectTerminal> terminals = new(StringComparer.Ordinal);
    HashSet<string> usedEntries = new(StringComparer.Ordinal);
    HashSet<string> usedExits = new(StringComparer.Ordinal);
    foreach (ProjectTerminal terminal in project.Terminals)
    {
      terminals.Add(terminal.Id, terminal);
    }

    Assert.Equal(49, terminals.Count);
    Assert.NotEmpty(generator.Candidates);
    foreach (RouteCandidate candidate in generator.Candidates)
    {
      Assert.NotNull(candidate.EntryTerminalId);
      Assert.NotNull(candidate.ExitTerminalId);
      ProjectTerminal entry = terminals[candidate.EntryTerminalId];
      ProjectTerminal exit = terminals[candidate.ExitTerminalId];
      Assert.Equal("entry", entry.Role);
      Assert.Equal("exit", exit.Role);
      Assert.Equal(entry.PositionM, candidate.DeparturePositionM);
      Assert.Equal(exit.PositionM, candidate.ArrivalPositionM);
      Assert.StartsWith(candidate.Edges[0] + "_", entry.LaneId);
      Assert.StartsWith(candidate.Edges[^1] + "_", exit.LaneId);
      Assert.True(candidate.DeparturePositionM >= 6);
      usedEntries.Add(entry.Id);
      usedExits.Add(exit.Id);
    }

    Assert.Equal(22, usedEntries.Count);
    Assert.Equal(27, usedExits.Count);
  }

  [Fact]
  public async Task IntersectionThroughTrafficRunsBeyondTheOriginalFailureAndStopsCleanly()
  {
    using ProjectFixture fixture = new();
    foreach (string file in Directory.GetFiles(PoduRos))
    {
      File.Copy(file, Path.Combine(fixture.Path, Path.GetFileName(file)), overwrite: true);
    }

    fixture.ChangeDemand(d => d["generation"]!["mean_departures_per_hour"] = 600);
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    HttpResponseMessage import = await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(fixture.Path), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Created, import.StatusCode);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/podu-ros/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.Equal("paused", status.State);
    for (int batch = 0; batch < 4; batch++)
    {
      response = await client.PostAsJsonAsync($"/api/v1/city-runs/{status.Id}/step", new CityRunCommand(1000), JsonDefaults.Options);
      status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
      Assert.True(status.State == "paused", status.Failure);
      Assert.InRange(status.PendingEntries, 0, 512);
    }

    Assert.Equal(2000, status.SimulationTime);
    CityProject inputs = CityProjectLoader.Load(fixture.Path);
    CityDemandGenerator replay = new(inputs, 42, inputs.Manifest.Defaults.StartTimeSeconds);
    int generatedCount = replay.GenerateUntil(status.SimulationTime, 10000).Length;
    Assert.True(status.ActiveVehicles + status.PendingEntries < generatedCount, "At least one generated car must clear its exit and leave the live network");
    CityRunMap map = (await client.GetFromJsonAsync<CityRunMap>($"/api/v1/city-runs/{status.Id}/map", JsonDefaults.Options))!;
    Assert.Equal(49, map.Terminals.Length);
    response = await client.PostAsJsonAsync($"/api/v1/city-runs/{status.Id}/stop", new CityRunCommand(), JsonDefaults.Options);
    status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.Equal("stopped", status.State);
  }

  [Fact]
  public void TerminalEndpointPolicyRequiresTerminalDefinitions()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d => d["generation"]!["endpoint_selection"] = "terminals");
    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(fixture.Path));
    Assert.Contains("requires nonempty terminals_file", error.Message);
  }
}

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
  [InlineData("podu-ros")]
  [InlineData("podu-ros-commute")]
  public void ThroughJourneysDoNotUseSideStreetTurnaroundShortcuts(string id)
  {
    string folder = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", id);
    CityProject project = CityProjectLoader.Load(folder);
    CityDemandGenerator generator = new(project, 42, project.Manifest.Defaults.StartTimeSeconds);
    Dictionary<string, HashSet<string>> turnarounds = new(StringComparer.Ordinal);
    foreach (ProjectConnection connection in project.Connections)
    {
      if (connection.Direction != "t")
      {
        continue;
      }

      if (!turnarounds.TryGetValue(connection.From, out HashSet<string>? destinations))
      {
        destinations = new(StringComparer.Ordinal);
        turnarounds.Add(connection.From, destinations);
      }

      destinations.Add(connection.To);
    }

    Assert.NotEmpty(turnarounds);
    foreach (RouteCandidate route in generator.Candidates)
    {
      for (int index = 1; index < route.Edges.Length; index++)
      {
        if (turnarounds.TryGetValue(route.Edges[index - 1], out HashSet<string>? destinations))
        {
          Assert.DoesNotContain(route.Edges[index], destinations);
        }
      }
    }
  }

  [Fact]
  public async Task MorningNicolinaTrafficDoesNotReproduceTheSideStreetGridlock()
  {
    string folder = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "podu-ros-commute");
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, folder);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/podu-ros-commute/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(status.State == "paused", status.Failure);
    for (int batch = 0; batch < 5; batch++)
    {
      response = await client.PostAsJsonAsync($"/api/v1/city-runs/{status.Id}/step", new CityRunCommand(1000), JsonDefaults.Options);
      status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
      Assert.True(status.State == "paused", status.Failure);
    }

    Assert.True(status.ArrivedVehicles > 1500, $"Only {status.ArrivedVehicles} vehicles cleared the map");
    Assert.True(status.PendingEntries < 100, $"{status.PendingEntries} entries are waiting");
    Assert.True(status.BacklogSeconds < status.Configuration.MaximumBacklogSeconds);
    Assert.True(status.GridlockRecoveries < 20);
    Snapshot snapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{status.Id}/snapshot", JsonDefaults.Options))!;
    int north = 0;
    int south = 0;
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      if (vehicle.EdgeId == "1134276130#0")
      {
        north++;
      }
      else if (vehicle.EdgeId == "305271347")
      {
        south++;
      }
    }

    Assert.True(north > south * 2, $"Nicolina north={north}, south={south}");
  }

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

    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/podu-ros/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.Equal("paused", status.State);
    for (int batch = 0; batch < 8; batch++)
    {
      response = await client.PostAsJsonAsync($"/api/v1/city-runs/{status.Id}/step", new CityRunCommand(1000), JsonDefaults.Options);
      status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
      Assert.True(status.State == "paused", status.Failure);
      Assert.InRange(status.PendingEntries, 0, 512);
    }

    Assert.Equal(4000, status.SimulationTime);
    Snapshot traffic = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{status.Id}/snapshot", JsonDefaults.Options))!;
    bool moving = false;
    foreach (VehicleState vehicle in traffic.Vehicles)
    {
      if (vehicle.SpeedMps > 0.1)
      {
        moving = true;
        break;
      }
    }

    Assert.True(moving, "Traffic must keep moving after the previous gridlock");
    CityProject inputs = CityProjectLoader.Load(fixture.Path);
    CityDemandGenerator replay = new(inputs, 42, inputs.Manifest.Defaults.StartTimeSeconds);
    int generatedCount = replay.GenerateUntil(status.SimulationTime, 10000).Length;
    Assert.True(status.ActiveVehicles + status.PendingEntries < generatedCount, "At least one generated car must clear its exit and leave the live network");
    CityRunMap map = (await client.GetFromJsonAsync<CityRunMap>($"/api/v1/city-runs/{status.Id}/map", JsonDefaults.Options))!;
    Assert.Equal(49, map.Terminals.Length);
    Assert.Equal(81, map.SignalPoints.Length);
    Assert.Equal(10, traffic.Signals.Length);
    Dictionary<string, string> states = new(StringComparer.Ordinal);
    foreach (SignalState signal in traffic.Signals)
    {
      states.Add(signal.Id, signal.State);
    }

    foreach (SignalMapPoint point in map.SignalPoints)
    {
      Assert.InRange(point.LinkIndex, 0, states[point.SignalId].Length - 1);
    }
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

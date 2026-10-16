using System.Net;
using System.Net.Http.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Microsoft.Extensions.DependencyInjection;

namespace Streetflow.Simulator.Tests;

public sealed class CityRunTests
{
  private static async Task Import(BackendFactory factory, HttpClient client, string id)
  {
    string path = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", id);
    await factory.AddProjectAsync(client, path);
  }

  private static async Task<CityRunStatus> Start(HttpClient client, string id, double? start = null)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/projects/{id}/runs", new CreateCityRunRequest(42, start), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.Equal("paused", status.State);
    return status;
  }

  private static async Task<CityRunStatus> Command(HttpClient client, string id, string command, int steps = 1, Pacing? pacing = null)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/city-runs/{id}/{command}", new CityRunCommand(steps, pacing), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
  }

  [Theory]
  [InlineData("tiny-graph")]
  [InlineData("iasi-central-day")]
  [InlineData("podu-ros")]
  public async Task EveryMapRunsWithMovingCarsAndReplays(string id)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await Import(factory, client, id);
    CityRunStatus run = await Start(client, id);
    CityRunStatus stepped = await Command(client, run.Id, "step", 200);
    Assert.Equal("paused", stepped.State);
    Assert.Equal(100, stepped.SimulationTime);
    Snapshot snapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    Assert.NotEmpty(snapshot.Vehicles);
    Assert.All(snapshot.Vehicles, vehicle => Assert.True(double.IsFinite(vehicle.X) && double.IsFinite(vehicle.Y)));
    bool moved = false;
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      moved = moved || vehicle.SpeedMps > 0;
    }

    Assert.True(moved);
    CityRunStatus replay = await Command(client, run.Id, "restart");
    Assert.NotEqual(run.Id, replay.Id);
    await Command(client, replay.Id, "step", 200);
    Snapshot replaySnapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{replay.Id}/snapshot", JsonDefaults.Options))!;
    Assert.Equal(snapshot.Vehicles, replaySnapshot.Vehicles);
    await Command(client, replay.Id, "stop");
    Assert.Null(factory.Services.GetRequiredService<CityRunCoordinator>().EngineProcessId);
    string directory = Path.Combine(factory.DataRoot, "city-runs", run.Id);
    foreach (string file in new[] { "observations.jsonl", "passages.json", "tripinfo.xml", "detectors.add.xml", "trips.json" })
    {
      Assert.False(File.Exists(Path.Combine(directory, file)), file);
    }

    Assert.Empty((await client.GetFromJsonAsync<RunStatus[]>("/api/v1/runs", JsonDefaults.Options))!);
  }

  [Fact]
  public async Task ControlsClockAndEngineOwnershipWork()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await Import(factory, client, "tiny-graph");
    CityRunStatus run = await Start(client, "tiny-graph", 86398);
    run = await Command(client, run.Id, "step", 8);
    Assert.Equal(1, run.Day);
    Assert.Equal(2, run.CityTimeSeconds);
    Assert.Equal("paused", run.State);
    HttpResponseMessage conflict = await client.PostAsJsonAsync("/api/v1/runs", new CreateRunRequest("tiny-intersection", "ownership-check", Seed: 42), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    await Command(client, run.Id, "pacing", pacing: new Pacing("unpaced"));
    await Command(client, run.Id, "resume");
    await Task.Delay(100);
    run = await Command(client, run.Id, "pause");
    Assert.True(run.SimulationTime > 4);
    CityRunStatus unchanged = (await client.GetFromJsonAsync<CityRunStatus>($"/api/v1/city-runs/{run.Id}", JsonDefaults.Options))!;
    Assert.Equal(run.SimulationTime, unchanged.SimulationTime);
    await Command(client, run.Id, "stop");
    Assert.Equal("stopped", (await Command(client, run.Id, "stop")).State);
  }

  [Fact]
  public async Task ZeroTrafficDoesNotEndContinuousRun()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d => d["generation"]!["mean_departures_per_hour"] = 0);
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    CityRunStatus run = await Start(client, "tiny-graph");
    run = await Command(client, run.Id, "step", 100);
    Assert.Equal("paused", run.State);
    Assert.Equal(50, run.SimulationTime);
    Assert.Equal(0, run.ActiveVehicles);
    Assert.True(run.GenerationEnded);
  }

  [Fact]
  public async Task PersistentOverloadKeepsAdvancingWithBoundedState()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d => d["generation"]!["mean_departures_per_hour"] = 360000);
    await using BackendFactory factory = new() { MaximumLiveVehicles = 2, MaximumPendingEntries = 1, MaximumBacklogSeconds = 2 };
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    CityRunStatus run = await Start(client, "tiny-graph");
    run = await Command(client, run.Id, "step", 100);
    Assert.Equal("paused", run.State);
    Assert.Null(run.Failure);
    Assert.Contains("Traffic congestion", run.Warning);
    Assert.Equal(50, run.SimulationTime);
    run = await Command(client, run.Id, "step", 100);
    Assert.Equal("paused", run.State);
    Assert.Equal(100, run.SimulationTime);
    Assert.InRange(run.ActiveVehicles + run.PendingEntries, 0, 2);
    Assert.InRange(run.PendingEntries, 0, 1);
  }

  [Fact]
  public async Task RunMetadataAndReplaySurviveServiceRestart()
  {
    string root;
    string id;
    await using (BackendFactory factory = new())
    {
      root = factory.DataRoot;
      using HttpClient client = factory.CreateClient();
      await Import(factory, client, "tiny-graph");
      CityRunStatus run = await Start(client, "tiny-graph");
      id = run.Id;
      await Command(client, id, "step", 20);
      await Command(client, id, "stop");
    }

    await using BackendFactory restarted = new(root);
    using HttpClient reopened = restarted.CreateClient();
    CityRunStatus stored = (await reopened.GetFromJsonAsync<CityRunStatus>($"/api/v1/city-runs/{id}", JsonDefaults.Options))!;
    Assert.Equal("stopped", stored.State);
    CityRunStatus replay = await Command(reopened, id, "restart");
    Assert.Equal(stored.Configuration, replay.Configuration);
    Assert.Equal(0, replay.SimulationTime);
    await Command(reopened, replay.Id, "stop");
  }

  [Fact]
  public async Task NonrepeatingTrafficDrainsAfterMidnightWithoutEndingTheRun()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d =>
    {
      d["repeat_daily"] = false;
      d["generation"]!["mean_departures_per_hour"] = 3600;
    });
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    CityRunStatus run = await Start(client, "tiny-graph", 86390);
    run = await Command(client, run.Id, "step", 1000);
    Assert.True(run.GenerationEnded);
    Assert.Equal("paused", run.State);
    Assert.Equal(0, run.ActiveVehicles);
    Assert.Equal(0, run.PendingEntries);
    await Command(client, run.Id, "stop");
  }
}

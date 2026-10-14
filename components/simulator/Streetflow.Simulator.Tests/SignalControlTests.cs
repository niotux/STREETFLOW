using System.Net;
using System.Net.Http.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class SignalControlTests
{
  private static async Task<CityRunStatus> StartAsync(HttpClient client)
  {
    string folder = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "tiny-graph");
    HttpResponseMessage imported = await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(folder), JsonDefaults.Options);
    imported.EnsureSuccessStatusCode();
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/tiny-graph/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(status.State == "paused", status.Failure);
    return status;
  }

  private static async Task<SignalState> SetAsync(HttpClient client, string id, string action, int? linkIndex = null)
  {
    HttpResponseMessage response = await client.PutAsJsonAsync($"/api/v1/city-runs/{id}/signals/center", new SignalControlRequest(action, linkIndex), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<SignalState>(JsonDefaults.Options))!;
  }

  private static async Task<Snapshot> StepAsync(HttpClient client, string id, int steps)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/city-runs/{id}/step", new CityRunCommand(steps), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(status.State == "paused", status.Failure);
    return (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{id}/snapshot", JsonDefaults.Options))!;
  }

  [Fact]
  public async Task SignalsStartOffAndRemainOffAcrossGeneratedCycles()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await StartAsync(client);
    Assert.Equal("external-v1", run.Configuration.SignalControlMode);
    SignalState[] initial = (await client.GetFromJsonAsync<SignalState[]>($"/api/v1/city-runs/{run.Id}/signals", JsonDefaults.Options))!;
    SignalState signal = Assert.Single(initial);
    Assert.All(signal.State, code => Assert.True(code == 'o' || code == 'O'));
    Snapshot snapshot = await StepAsync(client, run.Id, 400);
    Assert.Equal(initial, snapshot.Signals);
    Assert.NotEmpty(snapshot.Vehicles);
  }

  [Fact]
  public async Task CommandsUpdatePausedSnapshotImmediatelyAndHoldUntilChanged()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await StartAsync(client);
    Snapshot initial = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    SignalState off = Assert.Single(initial.Signals);
    SignalState green = await SetAsync(client, run.Id, "green", 0);
    Assert.Equal('G', green.State[0]);
    Assert.Equal(off.State[1..], green.State[1..]);
    Snapshot immediate = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    Assert.Equal(0, immediate.SimulationTime);
    Assert.True(immediate.Sequence > initial.Sequence);
    Assert.Equal(green, Assert.Single(immediate.Signals));
    Snapshot held = await StepAsync(client, run.Id, 400);
    Assert.Equal(green, Assert.Single(held.Signals));
    Assert.Equal('r', (await SetAsync(client, run.Id, "on", 0)).State[0]);
    Assert.Equal('u', (await SetAsync(client, run.Id, "red-yellow", 0)).State[0]);
    Assert.Equal('y', (await SetAsync(client, run.Id, "yellow", 0)).State[0]);
    Assert.Equal('r', (await SetAsync(client, run.Id, "red", 0)).State[0]);
    Assert.Equal(off, await SetAsync(client, run.Id, "off", 0));
    Assert.Equal(new string('r', off.State.Length), (await SetAsync(client, run.Id, "on")).State);
    Assert.Equal(off, await SetAsync(client, run.Id, "off"));
  }

  [Fact]
  public async Task RedStopsVehiclesAndGreenReleasesTheQueue()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await StartAsync(client);
    await SetAsync(client, run.Id, "red");
    Snapshot red = await StepAsync(client, run.Id, 200);
    Dictionary<string, VehicleState> queued = new(StringComparer.Ordinal);
    foreach (VehicleState vehicle in red.Vehicles)
    {
      Assert.False(vehicle.EdgeId.StartsWith(':'));
      if (vehicle.SpeedMps < 0.1 && vehicle.LanePosition > 180)
      {
        queued.Add(vehicle.Id, vehicle);
      }
    }

    Assert.NotEmpty(queued);
    await SetAsync(client, run.Id, "green");
    Snapshot green = await StepAsync(client, run.Id, 40);
    foreach (VehicleState vehicle in green.Vehicles)
    {
      if (queued.TryGetValue(vehicle.Id, out VehicleState? stopped) && stopped.LaneId == vehicle.LaneId)
      {
        queued.Remove(vehicle.Id);
      }
    }

    Assert.NotEmpty(queued);
  }

  [Theory]
  [InlineData("unknown", null)]
  [InlineData(null, null)]
  [InlineData("green", -1)]
  [InlineData("red", 12)]
  public async Task InvalidCommandsLeaveSignalsUnchanged(string? action, int? linkIndex)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await StartAsync(client);
    Snapshot before = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    HttpResponseMessage response = await client.PutAsJsonAsync($"/api/v1/city-runs/{run.Id}/signals/center", new SignalControlRequest(action!, linkIndex), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    response = await client.PutAsJsonAsync($"/api/v1/city-runs/{run.Id}/signals/missing", new SignalControlRequest("green"), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Snapshot after = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    Assert.Equal(before.Sequence, after.Sequence);
    Assert.Equal(before.Signals, after.Signals);
  }

  [Fact]
  public async Task RestartResetsSignalsAndStoppedRunsRejectControl()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await StartAsync(client);
    await SetAsync(client, run.Id, "green");
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/city-runs/{run.Id}/restart", new CityRunCommand(), JsonDefaults.Options);
    CityRunStatus restarted = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(restarted.State == "paused", restarted.Failure);
    SignalState[] signals = (await client.GetFromJsonAsync<SignalState[]>($"/api/v1/city-runs/{restarted.Id}/signals", JsonDefaults.Options))!;
    Assert.All(Assert.Single(signals).State, code => Assert.True(code == 'o' || code == 'O'));
    response = await client.PutAsJsonAsync($"/api/v1/city-runs/{run.Id}/signals/center", new SignalControlRequest("red"), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }
}

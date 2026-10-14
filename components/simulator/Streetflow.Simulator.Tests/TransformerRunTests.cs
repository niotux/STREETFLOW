using System.Net;
using System.Net.Http.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class TransformerRunTests
{
  [Theory]
  [InlineData("tiny-graph")]
  [InlineData(TransformerProjectFixture.ProjectId)]
  public async Task BothProjectFormatsRunReplayAndRemoveVehiclesAtExits(string projectId)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await Import(client, projectId);
    HttpResponseMessage created = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/runs", new CreateCityRunRequest(42, 8 * 3600), JsonDefaults.Options);
    CityRunStatus run = await Status(created);
    Assert.Equal("paused", run.State);
    run = await Command(client, run.Id, "step", 200);
    Assert.Equal(100, run.SimulationTime);
    Assert.Null(run.Failure);
    Snapshot snapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    Assert.NotEmpty(snapshot.Vehicles);
    bool moved = false;
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      moved = moved || vehicle.SpeedMps > 0;
    }

    Assert.True(moved);
    CityRunStatus replay = await Command(client, run.Id, "restart");
    Assert.Equal(run.Configuration, replay.Configuration);
    await Command(client, replay.Id, "step", 200);
    Snapshot replaySnapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{replay.Id}/snapshot", JsonDefaults.Options))!;
    Assert.Equal(snapshot.Vehicles, replaySnapshot.Vehicles);
    replay = await Command(client, replay.Id, "step", 600);
    Assert.Null(replay.Failure);
    Assert.True(replay.ArrivedVehicles > 20);
    Assert.Equal(0, replay.GridlockRecoveries);
    await Command(client, replay.Id, "stop");
  }

  [Fact]
  public async Task ImportedTransformerWeightsAndRunConfigurationSurviveServiceRestart()
  {
    string dataRoot;
    string runId;
    CityRunConfiguration configuration;
    await using (BackendFactory factory = new())
    {
      dataRoot = factory.DataRoot;
      using HttpClient client = factory.CreateClient();
      await Import(client, TransformerProjectFixture.ProjectId);
      HttpResponseMessage created = await client.PostAsJsonAsync($"/api/v1/projects/{TransformerProjectFixture.ProjectId}/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
      CityRunStatus run = await Status(created);
      runId = run.Id;
      configuration = run.Configuration;
      await Command(client, runId, "step", 20);
      await Command(client, runId, "stop");
    }

    await using BackendFactory restarted = new(dataRoot);
    using HttpClient reopened = restarted.CreateClient();
    HttpResponseMessage model = await reopened.GetAsync($"/api/v1/projects/{TransformerProjectFixture.ProjectId}/model");
    HttpResponseMessage weights = await reopened.GetAsync($"/api/v1/projects/{TransformerProjectFixture.ProjectId}/weights");
    Assert.Equal(HttpStatusCode.OK, model.StatusCode);
    Assert.Equal(HttpStatusCode.OK, weights.StatusCode);
    CityRunStatus stored = (await reopened.GetFromJsonAsync<CityRunStatus>($"/api/v1/city-runs/{runId}", JsonDefaults.Options))!;
    Assert.Equal(configuration, stored.Configuration);
    CityRunStatus replay = await Command(reopened, runId, "restart");
    Assert.Equal(configuration, replay.Configuration);
    replay = await Command(reopened, replay.Id, "step", 20);
    Assert.Null(replay.Failure);
    Assert.Equal(10, replay.SimulationTime);
    await Command(reopened, replay.Id, "stop");
  }

  private static async Task Import(HttpClient client, string projectId)
  {
    string source = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", projectId);
    HttpResponseMessage imported = await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(source), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
  }

  private static async Task<CityRunStatus> Command(HttpClient client, string runId, string command, int steps = 1)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/city-runs/{runId}/{command}", new CityRunCommand(steps), JsonDefaults.Options);
    return await Status(response);
  }

  private static async Task<CityRunStatus> Status(HttpResponseMessage response)
  {
    string body = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, body);
    return System.Text.Json.JsonSerializer.Deserialize<CityRunStatus>(body, JsonDefaults.Options)!;
  }
}

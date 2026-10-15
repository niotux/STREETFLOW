using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class NeuralRoutesTests
{
  private static readonly NeuralGenerationLimits Limits = new(10000, 4096);

  [Fact]
  public void NeuralGenerationUsesTimeAndRatesAndIsIndependentOfBatching()
  {
    CityProject project = CityProjectLoader.Load(NeuralProjectFixture.Source);
    Assert.Equal("Neural Routes", project.ModelKind);
    Assert.Null(project.Demand);
    Assert.Null(project.TransformerWeights);
    using NeuralDemandGenerator whole = new(project, 42, 25200, Limits);
    using NeuralDemandGenerator batches = new(project, 42, 25200, Limits);
    Assert.Equal(440.0 / 3600, whole.RatePerSecond(0), 10);
    Assert.Equal(0, whole.RatePerSecond(7200));
    Assert.Equal(440.0 / 3600, whole.RatePerSecond(9 * 3600), 10);
    GeneratedArrival[] expected = whole.GenerateUntil(3600, 10000);
    List<GeneratedArrival> actual = [];
    while (batches.NextDepartureSeconds <= 3600)
    {
      actual.AddRange(batches.GenerateUntil(3600, 7));
    }

    Assert.Equal(expected.Length, actual.Count);
    int southCount = 0;
    int northbound = 0;
    for (int index = 0; index < expected.Length; index++)
    {
      Assert.Equal(expected[index].Id, actual[index].Id);
      Assert.Equal(expected[index].DepartureSeconds, actual[index].DepartureSeconds);
      Assert.Equal(expected[index].Route.Edges, actual[index].Route.Edges);
      if (expected[index].Route.Edges[0] == "south_in")
      {
        southCount++;
        if (expected[index].Route.Edges[^1] == "north_out")
        {
          northbound++;
        }
      }
    }

    Assert.InRange(expected.Length, 350, 550);
    Assert.InRange((double)northbound / southCount, 0.65, 0.85);
    using NeuralDemandGenerator evening = new(project, 42, 57600, Limits);
    GeneratedArrival[] eveningCars = evening.GenerateUntil(3600, 10000);
    int eveningNorth = 0;
    foreach (GeneratedArrival arrival in eveningCars)
    {
      if (arrival.Route.Edges[0] == "north_in")
      {
        eveningNorth++;
      }
    }

    Assert.InRange((double)eveningNorth / eveningCars.Length, 0.85, 0.97);
  }

  [Theory]
  [InlineData("network")]
  [InlineData("turn-order")]
  [InlineData("period-gap")]
  [InlineData("negative-rate")]
  [InlineData("model-path")]
  [InlineData("onnx")]
  public void InvalidNeuralProjectsAreRejected(string mutation)
  {
    using NeuralProjectFixture fixture = new();
    if (mutation == "onnx")
    {
      File.WriteAllText(Path.Combine(fixture.Path, "route-model.onnx"), "invalid model");
    }
    else
    {
      fixture.Change("neural-model.json", node =>
      {
        switch (mutation)
        {
          case "network": node["network_sha256"] = new string('0', 64); break;
          case "turn-order": node["roads"]![0]!["next_edge_ids"]![0] = "wrong-road"; break;
          case "period-gap": node["periods"]![0]!["end_seconds"] = 1; break;
          case "negative-rate": node["periods"]![1]!["arrivals"]![0]!["vehicles_per_hour"] = -1; break;
          case "model-path": node["model_file"] = "../route-model.onnx"; break;
        }
      });
    }

    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(fixture.Path));
    Assert.Equal("invalid_project", error.Code);
  }

  [Fact]
  public void UncoveredHoursAndNonRepeatingMidnightHaveNoNewDemand()
  {
    using NeuralProjectFixture fixture = new();
    fixture.Change("neural-model.json", node => node["repeat_daily"] = false);
    CityProject project = CityProjectLoader.Load(fixture.Path);
    using NeuralDemandGenerator midnight = new(project, 42, 86399, Limits);
    Assert.True(midnight.Ended);
    using NeuralDemandGenerator morning = new(project, 42, 25200, Limits);
    _ = morning.GenerateUntil(86400, 10000);
    Assert.True(morning.Ended);
    Assert.Equal(0, morning.RatePerSecond(86400));
  }

  [Fact]
  public async Task AllThreeFormatsCanBeImportedAndNeuralRunsSurviveRestart()
  {
    string dataRoot;
    string runId;
    CityRunConfiguration configuration;
    await using (BackendFactory factory = new())
    {
      dataRoot = factory.DataRoot;
      using HttpClient client = factory.CreateClient();
      foreach (string projectId in new string[] { "tiny-graph", TransformerProjectFixture.ProjectId, NeuralProjectFixture.ProjectId })
      {
        string source = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", projectId);
        HttpResponseMessage imported = await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(source), JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
      }

      Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/projects/{NeuralProjectFixture.ProjectId}/model")).StatusCode);
      Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/projects/{NeuralProjectFixture.ProjectId}/weights")).StatusCode);
      CityRunStatus run = await Create(client);
      Assert.Equal("paused", run.State);
      runId = run.Id;
      configuration = run.Configuration;
      run = await Command(client, runId, "step", 200);
      Assert.Null(run.Failure);
      Snapshot snapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
      Assert.NotEmpty(snapshot.Vehicles);
      CityRunStatus replay = await Command(client, runId, "restart");
      Assert.Equal(configuration, replay.Configuration);
      await Command(client, replay.Id, "step", 200);
      Snapshot repeated = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{replay.Id}/snapshot", JsonDefaults.Options))!;
      Assert.Equal(snapshot.Vehicles, repeated.Vehicles);
      replay = await Command(client, replay.Id, "step", 600);
      Assert.Null(replay.Failure);
      Assert.True(replay.ArrivedVehicles > 10);
      await Command(client, replay.Id, "stop");
    }

    await using BackendFactory restarted = new(dataRoot);
    using HttpClient reopened = restarted.CreateClient();
    CityRunStatus saved = await Command(reopened, runId, "restart");
    Assert.Equal(configuration, saved.Configuration);
    Assert.Equal("paused", saved.State);
    await Command(reopened, saved.Id, "stop");
  }

  private static async Task<CityRunStatus> Create(HttpClient client)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/projects/{NeuralProjectFixture.ProjectId}/runs", new CreateCityRunRequest(42, 25200), JsonDefaults.Options);
    return await Status(response);
  }

  private static async Task<CityRunStatus> Command(HttpClient client, string id, string command, int steps = 1)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/city-runs/{id}/{command}", new CityRunCommand(steps), JsonDefaults.Options);
    return await Status(response);
  }

  private static async Task<CityRunStatus> Status(HttpResponseMessage response)
  {
    string body = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, body);
    return JsonSerializer.Deserialize<CityRunStatus>(body, JsonDefaults.Options)!;
  }
}

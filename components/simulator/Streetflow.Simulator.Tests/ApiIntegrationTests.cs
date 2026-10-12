using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Tests;

public sealed class ApiIntegrationTests
{
  [Fact]
  public async Task BrowserInspectorAndApiAreServedTogether()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    HttpResponseMessage page = await client.GetAsync("/");
    Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
    Assert.Contains("Traffic playground", await page.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/styles.css")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/scenarios")).StatusCode);
  }

  private static async Task<T> Body<T>(HttpResponseMessage response)
  {
    var body = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
    return JsonSerializer.Deserialize<T>(body, JsonDefaults.Options)!;
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
  {
    return client.PostAsJsonAsync(path, body, JsonDefaults.Options);
  }

  private static async Task<RunStatus> Create(HttpClient client, string scenario = "tiny-intersection", string request = "create", double? duration = null)
  {
    RunOverrides overrides = new(DurationSeconds: duration);
    CreateRunRequest createRequest = new(scenario, request, Overrides: overrides);
    HttpResponseMessage response = await Post(client, "/api/v1/runs", createRequest);
    return await Body<RunStatus>(response);
  }

  private static async Task<RunStatus> Step(HttpClient client, string id, int steps, string request = "step")
  {
    return await Body<RunStatus>(await Post(client, $"/api/v1/runs/{id}/step", new StepRequest(request, steps)));
  }

  private static async Task<JsonElement> Get(HttpClient client, string path)
  {
    return await Body<JsonElement>(await client.GetAsync(path));
  }

  [Fact]
  public async Task RealRunCompletesAndPreservesTripsObservationsAndInputs()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    Assert.Equal("paused", run.State);
    Assert.Contains("SUMO", run.EngineVersion);
    RunStatus complete = await Step(client, run.Id, 360);
    Assert.Equal("completed", complete.State);
    Assert.Equal(32, complete.InsertedVehicles);
    Assert.Equal(32, complete.ArrivedVehicles);
    Assert.Equal(0, complete.TeleportEvents);
    Assert.Null(complete.Failure);
    JsonElement results = await Get(client, $"/api/v1/runs/{run.Id}/results");
    Assert.Equal(32, results.GetProperty("completed").GetInt32());
    Assert.True(results.GetProperty("results_final").GetBoolean());
    JsonElement trips = (await Get(client, $"/api/v1/runs/{run.Id}/trips")).GetProperty("trips");
    foreach (JsonElement trip in trips.EnumerateArray())
    {
      Assert.Equal("completed", trip.GetProperty("status").GetString());
      Assert.True(trip.GetProperty("duration").GetDouble() > 0);
    }

    JsonElement observations = await Get(client, "/api/v1/traffic/observations?after=0");
    JsonElement frames = observations.GetProperty("frames");
    Assert.Equal(36, frames.GetArrayLength());
    int crossings = 0;
    foreach (JsonElement frame in frames.EnumerateArray())
    {
      foreach (JsonElement lane in frame.GetProperty("lanes").EnumerateArray())
      {
        crossings += lane.GetProperty("crossed_vehicle_count").GetInt32();
      }
    }

    Assert.Equal(32, crossings);
    Assert.DoesNotContain("route", observations.GetRawText());
    Assert.DoesNotContain("vehicle_id", observations.GetRawText());
    Assert.True(File.Exists(Path.Combine(factory.DataRoot, run.Id, "tripinfo.xml")));
    Assert.True(File.Exists(Path.Combine(factory.DataRoot, run.Id, "network.net.xml")));
    Assert.Null(factory.Services.GetRequiredService<RunCoordinator>().EngineProcessId);
    JsonElement passages = (await Get(client, $"/api/v1/runs/{run.Id}/passages")).GetProperty("passages");
    Assert.Equal(8, passages.GetArrayLength());
    foreach (JsonElement passage in passages.EnumerateArray())
    {
      Assert.Equal("completed", passage.GetProperty("status").GetString());
      Assert.True(passage.GetProperty("duration_seconds").GetDouble() > 0);
    }
  }

  [Fact]
  public async Task ResetReplaysSameDemandWithNewSessionAndSameVehiclePositions()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    await Step(client, run.Id, 20);
    var before = (await Get(client, $"/api/v1/runs/{run.Id}/snapshot")).GetProperty("vehicles").GetRawText();
    RunStatus reset = await Body<RunStatus>(await Post(client, $"/api/v1/runs/{run.Id}/reset", new CommandRequest("reset")));
    Assert.NotEqual(run.Id, reset.Id);
    Assert.Equal(run.Configuration, reset.Configuration);
    await Step(client, reset.Id, 20);
    var after = (await Get(client, $"/api/v1/runs/{reset.Id}/snapshot")).GetProperty("vehicles").GetRawText();
    Assert.Equal(before, after);
    Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/traffic/observations?session_id={run.Id}")).StatusCode);
  }

  [Fact]
  public async Task DuplicateCreateAndStepDoNotStartOrAdvanceTwice()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus first = await Create(client);
    RunStatus repeated = await Create(client);
    Assert.Equal(first.Id, repeated.Id);
    RunStatus stepped = await Step(client, first.Id, 10);
    RunStatus replay = await Step(client, first.Id, 10);
    Assert.Equal(stepped.SimulationTime, replay.SimulationTime);
    HttpResponseMessage conflict = await Post(client, $"/api/v1/runs/{first.Id}/step", new StepRequest("step", 11));
    Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    Assert.Equal(5, (await Get(client, $"/api/v1/runs/{first.Id}")).GetProperty("simulation_time").GetDouble());
  }

  [Fact]
  public async Task CompetingCreatesAdmitExactlyOneRun()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    const int concurrentCreates = 4;
    Task<HttpResponseMessage>[] requests = new Task<HttpResponseMessage>[concurrentCreates];
    for (int index = 0; index < requests.Length; index++)
    {
      requests[index] = Post(client, "/api/v1/runs", new CreateRunRequest("tiny-intersection", "create-" + index));
    }

    HttpResponseMessage[] responses = await Task.WhenAll(requests);
    int created = 0;
    int conflicts = 0;
    foreach (HttpResponseMessage response in responses)
    {
      if (response.StatusCode == HttpStatusCode.Created)
      {
        created++;
      }

      if (response.StatusCode == HttpStatusCode.Conflict)
      {
        conflicts++;
      }
    }

    Assert.Equal(1, created);
    Assert.Equal(concurrentCreates - 1, conflicts);
  }

  [Fact]
  public async Task InvalidInputsAreRejectedAndPausedPollingDoesNotAdvanceTime()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/api/v1/runs", new CreateRunRequest("missing", "missing"))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/v1/runs", new CreateRunRequest("tiny-intersection", "bad", Overrides: new(StepSeconds: 0)))).StatusCode);
    RunStatus run = await Create(client);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, $"/api/v1/runs/{run.Id}/step", new StepRequest("bad", 0))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/runs/{run.Id}/snapshot?limit=10001")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/runs/{run.Id}/snapshot?min_x=0")).StatusCode);
    JsonElement frames = (await Get(client, "/api/v1/traffic/observations")).GetProperty("frames");
    Assert.Equal(0, frames.GetArrayLength());
    Assert.Equal(0, (await Get(client, $"/api/v1/runs/{run.Id}")).GetProperty("simulation_time").GetDouble());
  }

  [Fact]
  public async Task ResumePauseStopAndHostShutdownCleanUpEngine()
  {
    BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    var pid = factory.Services.GetRequiredService<RunCoordinator>().EngineProcessId!.Value;
    await Body<RunStatus>(await Post(client, $"/api/v1/runs/{run.Id}/resume", new CommandRequest("resume")));
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, $"/api/v1/runs/{run.Id}/step", new StepRequest("bad-step"))).StatusCode);
    await Body<RunStatus>(await Post(client, $"/api/v1/runs/{run.Id}/pause", new CommandRequest("pause")));
    await factory.DisposeAsync();
    Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    await using BackendFactory restarted = new BackendFactory(factory.DataRoot);
    using HttpClient restartedClient = restarted.CreateClient();
    JsonElement persisted = await Get(restartedClient, $"/api/v1/runs/{run.Id}");
    Assert.Equal("stopped", persisted.GetProperty("state").GetString());
    Assert.True((await Get(restartedClient, $"/api/v1/runs/{run.Id}/trips")).GetProperty("total").GetInt32() > 0);
  }

  [Fact]
  public async Task EngineStartupFailureBecomesInspectableFailedRun()
  {
    await using BackendFactory factory = new BackendFactory
    {
      SumoBinary = "streetflow-nonexistent-sumo"
    };
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    Assert.Equal("failed", run.State);
    Assert.NotNull(run.Failure);
    Assert.Null(factory.Services.GetRequiredService<RunCoordinator>().EngineProcessId);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, $"/api/v1/runs/{run.Id}/resume", new CommandRequest("resume"))).StatusCode);
  }

  [Fact]
  public async Task EngineCrashFailsTheRunAndAllowsAnotherRun()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    using (Process process = Process.GetProcessById(factory.Services.GetRequiredService<RunCoordinator>().EngineProcessId!.Value))
    {
      process.Kill();
      await process.WaitForExitAsync();
    }

    RunStatus failed = await Step(client, run.Id, 1);
    Assert.Equal("failed", failed.State);
    Assert.NotNull(failed.Failure);
    Assert.Null(factory.Services.GetRequiredService<RunCoordinator>().EngineProcessId);
    Assert.Equal("paused", (await Create(client, request: "after-crash")).State);
  }

  [Fact]
  public async Task DemandOverrideAndResetPreserveResolvedTripCount()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    CreateRunRequest request = new CreateRunRequest("tiny-intersection", "volume", Overrides: new(DemandMultiplier: 2));
    RunStatus run = await Body<RunStatus>(await Post(client, "/api/v1/runs", request));
    Assert.Equal(64, (await Get(client, $"/api/v1/runs/{run.Id}/trips")).GetProperty("total").GetInt32());
    RunStatus reset = await Body<RunStatus>(await Post(client, $"/api/v1/runs/{run.Id}/reset", new CommandRequest("reset")));
    Assert.Equal(64, (await Get(client, $"/api/v1/runs/{reset.Id}/trips")).GetProperty("total").GetInt32());
    Assert.Equal(2, reset.Configuration.DemandMultiplier);
  }

  [Fact]
  public async Task RequestReplaySurvivesAServiceRestart()
  {
    BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    await Step(client, run.Id, 10);
    await factory.DisposeAsync();
    await using BackendFactory restarted = new BackendFactory(factory.DataRoot);
    using HttpClient restartedClient = restarted.CreateClient();
    Assert.Equal(run.Id, (await Create(restartedClient)).Id);
    Assert.Equal(5, (await Step(restartedClient, run.Id, 10)).SimulationTime);
    Assert.Equal(5, (await Get(restartedClient, $"/api/v1/runs/{run.Id}")).GetProperty("simulation_time").GetDouble());
    Assert.Equal(1, (await Get(restartedClient, $"/api/v1/runs/{run.Id}/observations")).GetProperty("frames").GetArrayLength());
    Assert.Null(restarted.Services.GetRequiredService<RunCoordinator>().EngineProcessId);
    Assert.False((await Get(restartedClient, $"/api/v1/runs/{run.Id}/snapshot")).GetProperty("available").GetBoolean());
  }

  [Fact]
  public async Task RetentionReportsGapAndInspectionLimitsDoNotRemoveSimulatedCars()
  {
    await using BackendFactory factory = new BackendFactory
    {
      Retention = 2
    };
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client);
    await Step(client, run.Id, 40);
    JsonElement frames = await Get(client, "/api/v1/traffic/observations?after=0");
    Assert.True(frames.GetProperty("gap").GetBoolean());
    Assert.Equal(2, frames.GetProperty("frames").GetArrayLength());
    JsonElement snapshot = await Get(client, $"/api/v1/runs/{run.Id}/snapshot?limit=1");
    Assert.Equal(1, snapshot.GetProperty("returned_count").GetInt32());
    Assert.True(snapshot.GetProperty("sampled").GetBoolean());
    Assert.True(snapshot.GetProperty("total_active_count").GetInt32() > 1);
  }

  [Fact]
  public async Task IasiImportedNetworkRunsRealVehiclesAndExposesLaneGeometry()
  {
    await using BackendFactory factory = new BackendFactory();
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client, "iasi-central");
    Assert.Equal("paused", run.State);
    RunStatus status = await Step(client, run.Id, 100);
    Assert.Equal("paused", status.State);
    Assert.True(status.InsertedVehicles > 20);
    Assert.Null(status.Failure);
    JsonElement map = await Get(client, $"/api/v1/runs/{run.Id}/map?limit=10");
    Assert.True(map.GetProperty("total_lanes").GetInt32() > 100);
    Assert.Equal(10, map.GetProperty("lanes").GetArrayLength());
    Assert.Contains("OpenStreetMap", map.GetProperty("attribution").GetString());
    Assert.True((await Get(client, $"/api/v1/runs/{run.Id}/snapshot")).GetProperty("total_active_count").GetInt32() > 0);
    RunStatus stopped = await Body<RunStatus>(await Post(client, $"/api/v1/runs/{run.Id}/stop", new CommandRequest("stop")));
    Assert.Equal("stopped", stopped.State);
    Assert.Null(stopped.Failure);
  }

  [PostgresFact]
  public async Task PostgreSqlStoresRunFramesAndTripResults()
  {
    var connectionString = Environment.GetEnvironmentVariable("STREETFLOW_TEST_POSTGRES")!;
    await using BackendFactory factory = new BackendFactory
    {
      ConnectionString = connectionString
    };
    using HttpClient client = factory.CreateClient();
    RunStatus run = await Create(client, duration: 10);
    RunStatus result = await Step(client, run.Id, 20);
    Assert.Equal("completed", result.State);
    await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    foreach (var table in new[]
    {
      "simulator_runs",
      "simulator_observations",
      "simulator_trips"
    }

    )
    {
      await using NpgsqlCommand command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {(table == "simulator_runs" ? "id" : "run_id")}=$1", connection);
      command.Parameters.AddWithValue(run.Id);
      Assert.True((long)(await command.ExecuteScalarAsync())! > 0);
    }
  }
}

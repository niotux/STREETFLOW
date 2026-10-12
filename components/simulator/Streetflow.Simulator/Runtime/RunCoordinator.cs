using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Streetflow.Simulator.Persistence;
using Streetflow.Simulator.Scenarios;
using Streetflow.Simulator.Sumo;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Runtime;

public sealed partial class RunCoordinator(ScenarioCatalog catalog, SimulatorOptions options, RunStore runStore, ILogger<RunCoordinator> logger, EngineOwnership ownership) : BackgroundService
{
  private const int MaximumRequestIdLength = 128;
  private const int MaximumStepCount = 1000;
  private const double TimeComparisonTolerance = 1e-8;
  private const double IdleDelayMilliseconds = 20;
  private const double MinimumDelayMilliseconds = 1;
  private const int MillisecondsPerSecond = 1000;
  private readonly SemaphoreSlim gate = new(1, 1);
  private readonly RunStore storage = runStore;
  private readonly Dictionary<string, RunStatus> archive = new(StringComparer.Ordinal);
  private readonly Dictionary<string, SavedRequest> requests = new(StringComparer.Ordinal);
  private readonly Queue<TrafficObservation> observations = new();
  private readonly Dictionary<string, TripRecord> trips = new(StringComparer.Ordinal);
  private SumoEngine? engine;
  private Scenario? scenario;
  private ObservationBuilder? builder;
  private PassageRecorder? passages;
  private RunStatus? current;
  private Snapshot? snapshot;
  private long snapshotSequence;
  private bool initialized;
  public int? EngineProcessId => engine?.ProcessId;

  public async Task InitializeAsync(CancellationToken token)
  {
    await storage.InitializeAsync(token);
    foreach (RunStatus saved in storage.LoadStatuses())
    {
      RunStatus status = saved;
      if (IsActiveState(status.State))
      {
        status = status with
        {
          State = "failed",
          Failure = "Service restarted before this run ended",
          FinishedAt = DateTimeOffset.UtcNow
        };
        await storage.SaveStatusAsync(status, token);
      }

      archive.Add(status.Id, status);
    }

    string requestsPath = Path.Combine(storage.Root, "requests.jsonl");
    if (File.Exists(requestsPath))
    {
      foreach (string line in File.ReadLines(requestsPath))
      {
        SavedRequest saved = JsonSerializer.Deserialize<SavedRequest>(line, JsonDefaults.Options)!;
        requests.Add(saved.Key, saved);
      }
    }

    initialized = true;
  }

  public async Task<RunStatus> CreateAsync(CreateRunRequest request, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      if (string.IsNullOrWhiteSpace(request.ScenarioId))
      {
        throw new ApiException(400, "invalid_scenario_id", "scenario_id is required");
      }

      string fingerprint = JsonSerializer.Serialize(request, RunStore.CompactOptions);
      if (TryReplay("create:" + request.RequestId, fingerprint, out RunStatus? replay))
      {
        return replay!;
      }

      Scenario selected = catalog.Get(request.ScenarioId);
      RunConfiguration config = ScenarioCatalog.Validate(selected, request);
      if (current is not null)
      {
        if (IsActiveState(current.State))
        {
          throw new ApiException(409, "active_run", "Stop the active run before creating another");
        }
      }

      RunStatus result = await CreateCoreAsync(selected, config, request.StartPaused, request.Overrides?.Pacing ?? new());
      await RememberAsync("create:" + request.RequestId, fingerprint, result);
      await EventAsync("create", request.RequestId);
      return result;
    }
    finally
    {
      gate.Release();
    }
  }

  private async Task<RunStatus> CreateCoreAsync(Scenario selected, RunConfiguration config, bool paused, Pacing pacing, bool resolvedDemand = false)
  {
    scenario = selected;
    string id = Guid.NewGuid().ToString("N");
    string directory = storage.DirectoryFor(id);
    selected = await PrepareRunScenarioAsync(selected, config, directory, resolvedDemand);
    ownership.Acquire(id);
    scenario = selected;
    InitializeRun(selected, config, id, directory, pacing);
    try
    {
      await SaveStatusAsync();
      string version = await engine.StartAsync(selected, config, directory, options, CancellationToken.None);
      current = current with
      {
        State = paused ? "paused" : "running",
        EngineVersion = version
      };
      await SaveStatusAsync();
    }
    catch (Exception ex)
    {
      await FailAsync(ex);
    }

    return current;
  }

  public async Task<RunStatus> CommandAsync(string id, string command, string requestId, int steps, Pacing? pacing, CancellationToken token)
  {
    ValidateCommand(requestId, command, steps, pacing);
    await gate.WaitAsync(token);
    try
    {
      string key = id + ":" + requestId;
      string fingerprint = JsonSerializer.Serialize(new RunCommandFingerprint(command, steps, pacing), RunStore.CompactOptions);
      if (TryReplay(key, fingerprint, out RunStatus? replay))
      {
        return replay!;
      }

      RunStatus target = Find(id);
      if (command == "reset")
      {
        return await ResetAsync(target, key, fingerprint, requestId);
      }

      if (current?.Id != id || IsFinishedState(target.State))
      {
        if (command == "stop" && (target.State == "stopped" || target.State == "completed"))
        {
          return target;
        }

        throw new ApiException(409, "invalid_state", $"Cannot {command} a {target.State} run");
      }

      await ExecuteCommandAsync(command, steps, pacing);
      await SaveStatusAsync();
      await EventAsync(command, requestId);
      await RememberAsync(key, fingerprint, current);
      return current;
    }
    finally
    {
      gate.Release();
    }
  }

  private static bool IsActiveState(string state) => state == "starting" || state == "running" || state == "paused";
  private static bool IsFinishedState(string state) => state == "completed" || state == "stopped" || state == "failed";
  private bool TryReplay(string key, string fingerprint, out RunStatus? result)
  {
    result = null;
    if (!requests.TryGetValue(key, out SavedRequest? saved))
    {
      return false;
    }

    if (saved.Fingerprint != fingerprint)
    {
      throw new ApiException(409, "request_id_conflict", "This request_id was already used for a different request");
    }

    result = saved.Result;
    return true;
  }

  private async Task RememberAsync(string key, string fingerprint, RunStatus result)
  {
    SavedRequest saved = new SavedRequest(key, fingerprint, result);
    string entry = JsonSerializer.Serialize(saved, RunStore.CompactOptions) + "\n";
    await File.AppendAllTextAsync(Path.Combine(storage.Root, "requests.jsonl"), entry);
    requests.Add(key, saved);
  }

  private Task EventAsync(string command, string requestId)
  {
    RunEvent entry = new(command, requestId, current!.SimulationTime, DateTimeOffset.UtcNow);
    return storage.AppendEventAsync(current.Id, entry, CancellationToken.None);
  }

  private async Task SaveStatusAsync()
  {
    archive[current!.Id] = current;
    await storage.SaveStatusAsync(current, CancellationToken.None);
  }

  private async Task AdvanceAsync()
  {
    try
    {
      EngineSnapshot next = await engine!.StepAsync(CancellationToken.None);
      await UpdateTripsAsync(next);
      current = current! with
      {
        SimulationTime = next.Time,
        ActiveVehicles = next.Vehicles.Length,
        InsertedVehicles = current.InsertedVehicles + next.Departed.Length,
        ArrivedVehicles = current.ArrivedVehicles + next.Arrived.Length,
        TeleportEvents = current.TeleportEvents + next.Teleports.Length,
        ExpectedVehicles = next.ExpectedVehicles
      };
      snapshot = new(current.Id, ++snapshotSequence, next.Time, next.Vehicles.Length, next.Vehicles, next.Signals);
      TrafficObservation[] frames = builder!.Advance(next);
      passages!.Advance(next);
      await storage.AppendObservationsAsync(current.Id, frames, CancellationToken.None);
      foreach (TrafficObservation frame in frames)
      {
        observations.Enqueue(frame);
      }

      while (observations.Count > options.ObservationRetention)
      {
        observations.Dequeue();
      }

      if (frames.Length > 0)
      {
        await SaveStatusAsync();
      }

      // Keep producing zero-traffic windows until the configured experiment ends.
      if (next.Time + TimeComparisonTolerance >= current.Configuration.DurationSeconds)
      {
        await FinishAsync("completed");
      }
    }
    catch (Exception ex)
    {
      await FailAsync(ex);
    }
  }

  private async Task FinishAsync(string state)
  {
    if (engine is not null)
    {
      try
      {
        await engine.DisposeAsync();
      }
      finally
      {
        engine = null;
        ownership.Release(current!.Id);
      }
    }

    ownership.Release(current!.Id);

    current = current! with
    {
      State = state,
      FinishedAt = DateTimeOffset.UtcNow
    };
    ReadEngineTrips();
    MarkUnfinishedTrips();
    List<TripRecord> orderedTrips = new(trips.Values);
    orderedTrips.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
    await storage.SaveTripsAsync(current.Id, orderedTrips.ToArray(), CancellationToken.None);
    await File.WriteAllTextAsync(Path.Combine(current.OutputDirectory, "passages.json"), JsonSerializer.Serialize(passages!.Records(), JsonDefaults.Options));
    await SaveStatusAsync();
  }

  private async Task FailAsync(Exception ex)
  {
    logger.LogError(ex, "Simulation run failed");
    current = current! with
    {
      State = "failed",
      Failure = ex.Message,
      FinishedAt = DateTimeOffset.UtcNow
    };
    try
    {
      await FinishAsync("failed");
    }
    catch (Exception recordingError)
    {
      logger.LogError(recordingError, "Could not finish recording failed run");
      archive[current.Id] = current;
      try
      {
        await storage.SaveStatusAsync(current, CancellationToken.None);
      }
      catch (Exception saveError)
      {
        logger.LogError(saveError, "Could not persist failure");
      }
    }
  }

  private static double Number(XElement element, string name)
  {
    return double.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture);
  }

  private RunStatus Find(string id)
  {
    return current?.Id == id ? current : archive.GetValueOrDefault(id) ?? throw new ApiException(404, "unknown_run", "Unknown run");
  }

  public async Task<T> ReadAsync<T>(Func<RunReader, T> read, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      return read(new RunReader(this));
    }
    finally
    {
      gate.Release();
    }
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!initialized)
    {
      throw new InvalidOperationException("Coordinator must be initialized before serving requests");
    }

    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        long started = Stopwatch.GetTimestamp();
        double delay = IdleDelayMilliseconds;
        await gate.WaitAsync(stoppingToken);
        try
        {
          if (current?.State == "running")
          {
            Pacing pacing = current.Pacing;
            double step = current.Configuration.StepSeconds;
            await AdvanceAsync();
            delay = pacing.Mode == "unpaced" ? MinimumDelayMilliseconds : Math.Max(MinimumDelayMilliseconds, step * MillisecondsPerSecond / pacing.Multiplier - Stopwatch.GetElapsedTime(started).TotalMilliseconds);
          }
        }
        finally
        {
          gate.Release();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(delay), stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
    finally
    {
      await gate.WaitAsync(CancellationToken.None);
      try
      {
        if (engine is not null)
        {
          await FinishAsync("stopped");
        }
      }
      finally
      {
        gate.Release();
      }
    }
  }

  private static async Task<Scenario> PrepareRunScenarioAsync(Scenario selected, RunConfiguration config, string directory, bool resolvedDemand)
  {
    Directory.CreateDirectory(directory);
    File.Copy(selected.NetworkPath, Path.Combine(directory, "network.net.xml"));
    DemandGenerator.Resolve(selected.RoutesPath, Path.Combine(directory, "routes.rou.xml"), resolvedDemand ? 1 : config.DemandMultiplier, config.Seed);
    ScenarioManifest runManifest = selected.Manifest.WithFiles("network.net.xml", "routes.rou.xml");
    await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(runManifest, JsonDefaults.Options));
    await File.WriteAllTextAsync(Path.Combine(directory, "cameras.json"), JsonSerializer.Serialize(selected.Cameras, JsonDefaults.Options));
    await File.WriteAllTextAsync(Path.Combine(directory, "checkpoints.json"), JsonSerializer.Serialize(selected.Checkpoints, JsonDefaults.Options));
    await File.WriteAllTextAsync(Path.Combine(directory, "configuration.json"), JsonSerializer.Serialize(config, JsonDefaults.Options));
    return ScenarioCatalog.Load(Path.Combine(directory, "manifest.json"));
  }

  [MemberNotNull(nameof(current), nameof(snapshot), nameof(builder), nameof(passages), nameof(engine))]
  private void InitializeRun(Scenario selected, RunConfiguration config, string id, string directory, Pacing pacing)
  {
    current = new(id, config, "starting", 0, pacing, DateTimeOffset.UtcNow, null, null, null, 0, 0, 0, 0, selected.Trips.Length, directory);
    snapshot = new(id, 0, 0, 0, [], []);
    snapshotSequence = 0;
    observations.Clear();
    trips.Clear();
    foreach (ScheduledTrip trip in selected.Trips)
    {
      trips.Add(trip.Id, new(trip.Id, "pending", trip.Departure, null, null, null, null, null, null, trip.Route));
    }

    builder = new(selected, id, config.ObservationWindowSeconds);
    passages = new(selected.Checkpoints);
    engine = new();
  }

  private static void ValidateCommand(string requestId, string command, int steps, Pacing? pacing)
  {
    if (string.IsNullOrWhiteSpace(requestId))
    {
      throw new ApiException(400, "invalid_request_id", "request_id is required and must be at most 128 characters");
    }

    if (requestId.Length > MaximumRequestIdLength)
    {
      throw new ApiException(400, "invalid_request_id", "request_id is required and must be at most 128 characters");
    }

    if (command == "step" && (steps < 1 || steps > MaximumStepCount))
    {
      throw new ApiException(400, "invalid_steps", "steps must be between 1 and 1000");
    }

    if (pacing is not null)
    {
      ScenarioCatalog.ValidatePacing(pacing);
    }
  }

  private async Task<RunStatus> ResetAsync(RunStatus target, string key, string fingerprint, string requestId)
  {
    if (current is not null)
    {
      bool anotherRunIsActive = current.Id != target.Id && IsActiveState(current.State);
      if (anotherRunIsActive)
      {
        throw new ApiException(409, "active_run", "Another run is active");
      }
    }

    if (current?.Id == target.Id && (target.State == "running" || target.State == "paused"))
    {
      await FinishAsync("stopped");
    }

    Scenario selected = ScenarioCatalog.Load(Path.Combine(storage.DirectoryFor(target.Id), "manifest.json"));
    // Saved demand has already been resolved; do not multiply it a second time.
    RunStatus result = await CreateCoreAsync(selected, target.Configuration, true, target.Pacing, resolvedDemand: true);
    await RememberAsync(key, fingerprint, result);
    await EventAsync("reset", requestId);
    return result;
  }

  private async Task ExecuteCommandAsync(string command, int steps, Pacing? pacing)
  {
    RunStatus target = current ?? throw new InvalidOperationException("No current run");
    switch (command)
    {
      case "pause":
        current = target with
        {
          State = "paused"
        };
        break;
      case "resume":
        current = target with
        {
          State = "running"
        };
        break;
      case "pacing":
        current = target with
        {
          Pacing = pacing!
        };
        break;
      case "stop":
        await FinishAsync("stopped");
        break;
      case "step":
        if (target.State != "paused")
        {
          throw new ApiException(409, "invalid_state", "Pause the run before stepping");
        }

        for (int i = 0; i < steps && current?.State == "paused"; i++)
        {
          await AdvanceAsync();
        }

        break;
      default:
        throw new ApiException(400, "invalid_command", "Unknown command");
    }
  }

  private async Task UpdateTripsAsync(EngineSnapshot next)
  {
    foreach (string id in next.Departed)
    {
      if (!trips.TryGetValue(id, out TripRecord? trip))
      {
        string[] route = [];
        foreach (VehicleState vehicle in next.Vehicles)
        {
          if (vehicle.Id == id)
          {
            SumoEngine activeEngine = engine ?? throw new InvalidOperationException("No active engine");
            route = await activeEngine.RouteAsync(id, CancellationToken.None);
            break;
          }
        }

        trip = new TripRecord(id, "pending", null, null, null, null, null, null, null, route);
      }

      double actualDeparture = next.Time - current!.Configuration.StepSeconds;
      trips[id] = trip with
      {
        Status = "active",
        Departure = actualDeparture,
        DepartureDelay = actualDeparture - trip.PlannedDeparture
      };
    }

    foreach (VehicleState v in next.Vehicles)
    {
      if (trips.TryGetValue(v.Id, out TripRecord? trip))
      {
        trips[v.Id] = trip with
        {
          CurrentLane = v.LaneId,
          Duration = next.Time - trip.Departure
        };
      }
    }

    foreach (string id in next.Arrived)
    {
      if (trips.TryGetValue(id, out TripRecord? trip))
      {
        trips[id] = trip with
        {
          Status = "completed",
          Arrival = next.Time,
          Duration = next.Time - trip.Departure,
          CurrentLane = null
        };
      }
    }
  }

  private void ReadEngineTrips()
  {
    RunStatus target = current ?? throw new InvalidOperationException("No current run");
    string path = Path.Combine(target.OutputDirectory, "tripinfo.xml");
    if (File.Exists(path))
    {
      try
      {
        foreach (XElement item in XDocument.Load(path).Root!.Elements("tripinfo"))
        {
          string id = (string)item.Attribute("id")!;
          TripRecord? old = trips.GetValueOrDefault(id);
          double departure = Number(item, "depart");
          double arrival = Number(item, "arrival");
          double delay = Number(item, "departDelay");
          string tripStatus = arrival >= 0 ? "completed" : "unfinished";
          double? tripArrival = arrival >= 0 ? arrival : null;
          double plannedDeparture = old?.PlannedDeparture ?? departure - delay;
          trips[id] = new TripRecord(id, tripStatus, plannedDeparture, departure, tripArrival, Number(item, "duration"), Number(item, "waitingTime"), Number(item, "timeLoss"), delay, old?.Route ?? [], null);
        }
      }
      catch (Exception ex) when (ex is System.Xml.XmlException or FormatException)
      {
        current = target with
        {
          State = "failed",
          Failure = $"Invalid engine trip output: {ex.Message}"
        };
      }
    }
  }

  private void MarkUnfinishedTrips()
  {
    List<string> activeTripIds = new();
    foreach (KeyValuePair<string, TripRecord> trip in trips)
    {
      if (trip.Value.Status == "active")
      {
        activeTripIds.Add(trip.Key);
      }
    }

    foreach (string tripId in activeTripIds)
    {
      trips[tripId] = trips[tripId] with
      {
        Status = "unfinished",
        CurrentLane = null
      };
    }
  }
}

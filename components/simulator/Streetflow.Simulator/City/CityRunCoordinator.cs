using System.Diagnostics;
using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Simulator.Sumo;

namespace Streetflow.Simulator.City;

public sealed partial class CityRunCoordinator(ProjectCatalog catalog, SimulatorOptions options, CityRunStorage storage, EngineOwnership ownership, ILogger<CityRunCoordinator> logger) : BackgroundService
{
  private const int MaximumStepCount = 1000;
  private const double MinimumStepSeconds = 0.05;
  private const double MaximumStepSeconds = 5;
  private const int IdleDelayMilliseconds = 20;
  private const int MillisecondsPerSecond = 1000;
  private const string ExternalSignalControlMode = "external-v1";
  private readonly SemaphoreSlim gate = new(1, 1);
  private readonly Dictionary<string, CityRunStatus> archive = new(StringComparer.Ordinal);
  private readonly Dictionary<string, PendingVehicleEntry> pendingEntries = new(StringComparer.Ordinal);
  private CityRunStatus? current;
  private CityProject? project;
  private ICityTrafficGenerator? generator;
  private SumoEngine? engine;
  private Snapshot? snapshot;
  private long sequence;
  private int liveVehicles;
  private double lastSavedTime;
  public int? EngineProcessId => engine?.ProcessId;

  public async Task InitializeAsync()
  {
    ValidateLimits();
    foreach (CityRunStatus saved in storage.Load())
    {
      CityRunStatus status = saved;
      if (IsActive(saved.State))
      {
        status = saved with { State = "failed", Failure = "Service restarted before this run ended; restart to replay from the beginning", FinishedAt = DateTimeOffset.UtcNow };
        await storage.SaveAsync(status);
      }

      archive.Add(status.Id, status);
    }
  }

  public async Task<CityRunStatus> CreateAsync(string projectId, CreateCityRunRequest request, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      CityProject selected = await catalog.GetAsync(projectId, token);
      CityRunConfiguration configuration = ResolveConfiguration(selected, request);
      // Loading replaces the loaded simulation; only one engine runs at a time.
      if (current is not null && IsActive(current.State))
      {
        await FinishAsync("stopped", null);
      }

      return await StartAsync(selected, configuration, request.StartPaused, request.Pacing ?? new Pacing());
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<CityRunStatus?> LoadedAsync(CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      return current;
    }
    finally
    {
      gate.Release();
    }
  }

  // Unloading stops any traffic and leaves no simulation loaded. Saved runs stay in the archive.
  public async Task UnloadAsync(CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      if (current is not null && IsActive(current.State))
      {
        await FinishAsync("stopped", null);
      }

      current = null;
      project = null;
      snapshot = null;
    }
    finally
    {
      gate.Release();
    }
  }

  private CityRunConfiguration ResolveConfiguration(CityProject selected, CreateCityRunRequest request)
  {
    double start = request.StartTimeSeconds ?? selected.Manifest.Defaults.StartTimeSeconds;
    double step = request.StepSeconds ?? selected.Manifest.Defaults.StepSeconds;
    if (request.Seed < 0 || !double.IsFinite(start) || start < 0 || start >= CityDemandGenerator.SecondsPerDay)
    {
      throw new ApiException(400, "invalid_run_settings", "seed must be nonnegative and start_time_seconds must be in [0, 86400)");
    }

    if (!double.IsFinite(step) || step < MinimumStepSeconds || step > MaximumStepSeconds)
    {
      throw new ApiException(400, "invalid_run_settings", "step_seconds must be from 0.05 to 5");
    }

    if (request.Pacing is not null)
    {
      ScenarioCatalog.ValidatePacing(request.Pacing);
    }

    TransformerGenerationLimits? limits = null;
    if (selected.Manifest.FormatVersion == 2)
    {
      limits = new TransformerGenerationLimits(options.MaximumTransformerStates, options.MaximumTransformerRouteMovements, options.MaximumTransformerCachedContexts, options.MaximumDynamicRoutes);
    }

    NeuralGenerationLimits? neuralLimits = null;
    if (selected.Manifest.FormatVersion == 3)
    {
      neuralLimits = new NeuralGenerationLimits(options.MaximumNeuralRouteMovements, options.MaximumDynamicRoutes);
    }

    return new CityRunConfiguration(selected.Manifest.Id, selected.Revision, GeneratorVersion(selected), request.Seed, start, step, options.MaximumLiveVehicles, options.MaximumPendingEntries, options.GenerationBatchSize, options.MaximumBacklogSeconds, ExternalSignalControlMode, limits, neuralLimits);
  }

  private async Task<CityRunStatus> StartAsync(CityProject selected, CityRunConfiguration configuration, bool paused, Pacing pacing)
  {
    string id = Guid.NewGuid().ToString("N");
    ownership.Acquire(id);
    try
    {
      project = storage.CopyProject(selected, id);
      if (project.Manifest.FormatVersion == 3)
      {
        generator = new NeuralDemandGenerator(project, configuration.Seed, configuration.StartTimeSeconds, configuration.NeuralLimits!);
      }
      else if (project.Manifest.FormatVersion == 2)
      {
        generator = new TransformerDemandGenerator(project, configuration.Seed, configuration.StartTimeSeconds, configuration.TransformerLimits!);
      }
      else
      {
        generator = new CityDemandGenerator(project, configuration.Seed, configuration.StartTimeSeconds);
      }
      storage.WriteRoutes(id, project, generator.Candidates);
      pendingEntries.Clear();
      sequence = 0;
      liveVehicles = 0;
      lastSavedTime = 0;
      current = new CityRunStatus(id, configuration, "starting", 0, configuration.StartTimeSeconds, 0, pacing, DateTimeOffset.UtcNow, null, null, null, 0, 0, 0, generator.Ended);
      snapshot = new Snapshot(id, 0, 0, 0, [], []);
      await SaveAsync();
      engine = new SumoEngine();
      string version = await engine.StartCityAsync(project, configuration, storage.DirectoryFor(id), options, CancellationToken.None);
      SignalState[] signals = await engine.GetSignalsAsync(CancellationToken.None);
      prioritySignalStates.Clear();
      foreach (SignalState signal in signals)
      {
        prioritySignalStates.Add(signal.Id, signal.State);
      }

      snapshot = snapshot with { Signals = signals, DeviceStates = TrafficDeviceStates.Read(project, signals) };
      current = current with { State = paused ? "paused" : "running", EngineVersion = version };
      await SaveAsync();
      return current;
    }
    catch (Exception exception)
    {
      if (current?.Id == id)
      {
        await FailAsync(exception);
        return current;
      }

      ownership.Release(id);
      DisposeGenerator();
      throw;
    }
  }

  public async Task<CityRunStatus[]> ListAsync(CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      List<CityRunStatus> statuses = new(archive.Values);
      statuses.Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
      return statuses.ToArray();
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<CityRunStatus> GetAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      return Find(id);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<Snapshot> SnapshotAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      CityRunStatus status = Find(id);
      if (current?.Id == id && snapshot is not null)
      {
        return snapshot;
      }

      return new Snapshot(id, 0, status.SimulationTime, 0, [], [], Available: false);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<CityRunMap> MapAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      _ = Find(id);
      CityProject selected;
      if (current?.Id == id && project is not null)
      {
        selected = project;
      }
      else
      {
        selected = CityProjectLoader.Load(Path.Combine(storage.DirectoryFor(id), "package"));
      }

      return new CityRunMap(selected.Manifest.Id, selected.Revision, selected.Manifest.Network.Attribution, selected.Lanes, selected.Junctions, selected.TerminalPoints, selected.SignalPoints) { TrafficDevices = selected.TrafficDevices };
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<CityRunStatus> CommandAsync(string id, string command, CityRunCommand request, CancellationToken token)
  {
    ValidateCommand(command, request);
    await gate.WaitAsync(token);
    try
    {
      CityRunStatus status = Find(id);
      if (command == "restart")
      {
        return await RestartAsync(status);
      }

      if (command == "stop" && !IsActive(status.State))
      {
        return status;
      }

      if (current?.Id != id || !IsActive(status.State))
      {
        throw new ApiException(409, "invalid_state", $"Cannot {command} a {status.State} run");
      }

      await ApplyCommandAsync(command, request);
      await SaveAsync();
      return current!;
    }
    finally
    {
      gate.Release();
    }
  }

  private async Task ApplyCommandAsync(string command, CityRunCommand request)
  {
    switch (command)
    {
      case "pause":
        current = current! with { State = "paused" };
        break;
      case "resume":
        current = current! with { State = "running" };
        break;
      case "pacing":
        current = current! with { Pacing = request.Pacing! };
        break;
      case "step":
        if (current!.State != "paused")
        {
          throw new ApiException(409, "invalid_state", "Pause the run before stepping");
        }

        for (int index = 0; index < request.Steps && current.State == "paused"; index++)
        {
          await AdvanceAsync();
        }

        break;
      case "stop":
        await FinishAsync("stopped", null);
        break;
    }
  }

  private async Task<CityRunStatus> RestartAsync(CityRunStatus status)
  {
    if (status.Configuration.SignalControlMode != ExternalSignalControlMode)
    {
      throw new ApiException(409, "incompatible_signal_control", "This saved run used automatic signals. Create a new run for external signal control");
    }

    if (status.Configuration.GeneratorVersion != CityDemandGenerator.Version && status.Configuration.GeneratorVersion != TransformerDemandGenerator.Version && status.Configuration.GeneratorVersion != NeuralDemandGenerator.Version)
    {
      throw new ApiException(409, "incompatible_generator", "This run requires a different generator version");
    }

    if (current is not null)
    {
      if (!IsActive(current.State))
      {
        return await ReplaySavedAsync(status);
      }
      if (current.Id != status.Id)
      {
        throw new ApiException(409, "active_run", "Stop the other active run before restarting");
      }

      await FinishAsync("stopped", null);
    }

    return await ReplaySavedAsync(status);
  }

  private async Task<CityRunStatus> ReplaySavedAsync(CityRunStatus status)
  {
    CityProject saved = CityProjectLoader.Load(Path.Combine(storage.DirectoryFor(status.Id), "package"));
    if (saved.Revision != status.Configuration.ProjectRevision)
    {
      throw new ApiException(409, "changed_project", "Saved project inputs changed; exact replay is unavailable");
    }

    if (status.Configuration.GeneratorVersion != GeneratorVersion(saved))
    {
      throw new ApiException(409, "incompatible_generator", "Saved project format does not match its generator version");
    }

    return await StartAsync(saved, status.Configuration, true, status.Pacing);
  }

  private async Task AdvanceAsync()
  {
    try
    {
      CityRunConfiguration configuration = current!.Configuration;
      int capacity = Math.Min(configuration.GenerationBatchSize, configuration.MaximumLiveVehicles - liveVehicles);
      capacity = Math.Min(capacity, configuration.MaximumPendingEntries - pendingEntries.Count);
      PendingVehicleEntry? blockedEntry = OldestPendingEntry();
      bool draining = false;
      if (blockedEntry is not null)
      {
        draining = current.SimulationTime - blockedEntry.SubmittedAt > configuration.MaximumBacklogSeconds;
      }
      if (draining)
      {
        capacity = 0;
      }

      double nextTime = current.SimulationTime + configuration.StepSeconds;
      GeneratedArrival[] arrivals = generator!.GenerateUntil(nextTime, Math.Max(0, capacity));
      await engine!.AddVehiclesAsync(arrivals, current.SimulationTime, CancellationToken.None);
      foreach (GeneratedArrival arrival in arrivals)
      {
        pendingEntries.Add(arrival.Id, new PendingVehicleEntry(arrival.Id, arrival.Route.Edges[0], arrival.Route.EntryTerminalId, current.SimulationTime));
      }

      EngineSnapshot next = await engine.StepAsync(CancellationToken.None);
      liveVehicles = next.ExpectedVehicles;
      current = current with { GridlockRecoveries = current.GridlockRecoveries + next.Teleports.Length, ArrivedVehicles = current.ArrivedVehicles + next.Arrived.Length };
      foreach (string departed in next.Departed)
      {
        pendingEntries.Remove(departed);
      }

      double departureBacklog = Math.Max(0, next.Time - generator.NextDepartureSeconds);
      PendingVehicleEntry? oldestEntry = OldestPendingEntry();
      double entryDelay = oldestEntry is null ? 0 : next.Time - oldestEntry.SubmittedAt;
      double backlog = Math.Max(departureBacklog, entryDelay);
      double absolute = configuration.StartTimeSeconds + next.Time;
      current = current with { SimulationTime = next.Time, CityTimeSeconds = absolute % CityDemandGenerator.SecondsPerDay, Day = (int)(absolute / CityDemandGenerator.SecondsPerDay), ActiveVehicles = next.Vehicles.Length, PendingEntries = pendingEntries.Count, BacklogSeconds = backlog, GenerationEnded = generator.Ended };
      snapshot = new Snapshot(current.Id, ++sequence, next.Time, next.Vehicles.Length, next.Vehicles, next.Signals) { DeviceStates = TrafficDeviceStates.Read(project!, next.Signals) };
      current = current with { Warning = null };
      if (backlog > configuration.MaximumBacklogSeconds)
      {
        string reason = entryDelay >= departureBacklog
          ? $"entry '{oldestEntry!.TerminalId ?? oldestEntry.EdgeId}' has been blocked for {entryDelay:F1}s"
          : $"requested departures are {departureBacklog:F1}s behind the city clock";
        current = current with { Warning = $"Traffic congestion: {reason}. Arrivals wait while blocked entries clear; the simulation continues." };
      }

      if (next.Time - lastSavedTime >= 5)
      {
        await SaveAsync();
        lastSavedTime = next.Time;
      }
    }
    catch (Exception exception)
    {
      await FailAsync(exception);
    }
  }

  private PendingVehicleEntry? OldestPendingEntry()
  {
    PendingVehicleEntry? oldest = null;
    foreach (PendingVehicleEntry entry in pendingEntries.Values)
    {
      if (oldest is null)
      {
        oldest = entry;
      }
      else if (entry.SubmittedAt < oldest.SubmittedAt)
      {
        oldest = entry;
      }
    }

    return oldest;
  }

  private async Task FailAsync(Exception exception)
  {
    logger.LogError(exception, "City simulation failed");
    await FinishAsync("failed", exception.Message);
  }

  private async Task FinishAsync(string state, string? failure)
  {
    try
    {
      if (engine is not null)
      {
        await engine.DisposeAsync();
        engine = null;
      }
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Could not close the city engine cleanly");
      state = "failed";
      failure = failure ?? exception.Message;
    }
    finally
    {
      engine = null;
      ownership.Release(current!.Id);
    }

    current = current! with { State = state, Failure = failure, FinishedAt = DateTimeOffset.UtcNow };
    pendingEntries.Clear();
    DisposeGenerator();
    if (state == "failed" && snapshot is not null)
    {
      snapshot = snapshot with { Available = false };
    }
    else
    {
      snapshot = new Snapshot(current.Id, sequence, current.SimulationTime, 0, [], [], Available: false);
    }

    await SaveAsync();
  }

  private async Task SaveAsync()
  {
    archive[current!.Id] = current;
    await storage.SaveAsync(current);
  }

  private CityRunStatus Find(string id) => current?.Id == id ? current : archive.GetValueOrDefault(id) ?? throw new ApiException(404, "unknown_run", "Unknown city run");
  private void DisposeGenerator()
  {
    if (generator is IDisposable disposable)
    {
      disposable.Dispose();
    }

    generator = null;
  }

  private static string GeneratorVersion(CityProject selected) => selected.Manifest.FormatVersion switch { 3 => NeuralDemandGenerator.Version, 2 => TransformerDemandGenerator.Version, _ => CityDemandGenerator.Version };

  private static bool IsActive(string state) => state == "starting" || state == "running" || state == "paused";

  private static void ValidateCommand(string command, CityRunCommand request)
  {
    if (command != "pause" && command != "resume" && command != "step" && command != "stop" && command != "restart" && command != "pacing")
    {
      throw new ApiException(400, "invalid_command", "Unknown city run command");
    }

    if (command == "step" && (request.Steps < 1 || request.Steps > MaximumStepCount))
    {
      throw new ApiException(400, "invalid_steps", "steps must be between 1 and 1000");
    }

    if (command == "pacing")
    {
      if (request.Pacing is null)
      {
        throw new ApiException(400, "invalid_pacing", "pacing is required");
      }

      ScenarioCatalog.ValidatePacing(request.Pacing);
    }
  }

  private void ValidateLimits()
  {
    bool valid = options.MaximumLiveVehicles > 0 && options.MaximumPendingEntries > 0 && options.GenerationBatchSize > 0;
    valid = valid && options.MaximumPendingEntries <= options.MaximumLiveVehicles;
    valid = valid && double.IsFinite(options.MaximumBacklogSeconds) && options.MaximumBacklogSeconds > 0;
    valid = valid && options.MaximumTransformerStates > 0 && options.MaximumTransformerRouteMovements > 0;
    valid = valid && options.MaximumTransformerCachedContexts > 0 && options.MaximumDynamicRoutes > 0;
    valid = valid && options.MaximumNeuralRouteMovements > 0;
    if (!valid)
    {
      throw new InvalidOperationException("City simulation limits must be positive; pending capacity must not exceed live capacity");
    }
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
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
            double step = current.Configuration.StepSeconds;
            Pacing pacing = current.Pacing;
            await AdvanceAsync();
            if (pacing.Mode == "unpaced")
            {
              delay = 1;
            }
            else
            {
              delay = Math.Max(1, step * MillisecondsPerSecond / pacing.Multiplier - Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
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
          await FinishAsync("stopped", null);
        }
      }
      finally
      {
        gate.Release();
      }
    }
  }
}

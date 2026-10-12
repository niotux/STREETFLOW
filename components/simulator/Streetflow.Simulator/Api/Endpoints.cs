using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public static class Endpoints
{
  private const int DefaultPageSize = 1000;
  private const int MaximumPageSize = 10000;
  private const string CoordinateSystem = "SUMO local meters";
  private static readonly string[] RunCommands = ["pause", "resume", "stop", "reset"];
  private static readonly string[] RunFiles = ["configuration.json", "manifest.json", "cameras.json", "checkpoints.json", "network.net.xml", "routes.rou.xml", "tripinfo.xml", "trips.json", "passages.json", "observations.jsonl", "events.jsonl", "engine.log"];
  private static readonly string[] SupportedFields = ["visible_vehicle_count", "crossed_vehicle_count", "queued_vehicle_count", "mean_speed_mps"];
  public static void MapSimulatorApi(this WebApplication app)
  {
    app.MapGet("/health", (SimulatorOptions options) =>
    {
      string storage = string.IsNullOrWhiteSpace(options.ConnectionString) ? "files" : "postgresql_and_files";
      return Results.Ok(new HealthResponse("ok", "sumo", storage));
    });
    app.MapGet("/api/v1", () => Results.Ok(new ApiInfoResponse("Streetflow simulator", "/openapi/v1.json", "/")));
    RouteGroupBuilder api = app.MapGroup("/api/v1");
    MapScenarioEndpoints(api);
    MapRunEndpoints(api);
    MapRunDataEndpoints(api);
    MapObservationEndpoints(api);
  }

  private static void MapScenarioEndpoints(RouteGroupBuilder api)
  {
    api.MapGet("/scenarios", (ScenarioCatalog catalog) => catalog.List());
    api.MapGet("/scenarios/{id}", (string id, ScenarioCatalog catalog) => catalog.Get(id).Info);
    api.MapPost("/scenarios/validate", (CreateRunRequest request, ScenarioCatalog catalog) => new ScenarioValidationResponse(true, ScenarioCatalog.Validate(catalog.Get(request.ScenarioId), request)));
  }

  private static void MapRunEndpoints(RouteGroupBuilder api)
  {
    api.MapPost("/runs", async (CreateRunRequest request, RunCoordinator coordinator, CancellationToken token) =>
    {
      RunStatus run = await coordinator.CreateAsync(request, token);
      return Results.Created($"/api/v1/runs/{run.Id}", run);
    });
    api.MapGet("/runs", (RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => reader.List(), token));
    api.MapGet("/runs/{id}", (string id, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => reader.Status(id), token));
    foreach (string command in RunCommands)
    {
      api.MapPost($"/runs/{{id}}/{command}", (string id, CommandRequest request, RunCoordinator coordinator, CancellationToken token) => coordinator.CommandAsync(id, command, request.RequestId, 0, null, token));
    }

    api.MapPost("/runs/{id}/step", (string id, StepRequest request, RunCoordinator coordinator, CancellationToken token) => coordinator.CommandAsync(id, "step", request.RequestId, request.Steps, null, token));
    api.MapPatch("/runs/{id}/pacing", (string id, PacingRequest request, RunCoordinator coordinator, CancellationToken token) => coordinator.CommandAsync(id, "pacing", request.RequestId, 0, new Pacing(request.Mode, request.Multiplier), token));
  }

  private static void MapRunDataEndpoints(RouteGroupBuilder api)
  {
    api.MapGet("/runs/{id}/snapshot", (string id, [AsParameters] SnapshotQuery query, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadSnapshot(reader, id, query.Limit, query.Viewport), token));
    api.MapGet("/runs/{id}/map", (string id, int? offset, int? limit, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadMap(reader, id, offset, limit), token));
    api.MapGet("/runs/{id}/lanes/{laneId}", (string id, string laneId, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadLane(reader, id, laneId), token));
    api.MapGet("/runs/{id}/vehicles/{vehicleId}", (string id, string vehicleId, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadVehicle(reader, id, vehicleId), token));
    api.MapGet("/runs/{id}/trips", (string id, int? offset, int? limit, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadTrips(reader, id, offset, limit), token));
    api.MapGet("/runs/{id}/results", (string id, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadResults(reader, id), token));
    api.MapGet("/runs/{id}/passages", (string id, int? offset, int? limit, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadPassages(reader, id, offset, limit), token));
  }

  private static void MapObservationEndpoints(RouteGroupBuilder api)
  {
    api.MapGet("/runs/{id}/observations", (string id, long? after, int? limit, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadArchivedObservations(reader, id, after, limit), token));
    api.MapGet("/traffic/catalog", (RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(ReadTrafficCatalog, token));
    api.MapGet("/traffic/observations", ([AsParameters] ObservationQuery query, RunCoordinator coordinator, CancellationToken token) => coordinator.ReadAsync(reader => ReadTrafficObservations(reader, query), token));
  }

  private static SnapshotResponse ReadSnapshot(RunCoordinator.RunReader reader, string id, int? limit, Viewport viewport)
  {
    int size = PageSize(limit);
    viewport.Validate();
    Snapshot snapshot = reader.Snapshot(id);
    List<VehicleState> vehicles = [];
    int viewportCount = 0;
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      if (!viewport.Contains(vehicle.X, vehicle.Y))
      {
        continue;
      }

      viewportCount++;
      if (vehicles.Count < size)
      {
        vehicles.Add(vehicle);
      }
    }

    bool sampled = snapshot.Sampled || viewportCount > size;
    return new SnapshotResponse(snapshot.RunId, snapshot.Sequence, snapshot.SimulationTime, snapshot.TotalActiveCount, viewportCount, vehicles.Count, snapshot.Available, sampled, vehicles.ToArray(), snapshot.Signals);
  }

  private static RunMapResponse ReadMap(RunCoordinator.RunReader reader, string id, int? offset, int? limit)
  {
    Scenario scenario = reader.Scenario(id);
    int start = Offset(offset);
    int size = PageSize(limit);
    return new RunMapResponse(scenario.Manifest.NetworkRevision, scenario.Manifest.Attribution, CoordinateSystem, scenario.Lanes.Length, start, Page(scenario.Lanes, start, size), scenario.Junctions.Length, Page(scenario.Junctions, start, size), scenario.Cameras, scenario.Checkpoints);
  }

  private static LaneResponse ReadLane(RunCoordinator.RunReader reader, string id, string laneId)
  {
    LaneMap? lane = null;
    foreach (LaneMap candidate in reader.Scenario(id).Lanes)
    {
      if (candidate.Id == laneId)
      {
        lane = candidate;
        break;
      }
    }

    if (lane is null)
    {
      throw new ApiException(404, "unknown_lane", "Unknown lane");
    }

    Snapshot snapshot = reader.Snapshot(id);
    List<VehicleState> vehicles = [];
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      if (vehicle.LaneId == laneId)
      {
        vehicles.Add(vehicle);
      }
    }

    int? visibleCount = snapshot.Available ? vehicles.Count : null;
    return new LaneResponse(lane, snapshot.SimulationTime, snapshot.Available, visibleCount, vehicles.ToArray());
  }

  private static VehicleResponse ReadVehicle(RunCoordinator.RunReader reader, string id, string vehicleId)
  {
    TripRecord? trip = null;
    foreach (TripRecord candidate in reader.Trips(id))
    {
      if (candidate.Id == vehicleId)
      {
        trip = candidate;
        break;
      }
    }

    if (trip is null)
    {
      throw new ApiException(404, "unknown_vehicle", "Unknown vehicle");
    }

    foreach (VehicleState vehicle in reader.Snapshot(id).Vehicles)
    {
      if (vehicle.Id == vehicleId)
      {
        return new VehicleResponse(trip, vehicle);
      }
    }

    return new VehicleResponse(trip, null);
  }

  private static TripsResponse ReadTrips(RunCoordinator.RunReader reader, string id, int? offset, int? limit)
  {
    TripRecord[] trips = reader.Trips(id);
    int start = Offset(offset);
    return new TripsResponse(trips.Length, start, Page(trips, start, PageSize(limit)));
  }

  private static ResultsResponse ReadResults(RunCoordinator.RunReader reader, string id)
  {
    RunStatus run = reader.Status(id);
    TripRecord[] trips = reader.Trips(id);
    int completed = 0;
    int pending = 0;
    int unfinished = 0;
    int durationCount = 0;
    double durationTotal = 0;
    foreach (TripRecord trip in trips)
    {
      if (trip.Status == "completed")
      {
        completed++;
        if (trip.Duration.HasValue)
        {
          durationTotal += trip.Duration.Value;
          durationCount++;
        }
      }
      else if (trip.Status == "pending")
      {
        pending++;
      }
      else if (trip.Status == "active" || trip.Status == "unfinished")
      {
        unfinished++;
      }
    }

    double? meanDuration = durationCount > 0 ? durationTotal / durationCount : null;
    bool resultsFinal = run.State == "completed" || run.State == "stopped";
    return new ResultsResponse(run, trips.Length, completed, pending, unfinished, meanDuration, resultsFinal, RunFiles);
  }

  private static PassagesResponse ReadPassages(RunCoordinator.RunReader reader, string id, int? offset, int? limit)
  {
    Passage[] passages = reader.Passages(id);
    int start = Offset(offset);
    return new PassagesResponse(passages.Length, start, Page(passages, start, PageSize(limit)));
  }

  private static ArchivedObservationsResponse ReadArchivedObservations(RunCoordinator.RunReader reader, string id, long? after, int? limit)
  {
    ValidateCursor(after);
    TrafficObservation[] frames = reader.ArchivedFrames(id, after ?? 0, PageSize(limit));
    long nextCursor = frames.Length > 0 ? frames[^1].Sequence : after ?? 0;
    return new ArchivedObservationsResponse(id, frames, nextCursor);
  }

  private static TrafficCatalogResponse ReadTrafficCatalog(RunCoordinator.RunReader reader)
  {
    Scenario? scenario = reader.CurrentScenario;
    return new TrafficCatalogResponse("1", reader.SessionId, scenario?.Manifest.NetworkRevision, scenario?.Manifest.DetectorConfigRevision, scenario?.Cameras ?? [], SupportedFields, "simulation_seconds", "ideal lane visibility with native SUMO counting-line events");
  }

  private static TrafficObservationsResponse ReadTrafficObservations(RunCoordinator.RunReader reader, ObservationQuery query)
  {
    if (query.SessionId is not null && query.SessionId != reader.SessionId)
    {
      throw new ApiException(409, "stale_session", "Read the catalog and use its current session_id");
    }

    ValidateCursor(query.After);
    int size = PageSize(query.Limit);
    TrafficObservation[] retained = reader.Frames;
    TrafficObservation[] frames = SelectObservations(retained, query, size);
    long? earliest = retained.Length > 0 ? retained[0].Sequence : null;
    long? latest = retained.Length > 0 ? retained[^1].Sequence : null;
    long? nextCursor = frames.Length > 0 ? frames[^1].Sequence : query.After;
    bool gap = query.After.HasValue && earliest.HasValue && query.After.Value < earliest.Value - 1;
    return new TrafficObservationsResponse(reader.SessionId, earliest, latest, nextCursor, gap, frames);
  }

  private static TrafficObservation[] SelectObservations(TrafficObservation[] retained, ObservationQuery query, int size)
  {
    List<TrafficObservation> matching = [];
    Dictionary<string, TrafficObservation> latestByDetector = new(StringComparer.Ordinal);
    foreach (TrafficObservation frame in retained)
    {
      if (!query.Matches(frame))
      {
        continue;
      }

      if (query.After.HasValue)
      {
        if (frame.Sequence > query.After.Value && matching.Count < size)
        {
          matching.Add(frame);
        }
      }
      else
      {
        latestByDetector[frame.DetectorId] = frame;
      }
    }

    if (!query.After.HasValue)
    {
      matching.AddRange(latestByDetector.Values);
      matching.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
      if (matching.Count > size)
      {
        matching.RemoveRange(size, matching.Count - size);
      }
    }

    return matching.ToArray();
  }

  internal static T[] Page<T>(T[] items, int start, int size)
  {
    int count = Math.Min(size, Math.Max(0, items.Length - start));
    T[] page = new T[count];
    if (count > 0)
    {
      Array.Copy(items, start, page, 0, count);
    }

    return page;
  }

  private static int PageSize(int? limit)
  {
    int size = limit ?? DefaultPageSize;
    if (size < 1 || size > MaximumPageSize)
    {
      throw new ApiException(400, "invalid_limit", "limit must be between 1 and 10000");
    }

    return size;
  }

  private static int Offset(int? offset)
  {
    if (offset < 0)
    {
      throw new ApiException(400, "invalid_offset", "offset must be nonnegative");
    }

    return offset ?? 0;
  }

  private static void ValidateCursor(long? after)
  {
    if (after < 0)
    {
      throw new ApiException(400, "invalid_cursor", "after must be nonnegative");
    }
  }
}

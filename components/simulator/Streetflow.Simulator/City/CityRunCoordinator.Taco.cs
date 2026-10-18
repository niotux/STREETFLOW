using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

public sealed partial class CityRunCoordinator
{
  private QueuePaths? queuePaths;

  public async Task<TacoTraffic> TacoTrafficAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      // Inspector snapshots have no camera regions or counting lines.
      // Sampled snapshots cannot establish complete lane counts.
      bool available = snapshot!.Available && !snapshot.Sampled;
      Dictionary<string, List<VehicleState>> vehicles = VehiclesByLane();
      QueuePaths paths = Paths();
      TacoDevice[] devices = project!.TrafficDevices;
      Dictionary<string, string> detectorByLane = new(StringComparer.Ordinal);
      foreach (ProjectDetector detector in project.Detectors)
      {
        foreach (string laneId in detector.LaneIds)
        {
          detectorByLane[laneId] = detector.Id;
        }
      }

      List<TacoLaneTraffic> lanes = [];
      foreach (LaneMap lane in TrafficLanes())
      {
        string[] deviceIds = devices.Where(d => d.LaneIds.Contains(lane.Id)).Select(d => d.Id).ToArray();
        string? detectorId = detectorByLane.GetValueOrDefault(lane.Id);
        int? visible = available ? vehicles.GetValueOrDefault(lane.Id)?.Count ?? 0 : null;
        int? queued = available && deviceIds.Length > 0 ? paths.Count(lane.Id, vehicles) : null;
        string method = detectorId is null ? "simulation_lane_snapshot" : "simulation_detector";
        lanes.Add(new TacoLaneTraffic(lane.Id, lane.Id, "unmapped", deviceIds,
          detectorId, null, null, visible, null, queued, null, available ? "partial" : "unavailable", method));
      }
      return new TacoTraffic(id, project.WorldId, project.WorldRevision, "simulation", "simulation_seconds",
        snapshot.Sequence, null, snapshot.SimulationTime, available ? "partial" : "unavailable", lanes.ToArray());
    }
    finally { gate.Release(); }
  }

  public async Task<byte[]> TacoQueuesAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      bool available = snapshot!.Available && !snapshot.Sampled;
      Dictionary<string, List<VehicleState>> vehicles = VehiclesByLane();
      QueuePaths paths = Paths();
      TacoDevice[] devices = project!.TrafficDevices;
      int?[][] counts = new int?[devices.Length][];
      for (int device = 0; device < devices.Length; device++)
      {
        string[] laneIds = devices[device].LaneIds;
        counts[device] = new int?[laneIds.Length];
        for (int lane = 0; lane < laneIds.Length; lane++)
        {
          counts[device][lane] = available ? paths.Count(laneIds[lane], vehicles) : null;
        }
      }
      return TacoQueueFrame.Encode(snapshot.Sequence, snapshot.SimulationTime, available, counts);
    }
    finally { gate.Release(); }
  }

  private QueuePaths Paths() => queuePaths?.Project == project ? queuePaths! : queuePaths = new QueuePaths(project!);

  private Dictionary<string, List<VehicleState>> VehiclesByLane()
  {
    Dictionary<string, List<VehicleState>> result = new(StringComparer.Ordinal);
    foreach (VehicleState vehicle in snapshot!.Vehicles)
    {
      if (!result.TryGetValue(vehicle.LaneId, out List<VehicleState>? list))
      {
        result[vehicle.LaneId] = list = [];
      }
      list.Add(vehicle);
    }
    return result;
  }

  // TACO describes vehicle traffic only; sidewalks, crossings and walking areas stay inside the simulator.
  private IEnumerable<LaneMap> TrafficLanes() =>
    project!.Lanes.Where(lane => lane.RenderKind is not ("sidewalk" or "crossing" or "walkingarea"));

  public async Task<TacoSession> TacoSessionAsync(CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      if (current is null || project is null || engine is null || !IsActive(current.State))
      {
        return new TacoSession(null, null, null, null, "none");
      }

      return new TacoSession(current.Id, project.WorldId, project.WorldRevision, project.Manifest.Id, current.State);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<TacoMap> TacoMapAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      LaneMap[] source = TrafficLanes().ToArray();
      IReadOnlyDictionary<string, string> upstream = Paths().Upstream;
      HashSet<string> published = new(source.Select(lane => lane.Id), StringComparer.Ordinal);
      TacoLane[] lanes = new TacoLane[source.Length];
      for (int index = 0; index < source.Length; index++)
      {
        TacoPoint[] points = new TacoPoint[source[index].Shape.Count];
        for (int point = 0; point < points.Length; point++)
        {
          Point position = source[index].Shape[point];
          points[point] = new TacoPoint(position.X, position.Y);
        }

        string? feeder = upstream.GetValueOrDefault(source[index].Id);
        lanes[index] = new TacoLane(source[index].Id, points) { UpstreamLaneId = feeder is not null && published.Contains(feeder) ? feeder : null };
      }

      TacoDetector[] detectors = new TacoDetector[project!.Detectors.Length];
      for (int index = 0; index < detectors.Length; index++)
      {
        ProjectDetector detector = project.Detectors[index];
        detectors[index] = new TacoDetector(detector.Id, detector.Label, detector.X, detector.Y, detector.LaneIds, detector.DeviceId);
      }
      return new TacoMap(project.WorldId, project.WorldRevision, project.Manifest.Network.Attribution, "local_meters", lanes, project.TrafficDevices)
      {
        Detectors = detectors
      };
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<TacoDeviceState[]> DevicesAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      return TrafficDeviceStates.Read(project!, snapshot!.Signals);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<TacoDeviceState> ControlDeviceAsync(string id, string deviceId, TacoDeviceCommand request, CancellationToken token)
  {
    ValidateSignalAction(request.Action);
    if (string.IsNullOrWhiteSpace(request.WorldId) || string.IsNullOrWhiteSpace(request.Revision))
    {
      throw new ApiException(400, "invalid_device_command", "world_id and revision are required");
    }
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      if (request.WorldId != project!.WorldId || request.Revision != project.WorldRevision)
      {
        throw new ApiException(409, "changed_world", "The active world or configuration revision differs; reload its map before sending commands");
      }

      ProjectTrafficDevice device = FindDevice(deviceId);
      RequireManualSignals();
      SignalState signal = FindSignal(device.SignalId);
      string off = prioritySignalStates[device.SignalId];
      char[] state = signal.State.ToCharArray();
      foreach (int link in device.LinkIndices)
      {
        if (link >= state.Length || link >= off.Length)
        {
          throw new ApiException(409, "invalid_device_mapping", $"Physical device '{deviceId}' does not match the running signal system");
        }

        state[link] = request.Action switch
        {
          "off" => off[link],
          "green" => 'G',
          "yellow" => 'y',
          "red-yellow" => 'u',
          _ => 'r'
        };
      }

      // One controller write changes every mapped movement together; engine steps share this gate.
      await engine!.SetSignalStateAsync(device.SignalId, new string(state), CancellationToken.None);
      SignalState[] signals = await engine.GetSignalsAsync(CancellationToken.None);
      TacoDeviceState[] devices = TrafficDeviceStates.Read(project, signals);
      snapshot = snapshot! with { Sequence = ++sequence, Signals = signals, DeviceStates = devices };
      foreach (TacoDeviceState applied in devices)
      {
        if (applied.Id == deviceId)
        {
          return applied;
        }
      }

      throw new InvalidOperationException("Configured physical device is missing from applied-state readback");
    }
    finally
    {
      gate.Release();
    }
  }

  private ProjectTrafficDevice FindDevice(string id)
  {
    if (project!.TrafficLights is not null)
    {
      foreach (ProjectTrafficDevice device in project.TrafficLights.Devices)
      {
        if (device.Id == id)
        {
          return device;
        }
      }
    }

    throw new ApiException(404, "unknown_device", $"Unknown physical device '{id}'");
  }
}

using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

public sealed partial class CityRunCoordinator
{
  public async Task<TacoTraffic> TacoTrafficAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      // Inspector snapshots have no camera regions or counting lines.
      // Sampled snapshots cannot establish complete lane counts.
      bool available = snapshot!.Available && !snapshot.Sampled;
      TacoLaneTraffic[] lanes = project!.Lanes.Select(lane => new TacoLaneTraffic(
        lane.Id, lane.Id, "unmapped", project.TrafficDevices.Where(d => d.LaneIds.Contains(lane.Id)).Select(d => d.Id).ToArray(),
        null, null, null, available ? snapshot.Vehicles.Count(v => v.LaneId == lane.Id) : null,
        null, null, null, available ? "partial" : "unavailable", "simulation_lane_snapshot")).ToArray();
      return new TacoTraffic(id, project.WorldId, project.WorldRevision, "simulation", "simulation_seconds",
        snapshot.Sequence, null, snapshot.SimulationTime, available ? "partial" : "unavailable", lanes);
    }
    finally { gate.Release(); }
  }

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
      LaneMap[] source = project!.Lanes;
      TacoLane[] lanes = new TacoLane[source.Length];
      for (int index = 0; index < source.Length; index++)
      {
        TacoPoint[] points = new TacoPoint[source[index].Shape.Count];
        for (int point = 0; point < points.Length; point++)
        {
          Point position = source[index].Shape[point];
          points[point] = new TacoPoint(position.X, position.Y);
        }

        lanes[index] = new TacoLane(source[index].Id, points);
      }

      return new TacoMap(project.WorldId, project.WorldRevision, project.Manifest.Network.Attribution, "local_meters", lanes, project.TrafficDevices);
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

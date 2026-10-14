using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

public sealed partial class CityRunCoordinator
{
  public async Task<TlcSession> TlcSessionAsync(CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      if (current is null || project is null || engine is null || !IsActive(current.State))
      {
        return new TlcSession(null, null, null, null, "none");
      }

      return new TlcSession(current.Id, project.WorldId, project.WorldRevision, project.Manifest.Id, current.State);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<TlcMap> TlcMapAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      LaneMap[] source = project!.Lanes;
      TlcLane[] lanes = new TlcLane[source.Length];
      for (int index = 0; index < source.Length; index++)
      {
        TlcPoint[] points = new TlcPoint[source[index].Shape.Count];
        for (int point = 0; point < points.Length; point++)
        {
          Point position = source[index].Shape[point];
          points[point] = new TlcPoint(position.X, position.Y);
        }

        lanes[index] = new TlcLane(source[index].Id, points);
      }

      return new TlcMap(project.WorldId, project.WorldRevision, project.Manifest.Network.Attribution, "local_meters", lanes, project.TrafficDevices);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<TlcDeviceState[]> DevicesAsync(string id, CancellationToken token)
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

  public async Task<TlcDeviceState> ControlDeviceAsync(string id, string deviceId, TlcDeviceCommand request, CancellationToken token)
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
      TlcDeviceState[] devices = TrafficDeviceStates.Read(project, signals);
      snapshot = snapshot! with { Sequence = ++sequence, Signals = signals, DeviceStates = devices };
      foreach (TlcDeviceState applied in devices)
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

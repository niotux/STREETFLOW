using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.City;

public sealed partial class CityRunCoordinator
{
  private readonly Dictionary<string, string> prioritySignalStates = new(StringComparer.Ordinal);

  public async Task<SignalState[]> SignalsAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      return snapshot!.Signals;
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<SignalState> ControlSignalAsync(string id, string signalId, SignalControlRequest request, CancellationToken token)
  {
    ValidateSignalAction(request.Action);
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      RequireManualSignals();
      if (!prioritySignalStates.TryGetValue(signalId, out string? offState))
      {
        throw new ApiException(404, "unknown_signal", $"Unknown signal controller '{signalId}'");
      }

      SignalState signal = FindSignal(signalId);
      string state = BuildSignalState(signal.State, offState, request);
      // Once a command starts, finish the write and readback even if the HTTP caller disconnects.
      await engine!.SetSignalStateAsync(signalId, state, CancellationToken.None);
      SignalState[] signals = await engine.GetSignalsAsync(CancellationToken.None);
      snapshot = snapshot! with { Sequence = ++sequence, Signals = signals, DeviceStates = TrafficDeviceStates.Read(project!, signals) };
      return FindSignal(signalId);
    }
    finally
    {
      gate.Release();
    }
  }

  private void RequireSignalRun(string id)
  {
    CityRunStatus status = Find(id);
    if (current?.Id != id || !IsActive(status.State) || engine is null)
    {
      throw new ApiException(409, "invalid_state", "Signal control requires a running or paused city run");
    }
  }

  private SignalState FindSignal(string signalId)
  {
    foreach (SignalState signal in snapshot!.Signals)
    {
      if (signal.Id == signalId)
      {
        return signal;
      }
    }

    throw new InvalidOperationException($"SUMO snapshot is missing controller '{signalId}'");
  }

  private static void ValidateSignalAction(string? action)
  {
    bool valid = action is "on" or "off" or "red" or "green" or "yellow" or "red-yellow";
    if (!valid)
    {
      throw new ApiException(400, "invalid_signal_action", "action must be on, off, red, green, yellow or red-yellow");
    }
  }

  private static string BuildSignalState(string currentState, string offState, SignalControlRequest request)
  {
    int start = 0;
    int end = currentState.Length;
    if (request.LinkIndex is not null)
    {
      int index = request.LinkIndex.Value;
      if (index < 0 || index >= currentState.Length)
      {
        throw new ApiException(400, "invalid_signal_link", $"link_index must be from 0 to {currentState.Length - 1}");
      }

      start = index;
      end = index + 1;
    }

    char[] state = currentState.ToCharArray();
    for (int index = start; index < end; index++)
    {
      state[index] = request.Action switch
      {
        "off" => offState[index],
        "green" => 'G',
        "yellow" => 'y',
        "red-yellow" => 'u',
        _ => 'r'
      };
    }

    return new string(state);
  }
}

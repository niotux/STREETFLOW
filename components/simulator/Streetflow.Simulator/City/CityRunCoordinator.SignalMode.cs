using Streetflow.Simulator.Runtime;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

public sealed partial class CityRunCoordinator
{
  private bool automaticSignals;
  private AdaptiveSignalController? adaptiveSignals;

  public async Task<TacoSignalMode> SignalModeAsync(string id, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      return ReadSignalMode();
    }
    finally { gate.Release(); }
  }

  public async Task<TacoSignalMode> SetSignalModeAsync(string id, TacoSignalModeCommand request, CancellationToken token)
  {
    if (request.Mode is not "automatic" and not "manual" || string.IsNullOrWhiteSpace(request.WorldId) || string.IsNullOrWhiteSpace(request.Revision))
    {
      throw new ApiException(400, "invalid_signal_mode", "mode must be automatic or manual; world_id and revision are required");
    }
    if (request.Policy?.Multiplier is double k && (!double.IsFinite(k) || k < .2 || k > 1.8))
      throw new ApiException(400, "invalid_signal_multiplier", "Multiplier k must be between 0.2 and 1.8, or null for automatic demand weighting");
    await gate.WaitAsync(token);
    try
    {
      RequireSignalRun(id);
      if (request.WorldId != project!.WorldId || request.Revision != project.WorldRevision)
      {
        throw new ApiException(409, "changed_world", "The active world or configuration revision differs; reload its map before sending commands");
      }
      bool automatic = request.Mode == "automatic";
      if (automatic && !engine!.AutomaticSignalsSupported)
      {
        throw new ApiException(409, "automatic_signals_unavailable", "This project has no usable signal timing programs");
      }
      bool policyChanged = request.Policy is not null && request.Policy != adaptiveSignals?.Policy;
      if (request.Policy is not null) adaptiveSignals?.Configure(request.Policy);
      if (automatic == automaticSignals && !policyChanged) return ReadSignalMode();

      // Timing runs in SUMO on the simulation clock; Melchior selects the mode.
      // Hold every controller's live state when returning to manual control.
      if (automatic)
      {
        if (adaptiveSignals is not null)
        {
          await ApplyAdaptiveSignalsAsync(snapshot!.Vehicles, snapshot.SimulationTime, current!.CityTimeSeconds, snapshot.Signals);
        }
        else await engine!.EnableAutomaticSignalsAsync(CancellationToken.None);
      }
      else
      {
        foreach (SignalState signal in snapshot!.Signals)
        {
          await engine!.SetSignalStateAsync(signal.Id, signal.State, CancellationToken.None);
        }
      }
      automaticSignals = automatic;
      SignalState[] signals = await engine!.GetSignalsAsync(CancellationToken.None);
      snapshot = snapshot! with { Sequence = ++sequence, Signals = signals, DeviceStates = TrafficDeviceStates.Read(project, signals) };
      return ReadSignalMode();
    }
    finally { gate.Release(); }
  }

  private TacoSignalMode ReadSignalMode() => new(automaticSignals ? "automatic" : "manual", engine!.AutomaticSignalsSupported)
  {
    AdaptiveSupported = adaptiveSignals is not null,
    Policy = adaptiveSignals?.Policy,
    AdaptiveDevices = adaptiveSignals?.Devices ?? []
  };

  private async Task<SignalState[]> ApplyAdaptiveSignalsAsync(VehicleState[] vehicles, double time, double citySeconds, SignalState[] previous)
  {
    SignalState[] desired = adaptiveSignals!.Update(vehicles, time, citySeconds);
    bool changed = false;
    foreach (SignalState signal in desired)
    {
      if (previous.FirstOrDefault(s => s.Id == signal.Id)?.State == signal.State) continue;
      await engine!.SetSignalStateAsync(signal.Id, signal.State, CancellationToken.None);
      changed = true;
    }
    return changed ? await engine!.GetSignalsAsync(CancellationToken.None) : previous;
  }

  private void RequireManualSignals()
  {
    if (automaticSignals)
    {
      throw new ApiException(409, "automatic_control_active", "Switch to manual control before commanding an individual light");
    }
  }
}

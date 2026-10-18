namespace Streetflow.Traffic.Contracts;

public sealed record TacoSignalMode(string Mode, bool AutomaticSupported)
{
  public bool AdaptiveSupported { get; init; }
  public TacoSignalPolicy? Policy { get; init; }
  public TacoAdaptiveDevice[] AdaptiveDevices { get; init; } = [];
}

public sealed record TacoSignalModeCommand(string Mode, string WorldId, string Revision)
{
  public TacoSignalPolicy? Policy { get; init; }
}

// A null multiplier derives k from A/B demand. Explicit k retains the same A/B
// identities: A*k, B/k. The provider executes this policy on simulation time.
public sealed record TacoSignalPolicy(bool PreventSpillback = true, bool WeightDemand = true, double? Multiplier = null);

public sealed record TacoAdaptiveDevice(string Id, bool QueueBuffer, int CapacityVehicles,
  int BlockedMovements, double DownstreamOccupancy, double Multiplier,
  double DemandA, double DemandB, bool RushHour, string GroupA, string GroupB,
  double GreenASeconds, double GreenBSeconds);

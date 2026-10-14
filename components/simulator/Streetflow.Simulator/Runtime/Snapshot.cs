using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Runtime;

public sealed record Snapshot(string RunId, long Sequence, double SimulationTime, int TotalActiveCount, VehicleState[] Vehicles, SignalState[] Signals, bool Sampled = false, bool Available = true)
{
  public TlcDeviceState[] DeviceStates { get; init; } = [];
}

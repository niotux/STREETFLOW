using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record SnapshotResponse(string RunId, long Sequence, double SimulationTime, int TotalActiveCount, int ViewportCount, int ReturnedCount, bool Available, bool Sampled, VehicleState[] Vehicles, SignalState[] Signals);

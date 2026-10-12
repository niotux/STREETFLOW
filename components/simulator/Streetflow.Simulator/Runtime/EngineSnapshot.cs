namespace Streetflow.Simulator.Runtime;

public sealed record EngineSnapshot(double Time, VehicleState[] Vehicles, SignalState[] Signals, string[] Departed, string[] Arrived, string[] Teleports, int ExpectedVehicles, LineCrossing[]? Crossings = null);

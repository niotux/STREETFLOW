using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record LaneResponse(LaneMap Lane, double SimulationTime, bool TrafficAvailable, int? VisibleCount, VehicleState[] Vehicles);

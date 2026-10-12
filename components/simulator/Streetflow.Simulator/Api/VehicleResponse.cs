using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record VehicleResponse(TripRecord Trip, VehicleState? Current);

using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record TripsResponse(int Total, int Offset, TripRecord[] Trips);

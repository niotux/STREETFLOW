using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public sealed record TrafficObservationsResponse(string? SessionId, long? EarliestSequence, long? LatestSequence, long? NextCursor, bool Gap, TrafficObservation[] Frames);

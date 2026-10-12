using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public sealed record ArchivedObservationsResponse(string SessionId, TrafficObservation[] Frames, long NextCursor);

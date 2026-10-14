namespace Streetflow.Traffic.Contracts;

public sealed record TlcSignalPoint(string SignalId, int LinkIndex, string LaneId, string ToLaneId, string? Direction, double X, double Y);

namespace Streetflow.Simulator.Runtime;

public sealed record SignalMapPoint(string SignalId, int LinkIndex, string LaneId, string ToLaneId, string? Direction, double X, double Y);

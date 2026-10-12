namespace Streetflow.Simulator.Runtime;

public sealed record JunctionMap(string Id, string Type, double X, double Y, IReadOnlyList<Point> Shape);

namespace Streetflow.Simulator.Runtime;

public sealed record TerminalMapPoint(string Id, string LaneId, string EdgeId, string Role, double PositionM, double X, double Y, string? Label);

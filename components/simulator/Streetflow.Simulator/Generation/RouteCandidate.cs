namespace Streetflow.Simulator.Generation;

public sealed record RouteCandidate(string Id, string VehicleTypeId, string[] Edges, double[] Lengths, double DistanceM, double TravelTimeSeconds)
{
  public string? EntryTerminalId { get; init; }
  public string? ExitTerminalId { get; init; }
  public int? DepartureLaneIndex { get; init; }
  public double? DeparturePositionM { get; init; }
  public int? ArrivalLaneIndex { get; init; }
  public double? ArrivalPositionM { get; init; }
}

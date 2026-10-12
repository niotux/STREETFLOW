namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectTerminal
{
  public required string Id { get; init; }
  public required string LaneId { get; init; }
  public required string Role { get; init; }
  public required double PositionM { get; init; }
  public string? Label { get; init; }
}

namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectDetector
{
  public required string Id { get; init; }
  public required string Label { get; init; }
  public required double X { get; init; }
  public required double Y { get; init; }
  public required string[] LaneIds { get; init; }
  public string? DeviceId { get; init; }
}

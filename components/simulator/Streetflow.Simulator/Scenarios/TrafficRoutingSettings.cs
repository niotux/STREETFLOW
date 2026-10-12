namespace Streetflow.Simulator.Scenarios;

public sealed record TrafficRoutingSettings
{
  public required string Cost { get; init; }
  public required int MaxAlternatives { get; init; }
  public required double MaxDetourRatio { get; init; }
}

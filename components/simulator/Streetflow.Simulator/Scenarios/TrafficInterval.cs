namespace Streetflow.Simulator.Scenarios;

public sealed record TrafficInterval
{
  public required double StartSeconds { get; init; }
  public required double EndSeconds { get; init; }
  public required double Weight { get; init; }
}

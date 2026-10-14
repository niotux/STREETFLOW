namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerPeriod
{
  public required double StartSeconds { get; init; }
  public required double EndSeconds { get; init; }
}

namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectDefaults
{
  public double StartTimeSeconds { get; init; }
  public double StepSeconds { get; init; } = 0.5;
}

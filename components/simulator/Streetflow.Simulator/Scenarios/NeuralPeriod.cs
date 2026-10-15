namespace Streetflow.Simulator.Scenarios;

public sealed record NeuralPeriod
{
  public required int StartSeconds { get; init; }
  public required int EndSeconds { get; init; }
  public required NeuralArrival[] Arrivals { get; init; }
}

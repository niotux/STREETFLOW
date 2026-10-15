namespace Streetflow.Simulator.Scenarios;

public sealed record NeuralArrival
{
  public required string StartEdge { get; init; }
  public required double VehiclesPerHour { get; init; }
}

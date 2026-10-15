namespace Streetflow.Simulator.Scenarios;

public sealed record NeuralRoad
{
  public required string EdgeId { get; init; }
  public required string[] NextEdgeIds { get; init; }
  public required bool CanExit { get; init; }
}

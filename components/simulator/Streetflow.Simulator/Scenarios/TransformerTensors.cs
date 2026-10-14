namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerTensors
{
  public required TransformerTensor ArrivalRates { get; init; }
  public required TransformerTensor JourneyLogits { get; init; }
  public required TransformerTensor MovementLogits { get; init; }
}

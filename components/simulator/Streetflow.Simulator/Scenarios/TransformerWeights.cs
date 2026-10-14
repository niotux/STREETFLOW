namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerWeights
{
  public required int FormatVersion { get; init; }
  public required TransformerTensors Tensors { get; init; }
}

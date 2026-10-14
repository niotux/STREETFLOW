namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerEvidence
{
  public required string[] TensorNames { get; init; }
  public required string Kind { get; init; }
  public required string Note { get; init; }
  public string? Source { get; init; }
}

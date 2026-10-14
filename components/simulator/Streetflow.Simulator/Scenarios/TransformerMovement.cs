namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerMovement
{
  public required string IncomingEdgeId { get; init; }
  public required string OutgoingEdgeId { get; init; }
}

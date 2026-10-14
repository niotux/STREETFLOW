namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerDestination
{
  public required string Id { get; init; }
  public required string[] TerminalIds { get; init; }
  public string? Label { get; init; }
}

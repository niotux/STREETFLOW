namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectNetwork
{
  public required string File { get; init; }
  public string? Attribution { get; init; }
}

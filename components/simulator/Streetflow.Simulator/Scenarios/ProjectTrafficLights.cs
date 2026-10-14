namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectTrafficLights
{
  public required int FormatVersion { get; init; }
  public required string WorldId { get; init; }
  public string? Description { get; init; }
  public required ProjectTrafficDevice[] Devices { get; init; }
}

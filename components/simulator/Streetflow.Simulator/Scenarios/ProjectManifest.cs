namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectManifest
{
  public required int FormatVersion { get; init; }
  public required string Id { get; init; }
  public required string Name { get; init; }
  public string? Description { get; init; }
  public required ProjectNetwork Network { get; init; }
  public required string DemandFile { get; init; }
  public string? DetectorsFile { get; init; }
  public string? TerminalsFile { get; init; }
  public string[] AdditionalFiles { get; init; } = [];
  public ProjectDefaults Defaults { get; init; } = new();
}

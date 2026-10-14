namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerModel
{
  public required int FormatVersion { get; init; }
  public required string Architecture { get; init; }
  public required string ArrivalModel { get; init; }
  public required bool RepeatDaily { get; init; }
  public required string RouteTime { get; init; }
  public required string WeightsFile { get; init; }
  public required TransformerAxes Axes { get; init; }
  public required TransformerEvidence[] Evidence { get; init; }
}

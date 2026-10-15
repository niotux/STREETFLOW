namespace Streetflow.Simulator.Scenarios;

public sealed record NeuralModel
{
  public required int FormatVersion { get; init; }
  public required string Architecture { get; init; }
  public required string ModelFile { get; init; }
  public required string NetworkSha256 { get; init; }
  public required string DatasetSha256 { get; init; }
  public required ProjectVehicleType VehicleType { get; init; }
  public required bool RepeatDaily { get; init; }
  public required int HiddenSize { get; init; }
  public required int MaxTurns { get; init; }
  public required int MaximumRouteMovements { get; init; }
  public required NeuralRoad[] Roads { get; init; }
  public required NeuralPeriod[] Periods { get; init; }
}

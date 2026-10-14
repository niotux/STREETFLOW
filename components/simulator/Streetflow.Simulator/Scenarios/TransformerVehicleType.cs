namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerVehicleType
{
  public required string Id { get; init; }
  public required string VehicleClass { get; init; }
  public string? Label { get; init; }
}

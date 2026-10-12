namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectVehicleType
{
  public required string Id { get; init; }
  public required string VehicleClass { get; init; }
  public required double Weight { get; init; }
}

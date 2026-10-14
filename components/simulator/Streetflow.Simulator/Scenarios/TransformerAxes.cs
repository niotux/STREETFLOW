namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerAxes
{
  public required TransformerPeriod[] Period { get; init; }
  public required string[] Origin { get; init; }
  public required TransformerDestination[] Destination { get; init; }
  public required string[] Category { get; init; }
  public required TransformerVehicleType[] VehicleType { get; init; }
  public required TransformerMovement[] Movement { get; init; }
}

namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectDemand
{
  public required int FormatVersion { get; init; }
  public required bool RepeatDaily { get; init; }
  public required EdgeTrafficProfile[] Profiles { get; init; }
  public required TrafficGenerationSettings Generation { get; init; }
  public required TrafficRoutingSettings Routing { get; init; }
  public required ProjectVehicleType[] VehicleTypes { get; init; }
}

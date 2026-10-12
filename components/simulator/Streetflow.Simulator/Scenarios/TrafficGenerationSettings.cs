namespace Streetflow.Simulator.Scenarios;

public sealed record TrafficGenerationSettings
{
  public required string ArrivalModel { get; init; }
  public required double MeanDeparturesPerHour { get; init; }
  public required string EndpointSelection { get; init; }
  public required double MinTripDistanceM { get; init; }
  public required double MaxTripDistanceM { get; init; }
  public required int CandidateLimit { get; init; }
}

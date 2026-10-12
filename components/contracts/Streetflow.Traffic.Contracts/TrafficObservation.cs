namespace Streetflow.Traffic.Contracts;

public sealed record TrafficObservation(string SessionId, long Sequence, string NetworkRevision, string DetectorConfigRevision, ObservationWindow Window, string IntersectionId, string DetectorId, IReadOnlyList<LaneObservation> Lanes, string Status = "ok")
{
  public string SchemaVersion => "1";
  public string Source => "simulation";
  public string TimeBasis => "simulation_seconds";
}

namespace Streetflow.Traffic.Contracts;

// Camera sessions have UTC windows; simulation windows remain numeric and unchanged.
public sealed record CameraObservation(string SessionId, long Sequence, string NetworkRevision,
  string DetectorConfigRevision, CameraWindow Window, string IntersectionId, string DetectorId,
  LaneObservation[] Lanes, string Status = "ok")
{
  public string SchemaVersion { get; init; } = "1";
  public string Source { get; init; } = "camera";
  public string TimeBasis { get; init; } = "utc";
  public bool SyntheticDemo { get; init; }
}

public sealed record CameraWindow(DateTimeOffset Start, DateTimeOffset End);

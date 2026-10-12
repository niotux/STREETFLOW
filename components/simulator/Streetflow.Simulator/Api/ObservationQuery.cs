using Microsoft.AspNetCore.Mvc;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public sealed class ObservationQuery
{
  [FromQuery(Name = "session_id")]
  public string? SessionId { get; set; }
  public long? After { get; set; }
  public int? Limit { get; set; }

  [FromQuery(Name = "detector_id")]
  public string? DetectorId { get; set; }

  [FromQuery(Name = "intersection_id")]
  public string? IntersectionId { get; set; }

  public bool Matches(TrafficObservation frame)
  {
    bool detectorMatches = DetectorId is null || frame.DetectorId == DetectorId;
    bool intersectionMatches = IntersectionId is null || frame.IntersectionId == IntersectionId;
    return detectorMatches && intersectionMatches;
  }
}

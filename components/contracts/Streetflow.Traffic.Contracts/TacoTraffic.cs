namespace Streetflow.Traffic.Contracts;

public sealed record TacoTraffic(string SessionId, string WorldId, string Revision, string Source,
  string TimeBasis, long Sequence, DateTimeOffset? ObservedAt, double? SimulationTime,
  string Status, TacoLaneTraffic[] Lanes);

public sealed record TacoLaneTraffic(string LaneId, string StreetId, string IntersectionId,
  string[] DeviceIds, string? DetectorId, DateTimeOffset? ObservedAt, double? AgeSeconds,
  int? VisibleVehicleCount, int? CrossedVehicleCount, int? QueuedVehicleCount,
  double? MeanSpeedMps, string MeasurementStatus, string MeasurementMethod);

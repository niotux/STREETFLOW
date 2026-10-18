namespace Streetflow.Casper;

public sealed record CameraStatus(string Id, string[] LaneIds, string CalibrationRevision,
  DateTimeOffset? PreviewObservedAt, double? PreviewAgeSeconds, string PreviewStatus,
  CameraMeasurementStatus Measurement);

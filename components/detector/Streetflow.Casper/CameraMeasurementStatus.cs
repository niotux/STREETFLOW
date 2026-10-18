namespace Streetflow.Casper;

public sealed record CameraMeasurementStatus(string Status, double? AgeSeconds, long? Sequence, DateTimeOffset? ObservedAt);

namespace Streetflow.Traffic.Contracts;

public sealed record LaneObservation(string ApproachId, string LaneId, int? VisibleVehicleCount, int? CrossedVehicleCount, int? QueuedVehicleCount, double? MeanSpeedMps, string MeasurementStatus = "ok");

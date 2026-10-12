namespace Streetflow.Simulator.Runtime;

public sealed record TripRecord(string Id, string Status, double? PlannedDeparture, double? Departure, double? Arrival, double? Duration, double? WaitingTime, double? TimeLoss, double? DepartureDelay, string[] Route, string? CurrentLane = null);

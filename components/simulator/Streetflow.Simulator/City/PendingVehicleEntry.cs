namespace Streetflow.Simulator.City;

internal sealed record PendingVehicleEntry(string VehicleId, string EdgeId, string? TerminalId, double SubmittedAt);

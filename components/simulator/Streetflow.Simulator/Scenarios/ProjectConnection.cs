namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectConnection(string From, string To, int FromLane, int ToLane, string? Via, string? SignalId = null, int? LinkIndex = null, string? Direction = null);

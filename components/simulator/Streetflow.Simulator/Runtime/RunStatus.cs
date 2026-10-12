namespace Streetflow.Simulator.Runtime;

public sealed record RunStatus(string Id, RunConfiguration Configuration, string State, double SimulationTime, Pacing Pacing, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string? Failure, string? EngineVersion, int ActiveVehicles, int InsertedVehicles, int ArrivedVehicles, int TeleportEvents, int ExpectedVehicles, string OutputDirectory);

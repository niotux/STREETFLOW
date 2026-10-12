namespace Streetflow.Simulator.Runtime;

public sealed record RunConfiguration(string ScenarioId, string ScenarioRevision, int Seed, double StepSeconds, double ObservationWindowSeconds, double DurationSeconds, double DemandMultiplier = 1);

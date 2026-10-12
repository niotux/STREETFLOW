namespace Streetflow.Simulator.Runtime;

public sealed record RunOverrides(double? StepSeconds = null, double? ObservationWindowSeconds = null, double? DurationSeconds = null, Pacing? Pacing = null, double? DemandMultiplier = null);

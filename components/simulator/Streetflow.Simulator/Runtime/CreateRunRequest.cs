namespace Streetflow.Simulator.Runtime;

public sealed record CreateRunRequest(string ScenarioId, string RequestId, string? ScenarioRevision = null, int Seed = 42, bool StartPaused = true, RunOverrides? Overrides = null);

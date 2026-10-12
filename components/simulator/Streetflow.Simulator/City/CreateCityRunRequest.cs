using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.City;

public sealed record CreateCityRunRequest(int Seed, double? StartTimeSeconds = null, double? StepSeconds = null, bool StartPaused = true, Pacing? Pacing = null);

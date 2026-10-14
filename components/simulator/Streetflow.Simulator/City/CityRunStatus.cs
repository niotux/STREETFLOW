using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.City;

public sealed record CityRunStatus(string Id, CityRunConfiguration Configuration, string State, double SimulationTime, double CityTimeSeconds, int Day, Pacing Pacing, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string? EngineVersion, string? Failure, int ActiveVehicles, int PendingEntries, double BacklogSeconds, bool GenerationEnded)
{
  public string? Warning { get; init; }
  public int GridlockRecoveries { get; init; }
  public int ArrivedVehicles { get; init; }
}

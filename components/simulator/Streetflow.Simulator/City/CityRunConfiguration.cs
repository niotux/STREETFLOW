namespace Streetflow.Simulator.City;

public sealed record CityRunConfiguration(string ProjectId, string ProjectRevision, string GeneratorVersion, int Seed, double StartTimeSeconds, double StepSeconds, int MaximumLiveVehicles, int MaximumPendingEntries, int GenerationBatchSize, double MaximumBacklogSeconds);

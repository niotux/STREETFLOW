using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record ResultsResponse(RunStatus Run, int KnownScheduledTrips, int Completed, int Pending, int Unfinished, double? MeanCompletedDurationSeconds, bool ResultsFinal, string[] Files);

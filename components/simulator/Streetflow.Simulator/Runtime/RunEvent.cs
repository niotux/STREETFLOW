namespace Streetflow.Simulator.Runtime;

internal sealed record RunEvent(string Command, string RequestId, double SimulationTime, DateTimeOffset WallTime);

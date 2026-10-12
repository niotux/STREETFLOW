namespace Streetflow.Simulator.Scenarios;

public sealed record ScenarioInfo(ScenarioManifest Manifest, int LaneCount, int JunctionCount, int ScheduledVehicles, Camera[] Cameras);

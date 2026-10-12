using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public sealed class Scenario
{
  public required string Directory { get; init; }
  public required ScenarioManifest Manifest { get; init; }
  public required LaneMap[] Lanes { get; init; }
  public required JunctionMap[] Junctions { get; init; }
  public required Camera[] Cameras { get; init; }
  public required ScheduledTrip[] Trips { get; init; }
  public CheckpointConfiguration Checkpoints { get; init; } = new([], []);
  public ScenarioInfo Info => new(Manifest, Lanes.Length, Junctions.Length, Trips.Length, Cameras);
  public string NetworkPath => Path.Combine(Directory, Manifest.NetworkFile);
  public string RoutesPath => Path.Combine(Directory, Manifest.RoutesFile);
}

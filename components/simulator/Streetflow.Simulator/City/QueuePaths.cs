using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.City;

// An approach starts at a controlled lane's stop line and extends upstream through lanes that have exactly one feeder.
// Short network pieces before a junction would otherwise hide most of the cars waiting for the light.
public sealed class QueuePaths
{
  public const double MaximumMeters = 250;

  private readonly Dictionary<string, LaneMap> lanes = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> upstream = new(StringComparer.Ordinal);

  public QueuePaths(CityProject project)
  {
    Project = project;
    Dictionary<string, ProjectEdge> edges = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in project.Edges)
    {
      edges.Add(edge.Id, edge);
      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(lane.Map.Id, lane.Map);
      }
    }

    Dictionary<string, HashSet<string>> internalFeeders = new(StringComparer.Ordinal);
    Dictionary<string, HashSet<string>> directFeeders = new(StringComparer.Ordinal);
    foreach (ProjectConnection connection in project.Connections)
    {
      string from = LaneId(connection.From, connection.FromLane);
      string to = LaneId(connection.To, connection.ToLane);
      if (connection.Via is not null)
      {
        Add(directFeeders, connection.Via, from);
      }
      bool fromInternal = edges.TryGetValue(connection.From, out ProjectEdge? source) && !source.Ordinary;
      Add(fromInternal ? internalFeeders : directFeeders, to, connection.Via ?? from);
    }

    foreach (string lane in lanes.Keys)
    {
      // Connections leaving a junction's internal lanes describe the last internal piece; prefer them.
      HashSet<string>? feeders = internalFeeders.GetValueOrDefault(lane) ?? directFeeders.GetValueOrDefault(lane);
      if (feeders is { Count: 1 })
      {
        string feeder = feeders.First();
        if (lanes.ContainsKey(feeder))
        {
          upstream[lane] = feeder;
        }
      }
    }
  }

  public CityProject Project { get; }
  public IReadOnlyDictionary<string, string> Upstream => upstream;

  // Every vehicle on the approach within MaximumMeters of the stop line, moving or stopped.
  public int Count(string laneId, IReadOnlyDictionary<string, List<VehicleState>> vehiclesByLane)
  {
    int count = 0;
    HashSet<string> visited = new(StringComparer.Ordinal);
    double offset = 0;
    string? current = laneId;
    while (current is not null && offset < MaximumMeters && visited.Add(current) && lanes.TryGetValue(current, out LaneMap? lane))
    {
      if (vehiclesByLane.TryGetValue(current, out List<VehicleState>? vehicles))
      {
        foreach (VehicleState vehicle in vehicles)
        {
          if (offset + lane.Length - vehicle.LanePosition <= MaximumMeters)
          {
            count++;
          }
        }
      }
      offset += lane.Length;
      current = upstream.GetValueOrDefault(current);
    }
    return count;
  }

  private static string LaneId(string edge, int index) => $"{edge}_{index}";

  private static void Add(Dictionary<string, HashSet<string>> feeders, string lane, string feeder)
  {
    if (!feeders.TryGetValue(lane, out HashSet<string>? set))
    {
      feeders[lane] = set = new(StringComparer.Ordinal);
    }
    set.Add(feeder);
  }
}

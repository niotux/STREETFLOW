using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

internal sealed class NeuralConnectionPermissions
{
  private readonly string vehicleClass;
  private readonly Dictionary<string, ProjectLane> lanes = new(StringComparer.Ordinal);
  private readonly Dictionary<string, ProjectLane> byLaneId = new(StringComparer.Ordinal);
  private readonly Dictionary<string, List<ProjectConnection>> connections = new(StringComparer.Ordinal);
  private readonly Dictionary<string, bool> cache = new(StringComparer.Ordinal);
  private readonly HashSet<string> internalEdges = new(StringComparer.Ordinal);

  public NeuralConnectionPermissions(ProjectEdge[] edges, ProjectConnection[] projectConnections, string vehicleClass)
  {
    this.vehicleClass = vehicleClass;
    foreach (ProjectEdge edge in edges)
    {
      if (!edge.Ordinary)
      {
        internalEdges.Add(edge.Id);
      }

      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(LaneKey(edge.Id, lane.Index), lane);
        byLaneId.Add(lane.Map.Id, lane);
      }
    }

    foreach (ProjectConnection connection in projectConnections)
    {
      string key = LaneKey(connection.From, connection.FromLane);
      if (!connections.TryGetValue(key, out List<ProjectConnection>? following))
      {
        following = [];
        connections.Add(key, following);
      }

      following.Add(connection);
    }
  }

  public bool Permits(ProjectConnection connection)
  {
    if (connection.Via is null)
    {
      return true;
    }

    string key = connection.Via + "\0" + LaneKey(connection.To, connection.ToLane);
    if (!cache.TryGetValue(key, out bool permitted))
    {
      permitted = FindPath(connection);
      cache.Add(key, permitted);
    }

    return permitted;
  }

  private bool FindPath(ProjectConnection connection)
  {
    Queue<ProjectLane> pending = new();
    HashSet<string> visited = new(StringComparer.Ordinal);
    pending.Enqueue(byLaneId[connection.Via!]);
    while (pending.TryDequeue(out ProjectLane? lane))
    {
      if (!lane.Permits(vehicleClass) || !visited.Add(lane.Map.Id))
      {
        continue;
      }

      if (!connections.TryGetValue(LaneKey(lane.Map.EdgeId, lane.Index), out List<ProjectConnection>? following))
      {
        continue;
      }

      foreach (ProjectConnection next in following)
      {
        bool reachesTarget = next.To == connection.To && next.ToLane == connection.ToLane;
        if (reachesTarget && next.Via is null)
        {
          return true;
        }

        if (!reachesTarget && !internalEdges.Contains(next.To))
        {
          continue;
        }

        ProjectLane target = next.Via is null ? lanes[LaneKey(next.To, next.ToLane)] : byLaneId[next.Via];
        pending.Enqueue(target);
      }
    }

    return false;
  }

  private static string LaneKey(string edge, int index) => edge + "\0" + index;
}

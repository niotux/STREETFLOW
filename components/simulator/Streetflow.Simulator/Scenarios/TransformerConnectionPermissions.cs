namespace Streetflow.Simulator.Scenarios;

public static class TransformerConnectionPermissions
{
  public static bool HasPermittedInternalPath(ProjectConnection connection, Dictionary<string, ProjectLane> lanes, ProjectConnection[] connections, string vehicleClass)
  {
    if (connection.Via is null)
    {
      return true;
    }

    ProjectLane first = lanes[connection.Via];
    if (!first.Permits(vehicleClass))
    {
      return false;
    }

    HashSet<string> internalEdges = ReadInternalEdges(lanes, connections);
    Queue<ProjectLane> pending = new();
    HashSet<string> visited = new(StringComparer.Ordinal);
    pending.Enqueue(first);
    visited.Add(first.Map.Id);
    while (pending.Count > 0)
    {
      ProjectLane current = pending.Dequeue();
      foreach (ProjectConnection next in connections)
      {
        if (next.From != current.Map.EdgeId || next.FromLane != current.Index)
        {
          continue;
        }

        bool reachesTarget = next.To == connection.To && next.ToLane == connection.ToLane;
        if (reachesTarget && next.Via is null)
        {
          return true;
        }

        if (!reachesTarget && !internalEdges.Contains(next.To))
        {
          continue;
        }

        ProjectLane? following = FindFollowingLane(next, lanes);
        if (following is not null && following.Permits(vehicleClass) && visited.Add(following.Map.Id))
        {
          pending.Enqueue(following);
        }
      }
    }

    return false;
  }

  private static ProjectLane? FindFollowingLane(ProjectConnection connection, Dictionary<string, ProjectLane> lanes)
  {
    if (connection.Via is not null)
    {
      return lanes[connection.Via];
    }

    foreach (ProjectLane lane in lanes.Values)
    {
      if (lane.Map.EdgeId == connection.To && lane.Index == connection.ToLane)
      {
        return lane;
      }
    }

    return null;
  }

  private static HashSet<string> ReadInternalEdges(Dictionary<string, ProjectLane> lanes, ProjectConnection[] connections)
  {
    HashSet<string> internalEdges = new(StringComparer.Ordinal);
    foreach (ProjectLane lane in lanes.Values)
    {
      if (lane.Map.EdgeId.StartsWith(':'))
      {
        internalEdges.Add(lane.Map.EdgeId);
      }
    }

    foreach (ProjectConnection connection in connections)
    {
      if (connection.Via is not null)
      {
        internalEdges.Add(lanes[connection.Via].Map.EdgeId);
      }
    }

    return internalEdges;
  }
}

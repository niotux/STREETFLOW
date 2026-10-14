using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

// A terminal lane must have a continuous lane path, including short connector roads.
internal sealed class TerminalRoutePlanner
{
  private const double MinimumLaneChangeDistanceM = 20;
  private const double LaneChangeCostSeconds = 3;
  private readonly List<RouteGraphEdge> graph;
  private readonly string vehicleClass;
  private readonly List<TerminalLane> lanes = [];
  private readonly Dictionary<string, int> laneIndices = new(StringComparer.Ordinal);
  private readonly Dictionary<string, ProjectLane> allLanes = new(StringComparer.Ordinal);
  private readonly Dictionary<string, int> edgeIndices = new(StringComparer.Ordinal);

  public TerminalRoutePlanner(CityProject project, List<RouteGraphEdge> graph, string vehicleClass)
  {
    this.graph = graph;
    this.vehicleClass = vehicleClass;
    foreach (ProjectEdge edge in project.Edges)
    {
      foreach (ProjectLane lane in edge.Lanes)
      {
        allLanes.Add(lane.Map.Id, lane);
      }
    }

    for (int edgeIndex = 0; edgeIndex < graph.Count; edgeIndex++)
    {
      edgeIndices.Add(graph[edgeIndex].Edge.Id, edgeIndex);
      foreach (ProjectLane lane in graph[edgeIndex].Edge.Lanes)
      {
        if (lane.Permits(vehicleClass))
        {
          laneIndices.Add(lane.Map.Id, lanes.Count);
          lanes.Add(new TerminalLane(edgeIndex, lane));
        }
      }
    }

    foreach (ProjectConnection connection in project.Connections)
    {
      if (connection.Direction == "t")
      {
        continue;
      }

      if (!edgeIndices.TryGetValue(connection.From, out int from) || !edgeIndices.TryGetValue(connection.To, out int to))
      {
        continue;
      }

      ProjectLane fromLane = graph[from].Edge.Lanes[connection.FromLane];
      ProjectLane toLane = graph[to].Edge.Lanes[connection.ToLane];
      bool hasFrom = laneIndices.TryGetValue(fromLane.Map.Id, out int fromIndex);
      bool hasTo = laneIndices.TryGetValue(toLane.Map.Id, out int toIndex);
      bool permitted = hasFrom && hasTo;
      if (connection.Via is not null)
      {
        permitted = permitted && allLanes[connection.Via].Permits(vehicleClass);
      }

      if (permitted)
      {
        lanes[fromIndex].Next.Add(toIndex);
      }
    }
  }

  public TerminalPath? Search(RouteEndpoints endpoints, BlockedMovement? blocked)
  {
    int start = laneIndices[endpoints.Entry.Lane.Map.Id];
    int end = laneIndices[endpoints.Exit.Lane.Map.Id];
    double[] costs = new double[lanes.Count];
    int[] previous = new int[lanes.Count];
    Array.Fill(costs, double.PositiveInfinity);
    Array.Fill(previous, -1);
    costs[start] = EntryTravelTime(endpoints);
    PriorityQueue<int, double> pending = new();
    pending.Enqueue(start, costs[start]);
    while (pending.TryDequeue(out int index, out double cost))
    {
      if (cost > costs[index])
      {
        continue;
      }

      if (index == end)
      {
        return Reconstruct(previous, end, cost);
      }

      foreach (int next in lanes[index].Next)
      {
        if (IsBlocked(index, next, blocked))
        {
          continue;
        }

        double length = lanes[next].Lane.Map.Length;
        if (lanes[next].EdgeIndex == endpoints.Exit.EdgeIndex)
        {
          length = endpoints.Exit.Terminal.PositionM;
        }

        Relax(index, next, cost + length / lanes[next].Lane.Map.Speed, costs, previous, pending);
      }

      AddLaneChanges(index, cost, endpoints, costs, previous, pending);
    }

    return null;
  }

  private static double EntryTravelTime(RouteEndpoints endpoints)
  {
    double length = endpoints.Entry.Lane.Map.Length - endpoints.Entry.Terminal.PositionM;
    if (endpoints.Entry.EdgeIndex == endpoints.Exit.EdgeIndex)
    {
      length = endpoints.Exit.Terminal.PositionM - endpoints.Entry.Terminal.PositionM;
    }

    return length / endpoints.Entry.Lane.Map.Speed;
  }

  private bool IsBlocked(int from, int to, BlockedMovement? blocked)
  {
    if (blocked is null)
    {
      return false;
    }

    return lanes[from].EdgeIndex == blocked.From && lanes[to].EdgeIndex == blocked.To;
  }

  private void AddLaneChanges(int index, double cost, RouteEndpoints endpoints, double[] costs, int[] previous, PriorityQueue<int, double> pending)
  {
    TerminalLane current = lanes[index];
    double available = current.Lane.Map.Length;
    if (current.EdgeIndex == endpoints.Entry.EdgeIndex)
    {
      available -= endpoints.Entry.Terminal.PositionM;
    }

    if (current.EdgeIndex == endpoints.Exit.EdgeIndex)
    {
      available = endpoints.Exit.Terminal.PositionM;
      if (current.EdgeIndex == endpoints.Entry.EdgeIndex)
      {
        available -= endpoints.Entry.Terminal.PositionM;
      }
    }

    if (available < MinimumLaneChangeDistanceM)
    {
      return;
    }

    ProjectLane[] edgeLanes = graph[current.EdgeIndex].Edge.Lanes;
    for (int nextLane = Math.Max(0, current.Lane.Index - 1); nextLane <= Math.Min(edgeLanes.Length - 1, current.Lane.Index + 1); nextLane++)
    {
      if (nextLane == current.Lane.Index || !edgeLanes[nextLane].Permits(vehicleClass))
      {
        continue;
      }

      int next = laneIndices[edgeLanes[nextLane].Map.Id];
      Relax(index, next, cost + LaneChangeCostSeconds, costs, previous, pending);
    }
  }

  private static void Relax(int from, int to, double cost, double[] costs, int[] previous, PriorityQueue<int, double> pending)
  {
    if (cost < costs[to])
    {
      costs[to] = cost;
      previous[to] = from;
      pending.Enqueue(to, cost);
    }
  }

  private TerminalPath Reconstruct(int[] previous, int end, double cost)
  {
    List<int> path = [];
    int index = end;
    while (index >= 0)
    {
      int edge = lanes[index].EdgeIndex;
      if (path.Count == 0 || path[^1] != edge)
      {
        path.Add(edge);
      }

      index = previous[index];
    }

    path.Reverse();
    return new TerminalPath(path.ToArray(), cost);
  }
}

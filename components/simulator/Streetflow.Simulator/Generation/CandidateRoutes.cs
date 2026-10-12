using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

// Candidate paths are prepared once. Annotated terminals constrain through journeys.
public sealed class CandidateRoutes
{
  private readonly CityProject project;
  private readonly Random random;
  private readonly List<RouteCandidate> candidates = [];
  private readonly Dictionary<string, RouteEndpoints> routeEndpoints = new(StringComparer.Ordinal);
  private string vehicleClass = "";
  private TerminalRoutePlanner? terminalPlanner;
  private readonly Dictionary<string, ProjectLane> lanes = new(StringComparer.Ordinal);
  public CandidateRoutes(CityProject project, int seed)
  {
    this.project = project;
    random = new Random(seed);
    foreach (ProjectEdge edge in project.Edges)
    {
      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(lane.Map.Id, lane);
      }
    }
  }

  public RouteCandidate[] Build()
  {
    foreach (ProjectVehicleType type in project.Demand.VehicleTypes)
    {
      if (type.Weight > 0)
      {
        BuildForType(type);
      }
    }

    return candidates.ToArray();
  }

  private void BuildForType(ProjectVehicleType type)
  {
    vehicleClass = type.VehicleClass;
    List<RouteGraphEdge> graph = CreateGraph(type.VehicleClass);
    terminalPlanner = null;
    if (project.Demand.Generation.EndpointSelection == "terminals")
    {
      terminalPlanner = new TerminalRoutePlanner(project, graph, vehicleClass);
      BuildForTerminals(type, graph);
      return;
    }
    List<int> starts = [];
    List<int> ends = [];
    for (int index = 0; index < graph.Count; index++)
    {
      starts.Add(index);
      ends.Add(index);
    }

    Shuffle(starts);
    Shuffle(ends);
    int count = 0;
    int shortestBudget = Math.Max(1, project.Demand.Generation.CandidateLimit / project.Demand.Routing.MaxAlternatives);
    // Round-robin starts avoids spending the entire budget on one road/component.
    RouteSearch[] searches = new RouteSearch[graph.Count];
    for (int index = 0; index < graph.Count; index++)
    {
      searches[index] = Search(graph, index, null);
    }

    for (int endOffset = 0; endOffset < ends.Count && count < shortestBudget; endOffset++)
    {
      foreach (int start in starts)
      {
        int end = ends[(endOffset + start) % ends.Count];
        if (start == end)
        {
          continue;
        }

        RouteCandidate? route = Reconstruct(graph, searches[start], start, end, type.Id);
        if (route is null)
        {
          continue;
        }

        candidates.Add(route);
        count++;
        if (count >= shortestBudget)
        {
          break;
        }
      }
    }

    AddAlternatives(type, graph, candidates.Count - count, count);
  }

  private void AddAlternatives(ProjectVehicleType type, List<RouteGraphEdge> graph, int first, int count)
  {
    // Add bounded alternatives by excluding each movement in a shortest path.
    int shortestCount = count;
    for (int index = first; index < first + shortestCount && count < project.Demand.Generation.CandidateLimit; index++)
    {
      RouteCandidate shortest = candidates[index];
      int alternatives = 1;
      HashSet<string> seen = new(StringComparer.Ordinal) { string.Join(" ", shortest.Edges) };
      for (int movement = 1; movement < shortest.Edges.Length && alternatives < project.Demand.Routing.MaxAlternatives; movement++)
      {
        int from = Find(graph, shortest.Edges[movement - 1]);
        int to = Find(graph, shortest.Edges[movement]);
        int start = Find(graph, shortest.Edges[0]);
        int end = Find(graph, shortest.Edges[^1]);
        RouteEndpoints? endpoints = routeEndpoints.GetValueOrDefault(shortest.Id);
        RouteSearch search = Search(graph, start, new BlockedMovement(from, to), endpoints);
        RouteCandidate? alternate = Reconstruct(graph, search, start, end, type.Id, endpoints);
        if (alternate is null)
        {
          continue;
        }

        bool withinDetour = alternate.TravelTimeSeconds <= shortest.TravelTimeSeconds * project.Demand.Routing.MaxDetourRatio;
        if (withinDetour && seen.Add(string.Join(" ", alternate.Edges)))
        {
          candidates.Add(alternate);
          if (endpoints is not null)
          {
            routeEndpoints.Add(alternate.Id, endpoints);
          }

          count++;
          alternatives++;
        }

        if (count >= project.Demand.Generation.CandidateLimit)
        {
          break;
        }
      }
    }
  }

  private void BuildForTerminals(ProjectVehicleType type, List<RouteGraphEdge> graph)
  {
    List<TerminalEndpoint> entries = ReadEndpoints(graph, "entry");
    List<TerminalEndpoint> exits = ReadEndpoints(graph, "exit");
    ShuffleEndpoints(entries);
    ShuffleEndpoints(exits);
    int first = candidates.Count;
    int count = 0;
    int budget = Math.Max(1, project.Demand.Generation.CandidateLimit / project.Demand.Routing.MaxAlternatives);
    HashSet<string> pairs = new(StringComparer.Ordinal);
    HashSet<string> usedExits = new(StringComparer.Ordinal);
    // Cover viable source and destination lanes before filling the remaining budget.
    foreach (TerminalEndpoint entry in entries)
    {
      foreach (TerminalEndpoint exit in exits)
      {
        if (count >= budget)
        {
          break;
        }

        if (TryTerminalPair(type, graph, entry, exit, pairs))
        {
          count++;
          usedExits.Add(exit.Terminal.Id);
          break;
        }
      }
    }

    foreach (TerminalEndpoint exit in exits)
    {
      if (usedExits.Contains(exit.Terminal.Id))
      {
        continue;
      }

      foreach (TerminalEndpoint entry in entries)
      {
        if (count >= budget)
        {
          break;
        }

        if (TryTerminalPair(type, graph, entry, exit, pairs))
        {
          count++;
          usedExits.Add(exit.Terminal.Id);
          break;
        }
      }
    }

    for (int offset = 0; offset < exits.Count && count < budget; offset++)
    {
      for (int entryIndex = 0; entryIndex < entries.Count && count < budget; entryIndex++)
      {
        TerminalEndpoint entry = entries[entryIndex];
        TerminalEndpoint exit = exits[(offset + entryIndex) % exits.Count];
        if (TryTerminalPair(type, graph, entry, exit, pairs))
        {
          count++;
        }
      }
    }

    AddAlternatives(type, graph, first, count);
  }

  private bool TryTerminalPair(ProjectVehicleType type, List<RouteGraphEdge> graph, TerminalEndpoint entry, TerminalEndpoint exit, HashSet<string> pairs)
  {
    string key = entry.Terminal.Id + "\0" + exit.Terminal.Id;
    if (!pairs.Add(key))
    {
      return false;
    }

    RouteEndpoints endpoints = new(entry, exit);
    RouteSearch search = Search(graph, entry.EdgeIndex, null, endpoints);
    RouteCandidate? route = Reconstruct(graph, search, entry.EdgeIndex, exit.EdgeIndex, type.Id, endpoints);
    if (route is null)
    {
      return false;
    }

    candidates.Add(route);
    routeEndpoints.Add(route.Id, endpoints);
    return true;
  }

  private List<TerminalEndpoint> ReadEndpoints(List<RouteGraphEdge> graph, string role)
  {
    List<TerminalEndpoint> endpoints = [];
    foreach (ProjectTerminal terminal in project.Terminals)
    {
      ProjectLane lane = lanes[terminal.LaneId];
      if (terminal.Role != role || !lane.Permits(vehicleClass))
      {
        continue;
      }

      int edgeIndex = Find(graph, lane.Map.EdgeId);
      endpoints.Add(new TerminalEndpoint(terminal, lane, edgeIndex));
    }

    endpoints.Sort((left, right) => StringComparer.Ordinal.Compare(left.Terminal.Id, right.Terminal.Id));
    return endpoints;
  }

  private void ShuffleEndpoints(List<TerminalEndpoint> endpoints)
  {
    for (int index = endpoints.Count - 1; index > 0; index--)
    {
      int other = random.Next(index + 1);
      TerminalEndpoint saved = endpoints[index];
      endpoints[index] = endpoints[other];
      endpoints[other] = saved;
    }
  }

  private List<RouteGraphEdge> CreateGraph(string vehicleClass)
  {
    List<RouteGraphEdge> graph = [];
    Dictionary<string, int> indices = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in project.Edges)
    {
      if (!edge.Ordinary)
      {
        continue;
      }

      double time = double.PositiveInfinity;
      double length = 0;
      foreach (ProjectLane lane in edge.Lanes)
      {
        if (lane.Permits(vehicleClass) && lane.Map.Length / lane.Map.Speed < time)
        {
          time = lane.Map.Length / lane.Map.Speed;
          length = lane.Map.Length;
        }
      }

      if (double.IsFinite(time))
      {
        indices.Add(edge.Id, graph.Count);
        graph.Add(new RouteGraphEdge(edge, length, time));
      }
    }

    graph.Sort((left, right) => StringComparer.Ordinal.Compare(left.Edge.Id, right.Edge.Id));
    indices.Clear();
    for (int index = 0; index < graph.Count; index++)
    {
      indices.Add(graph[index].Edge.Id, index);
    }

    foreach (ProjectConnection connection in project.Connections)
    {
      if (!indices.TryGetValue(connection.From, out int from) || !indices.TryGetValue(connection.To, out int to))
      {
        continue;
      }

      bool permitted = graph[from].Edge.Lanes[connection.FromLane].Permits(vehicleClass) && graph[to].Edge.Lanes[connection.ToLane].Permits(vehicleClass);
      if (connection.Via is not null)
      {
        permitted = permitted && lanes[connection.Via].Permits(vehicleClass);
      }

      if (permitted && !graph[from].Next.Contains(to))
      {
        graph[from].Next.Add(to);
      }
    }

    foreach (RouteGraphEdge edge in graph)
    {
      edge.Next.Sort();
    }

    return graph;
  }

  private RouteSearch Search(List<RouteGraphEdge> graph, int start, BlockedMovement? blocked, RouteEndpoints? endpoints = null)
  {
    double[] costs = new double[graph.Count];
    int[] previous = new int[graph.Count];
    if (endpoints is not null)
    {
      Array.Fill(costs, double.PositiveInfinity);
      Array.Fill(previous, -1);
      TerminalPath? path = terminalPlanner!.Search(endpoints, blocked);
      if (path is not null)
      {
        costs[endpoints.Exit.EdgeIndex] = path.Cost;
        return new RouteSearch(costs, previous, path.Edges);
      }

      return new RouteSearch(costs, previous);
    }
    Array.Fill(costs, double.PositiveInfinity);
    Array.Fill(previous, -1);
    costs[start] = graph[start].TravelTime;
    PriorityQueue<int, double> pending = new();
    pending.Enqueue(start, costs[start]);
    while (pending.TryDequeue(out int index, out double cost))
    {
      if (cost > costs[index])
      {
        continue;
      }

      foreach (int next in graph[index].Next)
      {
        if (blocked is not null)
        {
          if (blocked.From == index && blocked.To == next)
          {
            continue;
          }
        }

        double candidate = cost + graph[next].TravelTime;
        if (candidate < costs[next])
        {
          costs[next] = candidate;
          previous[next] = index;
          pending.Enqueue(next, candidate);
        }
      }
    }

    return new RouteSearch(costs, previous);
  }

  private RouteCandidate? Reconstruct(List<RouteGraphEdge> graph, RouteSearch search, int start, int end, string typeId, RouteEndpoints? endpoints = null)
  {
    if (!double.IsFinite(search.Costs[end]))
    {
      return null;
    }

    List<int> path = [];
    int current = end;
    if (search.TerminalPath is not null)
    {
      path.AddRange(search.TerminalPath);
      current = -1;
    }

    while (current != -1)
    {
      path.Add(current);
      if (current == start)
      {
        break;
      }

      current = search.Previous[current];
    }

    if (search.TerminalPath is null)
    {
      path.Reverse();
    }

    HashSet<int> uniqueEdges = new(path);
    if (uniqueEdges.Count != path.Count)
    {
      return null;
    }

    string[] edges = new string[path.Count];
    double[] lengths = new double[path.Count];
    double distance = 0;
    for (int index = 0; index < path.Count; index++)
    {
      RouteGraphEdge edge = graph[path[index]];
      edges[index] = edge.Edge.Id;
      lengths[index] = edge.Length;
      distance += edge.Length;
    }

    double travelTime = search.Costs[end];
    if (endpoints is not null)
    {
      ApplyTerminalDistances(path.Count, lengths, endpoints);
      distance = 0;
      foreach (double length in lengths)
      {
        distance += length;
      }
    }

    TrafficGenerationSettings settings = project.Demand.Generation;
    if (distance < settings.MinTripDistanceM || distance > settings.MaxTripDistanceM)
    {
      return null;
    }

    RouteCandidate candidate = new($"candidate-{candidates.Count}", typeId, edges, lengths, distance, travelTime);
    if (endpoints is not null)
    {
      candidate = candidate with
      {
        EntryTerminalId = endpoints.Entry.Terminal.Id,
        ExitTerminalId = endpoints.Exit.Terminal.Id,
        DepartureLaneIndex = endpoints.Entry.Lane.Index,
        DeparturePositionM = endpoints.Entry.Terminal.PositionM,
        ArrivalLaneIndex = endpoints.Exit.Lane.Index,
        ArrivalPositionM = endpoints.Exit.Terminal.PositionM
      };
    }

    return candidate;
  }

  private static void ApplyTerminalDistances(int edgeCount, double[] lengths, RouteEndpoints endpoints)
  {
    lengths[0] = endpoints.Entry.Lane.Map.Length - endpoints.Entry.Terminal.PositionM;
    lengths[^1] = endpoints.Exit.Terminal.PositionM;
    if (edgeCount == 1)
    {
      lengths[0] = endpoints.Exit.Terminal.PositionM - endpoints.Entry.Terminal.PositionM;
    }
  }

  private static int Find(List<RouteGraphEdge> graph, string id)
  {
    for (int index = 0; index < graph.Count; index++)
    {
      if (graph[index].Edge.Id == id)
      {
        return index;
      }
    }

    throw new InvalidOperationException("Candidate edge not in graph");
  }

  private void Shuffle(List<int> values)
  {
    for (int index = values.Count - 1; index > 0; index--)
    {
      int other = random.Next(index + 1);
      int saved = values[index];
      values[index] = values[other];
      values[other] = saved;
    }
  }
}

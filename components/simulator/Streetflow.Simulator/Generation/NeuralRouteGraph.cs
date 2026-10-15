using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

public sealed class NeuralRouteGraph
{
  private const double MinimumLaneChangeDistanceM = 20;
  private readonly string vehicleClass;
  private readonly Dictionary<string, ProjectEdge> edges = new(StringComparer.Ordinal);
  private readonly Dictionary<string, List<NeuralLaneMovement>> movements = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string[]> successors = new(StringComparer.Ordinal);
  public string[] EdgeIds { get; }

  public NeuralRouteGraph(ProjectEdge[] projectEdges, ProjectConnection[] connections, string vehicleClass)
  {
    this.vehicleClass = vehicleClass;
    foreach (ProjectEdge edge in projectEdges)
    {
      bool usable = false;
      foreach (ProjectLane lane in edge.Lanes)
      {
        usable = usable || lane.Permits(vehicleClass);
      }

      if (edge.Ordinary && usable)
      {
        edges.Add(edge.Id, edge);
      }
    }

    Dictionary<string, HashSet<string>> next = new(StringComparer.Ordinal);
    foreach (string edge in edges.Keys)
    {
      next.Add(edge, new HashSet<string>(StringComparer.Ordinal));
    }

    NeuralConnectionPermissions permissions = new(projectEdges, connections, vehicleClass);
    foreach (ProjectConnection connection in connections)
    {
      if (!edges.TryGetValue(connection.From, out ProjectEdge? from) || !edges.TryGetValue(connection.To, out ProjectEdge? to))
      {
        continue;
      }

      bool permitted = from.Lanes[connection.FromLane].Permits(vehicleClass) && to.Lanes[connection.ToLane].Permits(vehicleClass);
      if (!permitted || !permissions.Permits(connection))
      {
        continue;
      }

      string key = MovementKey(connection.From, connection.To);
      if (!movements.TryGetValue(key, out List<NeuralLaneMovement>? laneMovements))
      {
        laneMovements = [];
        movements.Add(key, laneMovements);
      }

      laneMovements.Add(new NeuralLaneMovement(connection.FromLane, connection.ToLane));
      next[connection.From].Add(connection.To);
    }

    List<string> ids = new(edges.Keys);
    ids.Sort(StringComparer.Ordinal);
    EdgeIds = ids.ToArray();
    foreach (string edge in EdgeIds)
    {
      List<string> following = new(next[edge]);
      following.Sort(StringComparer.Ordinal);
      successors.Add(edge, following.ToArray());
    }
  }

  public ProjectEdge Edge(string edge) => edges[edge];
  public string[] Next(string edge) => successors[edge];

  public HashSet<int> InitialLanes(string edge)
  {
    HashSet<int> result = [];
    foreach (ProjectLane lane in edges[edge].Lanes)
    {
      if (lane.Permits(vehicleClass))
      {
        result.Add(lane.Index);
      }
    }

    return result;
  }

  public HashSet<int> Advance(string from, string to, HashSet<int> possible)
  {
    HashSet<int> expanded = new(possible);
    Queue<int> pending = new(possible);
    ProjectLane[] lanes = edges[from].Lanes;
    while (pending.TryDequeue(out int index))
    {
      if (lanes[index].Map.Length < MinimumLaneChangeDistanceM)
      {
        continue;
      }

      for (int neighbor = Math.Max(0, index - 1); neighbor <= Math.Min(lanes.Length - 1, index + 1); neighbor++)
      {
        if (lanes[neighbor].Permits(vehicleClass) && expanded.Add(neighbor))
        {
          pending.Enqueue(neighbor);
        }
      }
    }

    HashSet<int> result = [];
    if (movements.TryGetValue(MovementKey(from, to), out List<NeuralLaneMovement>? options))
    {
      foreach (NeuralLaneMovement option in options)
      {
        if (expanded.Contains(option.FromLane))
        {
          result.Add(option.ToLane);
        }
      }
    }

    return result;
  }

  private static string MovementKey(string from, string to) => from + "\0" + to;
}

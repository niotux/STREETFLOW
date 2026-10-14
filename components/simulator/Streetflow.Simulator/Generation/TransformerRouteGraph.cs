using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

// Version 2 currently validates single-lane road states; it never guesses lane changes.
public sealed class TransformerRouteGraph
{
  private readonly TransformerAxes axes;
  private readonly Dictionary<string, int> edgeIndices = new(StringComparer.Ordinal);
  private readonly Dictionary<string, ProjectTerminal> terminals = new(StringComparer.Ordinal);
  private readonly Dictionary<int, bool[]> reachable = [];
  private readonly List<int>[] predecessors;
  public TransformerRoutingState[] States { get; }

  public TransformerRouteGraph(CityProject project, string vehicleClass)
  {
    axes = project.TransformerModel!.Axes;
    List<ProjectEdge> edges = [];
    Dictionary<string, ProjectLane> lanes = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in project.Edges)
    {
      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(lane.Map.Id, lane);
      }

      if (edge.Ordinary && edge.Lanes[0].Permits(vehicleClass))
      {
        edges.Add(edge);
      }
    }

    edges.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
    States = new TransformerRoutingState[edges.Count];
    predecessors = new List<int>[edges.Count];
    for (int index = 0; index < edges.Count; index++)
    {
      edgeIndices.Add(edges[index].Id, index);
      States[index] = new TransformerRoutingState(edges[index]);
      predecessors[index] = [];
    }

    foreach (ProjectTerminal terminal in project.Terminals)
    {
      terminals.Add(terminal.Id, terminal);
      string edgeId = lanes[terminal.LaneId].Map.EdgeId;
      if (terminal.Role == "exit" && edgeIndices.TryGetValue(edgeId, out int index))
      {
        States[index].Exit = terminal;
      }
    }

    Dictionary<string, int> movements = new(StringComparer.Ordinal);
    for (int index = 0; index < axes.Movement.Length; index++)
    {
      TransformerMovement movement = axes.Movement[index];
      movements.Add(MovementKey(movement.IncomingEdgeId, movement.OutgoingEdgeId), index);
    }

    HashSet<string> connected = new(StringComparer.Ordinal);
    foreach (ProjectConnection connection in project.Connections)
    {
      if (!edgeIndices.TryGetValue(connection.From, out int from) || !edgeIndices.TryGetValue(connection.To, out int to))
      {
        continue;
      }

      if (!TransformerConnectionPermissions.HasPermittedInternalPath(connection, lanes, project.Connections, vehicleClass))
      {
        continue;
      }

      string key = MovementKey(connection.From, connection.To);
      if (States[from].Exit is not null || !connected.Add(key))
      {
        continue;
      }

      States[from].Transitions.Add(new TransformerTransition(movements[key], to));
      predecessors[to].Add(from);
    }

    foreach (TransformerRoutingState state in States)
    {
      state.Transitions.Sort((left, right) => left.MovementIndex.CompareTo(right.MovementIndex));
    }
  }

  private static string MovementKey(string from, string to) => from + "\0" + to;

  public int OriginState(int origin)
  {
    ProjectTerminal terminal = terminals[axes.Origin[origin]];
    foreach (TransformerRoutingState state in States)
    {
      if (state.Lane.Map.Id == terminal.LaneId)
      {
        return edgeIndices[state.Edge.Id];
      }
    }

    return -1;
  }

  public ProjectTerminal OriginTerminal(int origin) => terminals[axes.Origin[origin]];

  public bool IsDestination(int state, int destination)
  {
    ProjectTerminal? exit = States[state].Exit;
    if (exit is null)
    {
      return false;
    }

    foreach (string id in axes.Destination[destination].TerminalIds)
    {
      if (exit.Id == id)
      {
        return true;
      }
    }

    return false;
  }

  public bool CanReach(int origin, int destination)
  {
    int start = OriginState(origin);
    return start >= 0 && Reachable(destination)[start];
  }

  public bool[] Reachable(int destination)
  {
    if (reachable.TryGetValue(destination, out bool[]? known))
    {
      return known;
    }

    bool[] result = new bool[States.Length];
    Queue<int> pending = new();
    for (int index = 0; index < States.Length; index++)
    {
      if (IsDestination(index, destination))
      {
        result[index] = true;
        pending.Enqueue(index);
      }
    }

    while (pending.TryDequeue(out int state))
    {
      foreach (int previous in predecessors[state])
      {
        if (!result[previous])
        {
          result[previous] = true;
          pending.Enqueue(previous);
        }
      }
    }

    reachable.Add(destination, result);
    return result;
  }
}

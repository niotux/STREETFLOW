namespace Streetflow.Simulator.Scenarios;

public static partial class CityProjectLoader
{
  private static void ValidateTransformerNetwork(TransformerModel model, TransformerWeights weights, ProjectEdge[] edges, ProjectConnection[] connections, ProjectTerminal[] terminals, string file)
  {
    Dictionary<string, ProjectEdge> ordinary = new(StringComparer.Ordinal);
    Dictionary<string, ProjectLane> lanes = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in edges)
    {
      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(lane.Map.Id, lane);
      }

      if (!edge.Ordinary)
      {
        continue;
      }

      ordinary.Add(edge.Id, edge);
      foreach (TransformerVehicleType type in model.Axes.VehicleType)
      {
        if (Permits(edge, type.VehicleClass))
        {
          Require(edge.Lanes.Length == 1, $"{file}: Transformer Routes currently requires one lane per usable ordinary road; edge '{edge.Id}' has {edge.Lanes.Length}");
          break;
        }
      }
    }

    Dictionary<string, ProjectTerminal> entries = new(StringComparer.Ordinal);
    Dictionary<string, ProjectTerminal> exits = new(StringComparer.Ordinal);
    Dictionary<string, ProjectTerminal> exitsByEdge = new(StringComparer.Ordinal);
    foreach (ProjectTerminal terminal in terminals)
    {
      if (terminal.Role == "entry")
      {
        entries.Add(terminal.Id, terminal);
      }
      else
      {
        exits.Add(terminal.Id, terminal);
        string edgeId = lanes[terminal.LaneId].Map.EdgeId;
        Require(exitsByEdge.TryAdd(edgeId, terminal), $"{file}: multiple exit terminals on edge '{edgeId}' are unsupported");
      }
    }

    Require(entries.Count > 0 && exits.Count > 0, $"{file}: Transformer Routes requires both entry and exit terminals");
    Require(model.Axes.Origin.Length == entries.Count, $"{file}: origin axis must cover every entry terminal exactly once");
    foreach (string origin in model.Axes.Origin)
    {
      Require(entries.TryGetValue(origin, out ProjectTerminal? entry), $"{file}: origin '{origin}' is not an entry terminal");
      string edgeId = lanes[entry!.LaneId].Map.EdgeId;
      if (exitsByEdge.TryGetValue(edgeId, out ProjectTerminal? exit))
      {
        Require(entry.PositionM < exit.PositionM, $"{file}: origin '{origin}' must precede same-lane exit '{exit.Id}'");
      }
    }

    HashSet<string> groupedExits = new(StringComparer.Ordinal);
    foreach (TransformerDestination destination in model.Axes.Destination)
    {
      foreach (string terminalId in destination.TerminalIds)
      {
        Require(exits.ContainsKey(terminalId), $"{file}: destination '{destination.Id}' references unknown exit terminal '{terminalId}'");
        Require(groupedExits.Add(terminalId), $"{file}: exit terminal '{terminalId}' belongs to multiple destination groups");
      }
    }

    Require(groupedExits.Count == exits.Count, $"{file}: destination axis must cover every exit terminal exactly once");
    ValidateTransformerMovements(model, ordinary, lanes, connections, file);
    ValidateTransformerOriginFeasibility(model, weights, ordinary, lanes, connections, entries, exitsByEdge, file);
  }

  private static void ValidateTransformerMovements(TransformerModel model, Dictionary<string, ProjectEdge> ordinary, Dictionary<string, ProjectLane> lanes, ProjectConnection[] connections, string file)
  {
    HashSet<string> legal = new(StringComparer.Ordinal);
    foreach (ProjectConnection connection in connections)
    {
      if (!ordinary.TryGetValue(connection.From, out ProjectEdge? incoming) || !ordinary.TryGetValue(connection.To, out ProjectEdge? outgoing))
      {
        continue;
      }

      foreach (TransformerVehicleType type in model.Axes.VehicleType)
      {
        if (TransformerConnectionPermits(connection, incoming, outgoing, lanes, connections, type.VehicleClass))
        {
          legal.Add(MovementKey(connection.From, connection.To));
          break;
        }
      }
    }

    foreach (TransformerMovement movement in model.Axes.Movement)
    {
      Require(legal.Remove(MovementKey(movement.IncomingEdgeId, movement.OutgoingEdgeId)), $"{file}: movement '{movement.IncomingEdgeId}' -> '{movement.OutgoingEdgeId}' is not a usable legal ordinary-road movement");
    }

    string? missing = null;
    foreach (string movement in legal)
    {
      if (missing is null || StringComparer.Ordinal.Compare(movement, missing) < 0)
      {
        missing = movement;
      }
    }

    Require(missing is null, $"{file}: movement axis is missing legal road movement '{missing?.Replace("\0", " -> ")}'");
  }

  private static void ValidateTransformerOriginFeasibility(TransformerModel model, TransformerWeights weights, Dictionary<string, ProjectEdge> ordinary, Dictionary<string, ProjectLane> lanes, ProjectConnection[] connections, Dictionary<string, ProjectTerminal> entries, Dictionary<string, ProjectTerminal> exitsByEdge, string file)
  {
    for (int originIndex = 0; originIndex < model.Axes.Origin.Length; originIndex++)
    {
      string originId = model.Axes.Origin[originIndex];
      bool positive = false;
      for (int period = 0; period < model.Axes.Period.Length; period++)
      {
        if (weights.Tensors.ArrivalRates.Values[period * model.Axes.Origin.Length + originIndex] > 0)
        {
          positive = true;
          break;
        }
      }

      if (!positive)
      {
        continue;
      }

      ProjectTerminal origin = entries[originId];
      string originEdge = lanes[origin.LaneId].Map.EdgeId;
      bool feasible = false;
      foreach (TransformerVehicleType type in model.Axes.VehicleType)
      {
        if (TransformerOriginReachesExit(originEdge, type.VehicleClass, ordinary, lanes, connections, exitsByEdge))
        {
          feasible = true;
          break;
        }
      }

      Require(feasible, $"{file}: positive-rate origin '{originId}' has no feasible destination and vehicle type");
    }
  }

  private static bool TransformerOriginReachesExit(string originEdge, string vehicleClass, Dictionary<string, ProjectEdge> ordinary, Dictionary<string, ProjectLane> lanes, ProjectConnection[] connections, Dictionary<string, ProjectTerminal> exitsByEdge)
  {
    if (!Permits(ordinary[originEdge], vehicleClass))
    {
      return false;
    }

    Queue<string> pending = new();
    HashSet<string> visited = new(StringComparer.Ordinal);
    pending.Enqueue(originEdge);
    visited.Add(originEdge);
    while (pending.Count > 0)
    {
      string current = pending.Dequeue();
      if (exitsByEdge.ContainsKey(current))
      {
        return true;
      }

      foreach (ProjectConnection connection in connections)
      {
        if (connection.From != current || visited.Contains(connection.To) || !ordinary.TryGetValue(connection.To, out ProjectEdge? next))
        {
          continue;
        }

        if (TransformerConnectionPermits(connection, ordinary[current], next, lanes, connections, vehicleClass))
        {
          visited.Add(next.Id);
          pending.Enqueue(next.Id);
        }
      }
    }

    return false;
  }

  private static bool TransformerConnectionPermits(ProjectConnection connection, ProjectEdge incoming, ProjectEdge outgoing, Dictionary<string, ProjectLane> lanes, ProjectConnection[] connections, string vehicleClass)
  {
    bool ordinaryLanesPermit = incoming.Lanes[connection.FromLane].Permits(vehicleClass) && outgoing.Lanes[connection.ToLane].Permits(vehicleClass);
    if (!ordinaryLanesPermit)
    {
      return false;
    }

    return TransformerConnectionPermissions.HasPermittedInternalPath(connection, lanes, connections, vehicleClass);
  }
}

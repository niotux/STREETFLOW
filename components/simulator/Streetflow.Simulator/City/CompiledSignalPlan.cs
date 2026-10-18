using System.Globalization;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.City;

// Ahead-of-time preparation of immutable routing, storage and phase data. The
// step loop uses lane indices and these arrays, never XML or graph searches.
public sealed class CompiledSignalPlan
{
  public const double VehicleSpaceMeters = 7.5;
  public const double LongRoadMeters = 100;
  public const int BufferCapacityVehicles = 20;
  public required LaneMap[] Lanes { get; init; }
  public required Dictionary<string, int> LaneIndices { get; init; }
  public required Controller[] Controllers { get; init; }

  public sealed record Phase(double Seconds, string State, int Group, double MinimumSeconds, bool Weighted);
  public sealed record Exit(int[] Lanes, double Capacity);
  public sealed record Movement(int Link, int FromLane, Exit[] Exits, bool QueueBuffer, int CapacityVehicles);
  public sealed record Controller(string Id, Phase[] Phases, Movement[] Movements, int[][] GroupLanes, string[] GroupLabels, int[][] GroupMovements, double Offset);

  public static CompiledSignalPlan? Compile(CityProject project, IReadOnlyDictionary<string, string> selectedPrograms)
  {
    LaneMap[] lanes = project.Lanes;
    Dictionary<string, int> indices = lanes.Select((lane, index) => (lane.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
    HashSet<int> vehicleLanes = project.Edges.Where(e => e.Ordinary).SelectMany(e => e.Lanes)
      .Where(lane => lane.Permits("passenger")).Select(lane => indices[lane.Map.Id]).ToHashSet();
    Dictionary<int, int[]> roadLanes = vehicleLanes.GroupBy(i => lanes[i].EdgeId).SelectMany(g => g.Select(i => (i, all: g.ToArray())))
      .ToDictionary(x => x.i, x => x.all);
    var upstream = new QueuePaths(project).Upstream;
    Dictionary<int, HashSet<int>> successors = [];
    HashSet<int> controlled = [];
    List<(ProjectConnection Connection, int From, int To)> connections = [];
    foreach (ProjectConnection connection in project.Connections)
    {
      if (!indices.TryGetValue($"{connection.From}_{connection.FromLane}", out int from)
        || !indices.TryGetValue($"{connection.To}_{connection.ToLane}", out int to)) continue;
      connections.Add((connection, from, to));
      if (vehicleLanes.Contains(from) && vehicleLanes.Contains(to))
      {
        if (!successors.TryGetValue(from, out HashSet<int>? next)) successors[from] = next = [];
        next.Add(to);
        if (connection.SignalId is not null) controlled.Add(from);
      }
    }

    int[] Approach(int start)
    {
      List<int> result = [];
      HashSet<int> seen = [];
      int current = start;
      double length = 0;
      while (seen.Add(current) && length < 500)
      {
        if (vehicleLanes.Contains(current)) { result.Add(current); length += lanes[current].Length; }
        if (!upstream.TryGetValue(lanes[current].Id, out string? feeder) || !indices.TryGetValue(feeder, out current) || controlled.Contains(current)) break;
      }
      return result.ToArray();
    }
    Exit Receiving(int start)
    {
      List<int> result = [];
      HashSet<int> seen = [];
      int current = start;
      double length = 0;
      while (seen.Add(current) && vehicleLanes.Contains(current) && length < 500)
      {
        result.Add(current); length += lanes[current].Length;
        if (controlled.Contains(current) || !successors.TryGetValue(current, out HashSet<int>? next) || next.Count != 1) break;
        current = next.First();
      }
      return new Exit(result.ToArray(), Math.Max(1, length / VehicleSpaceMeters));
    }
    Dictionary<int, int[]> approaches = vehicleLanes.ToDictionary(i => i, Approach);
    Dictionary<(string, string), XElement> programs = [];
    foreach (string relative in new[] { project.Manifest.Network.File }.Concat(project.Manifest.AdditionalFiles))
    {
      XElement root = XDocument.Load(ProjectPaths.Resolve(project.Directory, relative)).Root!;
      foreach (XElement logic in root.Elements("tlLogic"))
        programs[((string)logic.Attribute("id")!, (string)logic.Attribute("programID")!)] = logic;
    }

    List<Controller> controllers = [];
    foreach (string id in project.SignalIds)
    {
      if (!selectedPrograms.TryGetValue(id, out string? program) || !programs.TryGetValue((id, program), out XElement? logic)
        || (string?)logic.Attribute("type") != "static") return null;
      List<Movement> movements = [];
      var links = connections.Where(c => c.Connection.SignalId == id && c.Connection.LinkIndex is not null && vehicleLanes.Contains(c.From) && vehicleLanes.Contains(c.To))
        .GroupBy(c => c.Connection.LinkIndex!.Value);
      foreach (var link in links)
      {
        int from = link.First().From;
        int[] storage = roadLanes[from].SelectMany(i => approaches[i]).Distinct().ToArray();
        int capacity = (int)Math.Floor(storage.Sum(i => lanes[i].Length) / VehicleSpaceMeters);
        double longest = roadLanes[from].Max(i => approaches[i].Sum(j => lanes[j].Length));
        movements.Add(new Movement(link.Key, from, link.Select(c => c.To).Distinct().Select(Receiving).ToArray(),
          longest >= LongRoadMeters && capacity >= BufferCapacityVehicles, capacity));
      }
      List<int[]> groups = [];
      List<Phase> phases = [];
      foreach (XElement phase in logic.Elements("phase"))
      {
        if (phase.Attribute("next") is not null) return null;
        string state = (string)phase.Attribute("state")!;
        if (!double.TryParse((string?)phase.Attribute("duration"), CultureInfo.InvariantCulture, out double duration)
          || !double.IsFinite(duration) || duration <= 0 || movements.Any(m => m.Link >= state.Length)) return null;
        int[] served = movements.Where(m => Green(state[m.Link])).Select(m => m.FromLane).Distinct().Order().ToArray();
        int group = -1;
        if (served.Length > 0)
        {
          group = groups.FindIndex(g => g.SequenceEqual(served));
          if (group < 0) { group = groups.Count; groups.Add(served); }
        }
        // Preserve short pedestrian clearance stages and every yellow/all-red
        // stage. Green walk time cannot fall below the modeled crossing length.
        double walkLength = connections.Where(c => c.Connection.SignalId == id && c.Connection.LinkIndex is int index
          && index < state.Length && Green(state[index]) && lanes[c.To].RenderKind == "crossing").Select(c => lanes[c.To].Length).DefaultIfEmpty(0).Max();
        double minimum = Math.Min(duration, Math.Max(10, walkLength > 0 ? walkLength + 2 : 0));
        bool weighted = group >= 0 && duration >= 10 && !state.Any(c => c is 'y' or 'Y' or 'u');
        phases.Add(new Phase(duration, state, group, minimum, weighted));
      }
      if (phases.Count == 0) return null;
      int[][] groupApproaches = groups.Select(g => g.SelectMany(i => approaches[i]).Distinct().ToArray()).ToArray();
      string[] labels = groups.Select(g => string.Join(" / ", (project.TrafficLights?.Devices ?? [])
        .Where(d => d.SignalId == id && d.LaneIds.Any(lane => g.Contains(indices[lane]))).Select(d => d.Label).Distinct())).ToArray();
      double offset = double.TryParse((string?)logic.Attribute("offset"), CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) ? parsed : 0;
      int[][] groupMovements = groups.Select((_, group) => Enumerable.Range(0, movements.Count)
        .Where(i => phases.Any(phase => phase.Group == group && Green(phase.State[movements[i].Link]))).ToArray()).ToArray();
      controllers.Add(new Controller(id, phases.ToArray(), movements.ToArray(), groupApproaches, labels, groupMovements, offset));
    }
    return controllers.Count == 0 ? null : new CompiledSignalPlan { Lanes = lanes, LaneIndices = indices, Controllers = controllers.ToArray() };
  }

  public static bool Green(char state) => state is 'g' or 'G' or 's';
}

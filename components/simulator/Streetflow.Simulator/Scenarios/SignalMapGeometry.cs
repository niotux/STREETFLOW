using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public static class SignalMapGeometry
{
  public static SignalMapPoint[] Read(CityProject project)
  {
    Dictionary<string, ProjectEdge> edges = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in project.Edges)
    {
      edges.Add(edge.Id, edge);
    }

    List<SignalMapPoint> points = [];
    foreach (ProjectConnection connection in project.Connections)
    {
      if (connection.SignalId is null || connection.LinkIndex is null)
      {
        continue;
      }

      LaneMap lane = edges[connection.From].Lanes[connection.FromLane].Map;
      LaneMap destination = edges[connection.To].Lanes[connection.ToLane].Map;
      Point end = lane.Shape[lane.Shape.Count - 1];
      points.Add(new SignalMapPoint(connection.SignalId, connection.LinkIndex.Value, lane.Id, destination.Id, connection.Direction, end.X, end.Y));
    }

    return points.ToArray();
  }
}

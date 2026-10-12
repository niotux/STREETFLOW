using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public static class TerminalMapGeometry
{
  public static TerminalMapPoint[] Read(CityProject project)
  {
    Dictionary<string, LaneMap> lanes = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in project.Edges)
    {
      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(lane.Map.Id, lane.Map);
      }
    }

    TerminalMapPoint[] points = new TerminalMapPoint[project.Terminals.Length];
    for (int index = 0; index < project.Terminals.Length; index++)
    {
      ProjectTerminal terminal = project.Terminals[index];
      LaneMap lane = lanes[terminal.LaneId];
      Point position = Position(lane, terminal.PositionM);
      points[index] = new TerminalMapPoint(terminal.Id, terminal.LaneId, lane.EdgeId, terminal.Role, terminal.PositionM, position.X, position.Y, terminal.Label);
    }

    return points;
  }

  private static Point Position(LaneMap lane, double positionM)
  {
    IReadOnlyList<Point> shape = lane.Shape;
    double shapeLength = 0;
    for (int index = 1; index < shape.Count; index++)
    {
      shapeLength += Distance(shape[index - 1], shape[index]);
    }

    double remaining = shapeLength * positionM / lane.Length;
    for (int index = 1; index < shape.Count; index++)
    {
      Point start = shape[index - 1];
      Point end = shape[index];
      double segmentLength = Distance(start, end);
      if (segmentLength > 0 && remaining <= segmentLength)
      {
        double fraction = remaining / segmentLength;
        return new Point(start.X + fraction * (end.X - start.X), start.Y + fraction * (end.Y - start.Y));
      }

      remaining -= segmentLength;
    }

    return shape[shape.Count - 1];
  }

  private static double Distance(Point start, Point end)
  {
    double x = end.X - start.X;
    double y = end.Y - start.Y;
    return Math.Sqrt(x * x + y * y);
  }
}

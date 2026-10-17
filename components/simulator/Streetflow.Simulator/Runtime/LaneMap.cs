namespace Streetflow.Simulator.Runtime;

public sealed record LaneMap(string Id, string EdgeId, double Length, double Speed, IReadOnlyList<Point> Shape)
{
  public string? RenderKind { get; init; }
}

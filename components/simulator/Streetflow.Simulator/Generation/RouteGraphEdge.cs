using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

internal sealed class RouteGraphEdge(ProjectEdge edge, double length, double travelTime)
{
  public ProjectEdge Edge { get; } = edge;
  public double Length { get; } = length;
  public double TravelTime { get; } = travelTime;
  public List<int> Next { get; } = [];
}

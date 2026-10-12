using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

internal sealed class TerminalLane(int edgeIndex, ProjectLane lane)
{
  public int EdgeIndex { get; } = edgeIndex;
  public ProjectLane Lane { get; } = lane;
  public List<int> Next { get; } = [];
}

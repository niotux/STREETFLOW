using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

public sealed class TransformerRoutingState(ProjectEdge edge)
{
  public ProjectEdge Edge { get; } = edge;
  public ProjectLane Lane => Edge.Lanes[0];
  public ProjectTerminal? Exit { get; set; }
  public List<TransformerTransition> Transitions { get; } = [];
}

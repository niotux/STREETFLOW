using Streetflow.Simulator.Runtime;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public sealed record ProjectMapResponse(string ProjectId, string Revision, string? Attribution, string CoordinateSystem, int TotalLanes, int TotalJunctions, int TotalEdges, int Offset, LaneMap[] Lanes, JunctionMap[] Junctions, ProjectMapEdge[] Edges, string[] SignalIds, TerminalMapPoint[] Terminals, SignalMapPoint[] SignalPoints)
{
  public TacoDevice[] TrafficDevices { get; init; } = [];
}

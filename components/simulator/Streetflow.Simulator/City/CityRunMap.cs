using Streetflow.Simulator.Runtime;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

public sealed record CityRunMap(string ProjectId, string Revision, string? Attribution, LaneMap[] Lanes, JunctionMap[] Junctions, TerminalMapPoint[] Terminals, SignalMapPoint[] SignalPoints)
{
  public TacoDevice[] TrafficDevices { get; init; } = [];
}

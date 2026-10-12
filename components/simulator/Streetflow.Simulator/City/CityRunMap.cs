using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.City;

public sealed record CityRunMap(string ProjectId, string Revision, string? Attribution, LaneMap[] Lanes, JunctionMap[] Junctions, TerminalMapPoint[] Terminals);

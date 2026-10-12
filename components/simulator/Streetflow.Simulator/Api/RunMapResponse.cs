using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Api;

public sealed record RunMapResponse(string NetworkRevision, string Attribution, string CoordinateSystem, int TotalLanes, int Offset, LaneMap[] Lanes, int TotalJunctions, JunctionMap[] Junctions, Camera[] Cameras, CheckpointConfiguration Checkpoints);

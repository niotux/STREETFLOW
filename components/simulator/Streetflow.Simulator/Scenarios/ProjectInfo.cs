namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectInfo(ProjectManifest Manifest, int EdgeCount, int UsableEdgeCount, int LaneCount, int JunctionCount, int SignalCount, string Revision);

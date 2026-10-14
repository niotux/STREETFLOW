namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectInfo(ProjectManifest Manifest, int EdgeCount, int UsableEdgeCount, int LaneCount, int JunctionCount, int SignalCount, string Revision)
{
  public string ModelKind => Manifest.FormatVersion == 2 ? "Transformer Routes" : "Edge Population";
}

namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectInfo(ProjectManifest Manifest, int EdgeCount, int UsableEdgeCount, int LaneCount, int JunctionCount, int SignalCount, string Revision)
{
  public string? Path { get; init; }
  public string ModelKind => Manifest.FormatVersion switch { 3 => "Neural Routes", 2 => "Transformer Routes", _ => "Edge Population" };
}

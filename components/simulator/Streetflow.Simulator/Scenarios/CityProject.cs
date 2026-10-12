using System.Text.Json.Serialization;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public sealed class CityProject
{
  [JsonIgnore]
  public required string Directory { get; init; }
  public required ProjectManifest Manifest { get; init; }
  public required ProjectDemand Demand { get; init; }
  public required ProjectEdge[] Edges { get; init; }
  public required JunctionMap[] Junctions { get; init; }
  public required ProjectConnection[] Connections { get; init; }
  public required string[] SignalIds { get; init; }
  public required string Revision { get; init; }
  public ProjectTerminal[] Terminals { get; init; } = [];

  public TerminalMapPoint[] TerminalPoints => TerminalMapGeometry.Read(this);

  public LaneMap[] Lanes
  {
    get
    {
      List<LaneMap> lanes = [];
      foreach (ProjectEdge edge in Edges)
      {
        foreach (ProjectLane lane in edge.Lanes)
        {
          lanes.Add(lane.Map);
        }
      }

      return lanes.ToArray();
    }
  }

  public ProjectInfo Info
  {
    get
    {
      int edgeCount = 0;
      int usableEdgeCount = 0;
      int laneCount = 0;
      foreach (ProjectEdge edge in Edges)
      {
        laneCount += edge.Lanes.Length;
        if (!edge.Ordinary)
        {
          continue;
        }

        edgeCount++;
        if (IsUsable(edge))
        {
          usableEdgeCount++;
        }
      }

      return new ProjectInfo(Manifest, edgeCount, usableEdgeCount, laneCount, Junctions.Length, SignalIds.Length, Revision);
    }
  }

  private bool IsUsable(ProjectEdge edge)
  {
    foreach (ProjectLane lane in edge.Lanes)
    {
      foreach (ProjectVehicleType type in Demand.VehicleTypes)
      {
        if (lane.Permits(type.VehicleClass))
        {
          return true;
        }
      }
    }

    return false;
  }
}

namespace Streetflow.Simulator.Scenarios;

public static partial class CityProjectLoader
{
  private static void ValidateDetectors(ProjectDetector[] detectors, ProjectEdge[] edges, string? file)
  {
    Require(detectors is not null, $"{file}: detectors cannot be null");
    HashSet<string> laneIds = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in edges)
    {
      if (!edge.Ordinary)
      {
        continue;
      }
      foreach (ProjectLane lane in edge.Lanes)
      {
        laneIds.Add(lane.Map.Id);
      }
    }

    HashSet<string> ids = new(StringComparer.Ordinal);
    HashSet<string> assigned = new(StringComparer.Ordinal);
    foreach (ProjectDetector detector in detectors)
    {
      Require(detector is not null && ValidId(detector.Id) && ids.Add(detector.Id), $"{file}: invalid or duplicate detector id");
      Require(!string.IsNullOrWhiteSpace(detector.Label) && double.IsFinite(detector.X) && double.IsFinite(detector.Y), $"{file}: invalid detector label or position");
      Require(detector.LaneIds is not null && detector.LaneIds.Length > 0, $"{file}: detector needs lanes");
      foreach (string laneId in detector.LaneIds)
      {
        Require(laneIds.Contains(laneId) && assigned.Add(laneId), $"{file}: unknown or overlapping detector lane '{laneId}'");
      }
    }
  }

  private static void ValidateDetectorDevices(ProjectDetector[] detectors, ProjectTrafficLights? lights, string? file)
  {
    foreach (ProjectDetector detector in detectors)
    {
      if (detector.DeviceId is null)
      {
        continue;
      }
      ProjectTrafficDevice? device = null;
      if (lights is not null)
      {
        foreach (ProjectTrafficDevice candidate in lights.Devices)
        {
          if (candidate.Id == detector.DeviceId)
          {
            device = candidate;
            break;
          }
        }
      }
      Require(device is not null, $"{file}: detector '{detector.Id}' references an unknown device");
      foreach (string laneId in detector.LaneIds)
      {
        Require(Array.IndexOf(device!.LaneIds, laneId) >= 0, $"{file}: detector lane is outside its device");
      }
    }
  }
}

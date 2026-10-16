using System.Text.Json;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Casper;

public sealed record LaneMapping(string LaneId, string StreetId, string IntersectionId, string ApproachId);
public sealed record CameraMapping(string Id, string IntersectionId, string TokenEnvironment, string[] LaneIds);
public sealed record CasperConfiguration(string WorldId, string Revision, string DetectorConfigRevision,
  TacoLane[] Lanes, TacoDevice[] Devices, LaneMapping[] LaneMappings, CameraMapping[] Cameras,
  double StaleAfterSeconds = 10, double MaximumWindowSeconds = 30)
{
  public static readonly JsonSerializerOptions Json = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
  };

  public static CasperConfiguration Load(string path)
  {
    CasperConfiguration config = JsonSerializer.Deserialize<CasperConfiguration>(File.ReadAllText(path), Json)
      ?? throw new InvalidOperationException("Empty Casper configuration");
    config.Validate();
    return config;
  }

  public void Validate()
  {
    static void Require(bool valid, string message)
    {
      if (!valid) throw new InvalidOperationException(message);
    }
    Require(!string.IsNullOrWhiteSpace(WorldId) && !string.IsNullOrWhiteSpace(Revision)
      && !string.IsNullOrWhiteSpace(DetectorConfigRevision), "World and configuration revisions are required");
    Require(double.IsFinite(StaleAfterSeconds) && StaleAfterSeconds > 0
      && double.IsFinite(MaximumWindowSeconds) && MaximumWindowSeconds > 0, "Invalid timing limits");
    Require(Lanes.Length > 0 && Cameras.Length > 0, "Configure lanes and cameras");
    Require(Lanes.Select(x => x.Id).Distinct().Count() == Lanes.Length, "Duplicate lane IDs");
    Require(Devices.Select(x => x.Id).Distinct().Count() == Devices.Length, "Duplicate device IDs");
    Require(Cameras.Select(x => x.Id).Distinct().Count() == Cameras.Length, "Duplicate camera IDs");
    Require(LaneMappings.Select(x => x.LaneId).Distinct().Count() == LaneMappings.Length
      && LaneMappings.Length == Lanes.Length, "Each lane requires one mapping");
    HashSet<string> laneIds = Lanes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
    foreach (TacoLane lane in Lanes)
      Require(!string.IsNullOrWhiteSpace(lane.Id) && lane.Shape.Length >= 2
        && lane.Shape.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)), "Invalid map lane");
    foreach (LaneMapping lane in LaneMappings)
      Require(laneIds.Contains(lane.LaneId) && !string.IsNullOrWhiteSpace(lane.StreetId)
        && !string.IsNullOrWhiteSpace(lane.IntersectionId) && !string.IsNullOrWhiteSpace(lane.ApproachId), "Invalid lane mapping");
    HashSet<string> assigned = new(StringComparer.Ordinal);
    foreach (CameraMapping camera in Cameras)
    {
      Require(!string.IsNullOrWhiteSpace(camera.Id) && camera.LaneIds.Length > 0, "Invalid camera");
      Require(!string.IsNullOrWhiteSpace(camera.TokenEnvironment)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(camera.TokenEnvironment)), "Missing camera token environment variable");
      foreach (string lane in camera.LaneIds)
        Require(laneIds.Contains(lane) && assigned.Add(lane)
          && LaneMappings.Single(x => x.LaneId == lane).IntersectionId == camera.IntersectionId,
          "Each lane needs one authoritative camera in its intersection; overlapping cameras must not be summed");
    }
    Require(assigned.SetEquals(laneIds), "Every lane requires an authoritative camera");
    foreach (TacoDevice device in Devices)
      Require(!string.IsNullOrWhiteSpace(device.Id) && double.IsFinite(device.X) && double.IsFinite(device.Y)
        && device.LaneIds.Length > 0 && device.LaneIds.All(laneIds.Contains), "Invalid stoplight mapping");
  }
}

namespace Streetflow.Casper;

public sealed class CameraViewCalibration
{
  public string DetectorId { get; set; } = "";
  public string IntersectionId { get; set; } = "";
  public string NetworkRevision { get; set; } = "";
  public string DetectorConfigRevision { get; set; } = "";
  public string TokenEnvironment { get; set; } = "";
  public double WindowSeconds { get; set; }
  public double QueueDurationSeconds { get; set; }
  public double StationaryRadius { get; set; }
  public double MaximumTrackingGapSeconds { get; set; }
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public double? TrackingIouThreshold { get; set; }
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public int? MaximumDetections { get; set; }
  public LaneViewCalibration[] Lanes { get; set; } = [];
}

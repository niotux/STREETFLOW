namespace Streetflow.Casper;

public sealed record CameraMapping(string Id, string IntersectionId, string TokenEnvironment, string[] LaneIds)
{
  public string? ViewCalibrationFile { get; init; }
}

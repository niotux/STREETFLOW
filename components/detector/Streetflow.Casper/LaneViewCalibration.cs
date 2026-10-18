namespace Streetflow.Casper;

public sealed class LaneViewCalibration
{
  public string LaneId { get; set; } = "";
  public string ApproachId { get; set; } = "";
  public double[][] Polygon { get; set; } = [];
  public double[][]? CountingLine { get; set; }
}

using Microsoft.AspNetCore.Mvc;

namespace Streetflow.Simulator.Api;

public sealed class SnapshotQuery
{
  public int? Limit { get; set; }

  [FromQuery(Name = "min_x")]
  public double? MinX { get; set; }

  [FromQuery(Name = "min_y")]
  public double? MinY { get; set; }

  [FromQuery(Name = "max_x")]
  public double? MaxX { get; set; }

  [FromQuery(Name = "max_y")]
  public double? MaxY { get; set; }
  internal Viewport Viewport => new(MinX, MinY, MaxX, MaxY);
}

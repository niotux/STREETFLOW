using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

internal sealed record Viewport(double? MinX, double? MinY, double? MaxX, double? MaxY)
{
  public bool Contains(double x, double y)
  {
    return !MinX.HasValue || (x >= MinX && y >= MinY && x <= MaxX && y <= MaxY);
  }

  public void Validate()
  {
    if (MinX is null && MinY is null && MaxX is null && MaxY is null)
    {
      return;
    }

    if (MinX is null || MinY is null || MaxX is null || MaxY is null)
    {
      throw InvalidViewport();
    }

    bool finite = double.IsFinite(MinX.Value) && double.IsFinite(MinY.Value) && double.IsFinite(MaxX.Value) && double.IsFinite(MaxY.Value);
    bool ordered = MinX <= MaxX && MinY <= MaxY;
    if (!finite || !ordered)
    {
      throw InvalidViewport();
    }
  }

  private static ApiException InvalidViewport() => new(400, "invalid_viewport", "Supply finite minX, minY, maxX and maxY in order");
}

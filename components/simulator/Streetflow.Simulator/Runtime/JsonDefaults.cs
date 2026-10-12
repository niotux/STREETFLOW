using System.Text.Json;

namespace Streetflow.Simulator.Runtime;

public static class JsonDefaults
{
  public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true
  };
}

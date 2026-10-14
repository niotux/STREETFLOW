using System.Text.Json;

namespace Streetflow.Logistics;

public static class JsonDefaults
{
  public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };
}

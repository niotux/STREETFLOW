using System.Text.Json;
using Streetflow.Casper;
using Streetflow.Traffic.Contracts;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string path = builder.Configuration["Casper:ConfigurationFile"] ?? "configuration.json";
CasperConfiguration config = CasperConfiguration.Load(Path.GetFullPath(path, builder.Environment.ContentRootPath));
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.ConfigureHttpJsonOptions(json =>
{
  json.SerializerOptions.PropertyNamingPolicy = CasperConfiguration.Json.PropertyNamingPolicy;
  json.SerializerOptions.UnmappedMemberHandling = CasperConfiguration.Json.UnmappedMemberHandling;
  json.SerializerOptions.RespectRequiredConstructorParameters = true;
});
ObservationStore store = new(config);
WebApplication app = builder.Build();
app.Use(async (context, next) =>
{
  try
  {
    await next(context);
  }
  catch (RequestError error)
  {
    context.Response.StatusCode = error.Status;
    await context.Response.WriteAsJsonAsync(new { code = error.Code, message = error.Message });
  }
  catch (BadHttpRequestException error)
  {
    context.Response.StatusCode = error.StatusCode;
    await context.Response.WriteAsJsonAsync(new { code = "invalid_request", message = "Invalid observation JSON or request size" });
  }
});
app.MapGet("/health", () => new { status = "ok", component = "Casper", cameras = config.Cameras.Length });
app.MapPost("/api/v1/cameras/{id}/observations", (string id, CameraObservation frame, HttpContext context) =>
{
  string authorization = context.Request.Headers.Authorization.ToString();
  string? token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null;
  return store.Ingest(id, token, frame, DateTimeOffset.UtcNow);
});
RouteGroupBuilder taco = app.MapGroup("/api/v1/taco");
taco.MapGet("/session", () => new TacoSession(store.SessionId, config.WorldId, config.Revision, null, "running"));
taco.MapGet("/sessions/{id}/map", (string id) =>
{
  store.RequireSession(id);
  return new TacoMap(config.WorldId, config.Revision, "Configured camera coverage", "local_meters", config.Lanes, config.Devices)
  {
    Detectors = config.Detectors
  };
});
taco.MapGet("/sessions/{id}/devices", (string id) =>
{
  store.RequireSession(id);
  return config.Devices.Select(device => new TacoDeviceState(device.Id, "unknown"));
});
taco.MapGet("/sessions/{id}/traffic", (string id) => store.Traffic(id, DateTimeOffset.UtcNow));
taco.MapGet("/sessions/{id}/queues", (string id) =>
{
  TacoTraffic traffic = store.Traffic(id, DateTimeOffset.UtcNow);
  Dictionary<string, int?> queued = traffic.Lanes.ToDictionary(lane => lane.LaneId, lane => lane.QueuedVehicleCount, StringComparer.Ordinal);
  int?[][] devices = config.Devices.Select(device => device.LaneIds.Select(lane => queued.GetValueOrDefault(lane)).ToArray()).ToArray();
  double time = traffic.ObservedAt?.ToUnixTimeMilliseconds() / 1000.0 ?? double.NaN;
  return Results.Bytes(TacoQueueFrame.Encode(traffic.Sequence, time, traffic.Status != "unavailable", devices), TacoQueueFrame.ContentType);
});
taco.MapGet("/capabilities", () => new { traffic = true, queues = true, signal_control = false, source = "camera" });
taco.MapPut("/sessions/{id}/devices/{deviceId}", (string id, string deviceId) =>
{
  store.RequireSession(id);
  throw new RequestError(501, "signal_control_unavailable", "Casper measures traffic; no physical signal controller is installed");
});
await app.RunAsync();

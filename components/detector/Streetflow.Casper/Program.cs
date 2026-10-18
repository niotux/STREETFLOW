using System.Text.Json;
using Streetflow.Casper;
using Streetflow.Traffic.Contracts;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string path = builder.Configuration["Casper:ConfigurationFile"] ?? "configuration.json";
string configurationPath = Path.GetFullPath(path, builder.Environment.ContentRootPath);
CasperConfiguration config = CasperConfiguration.Load(configurationPath);
string calibrationDirectory = builder.Configuration["Casper:CalibrationDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "calibrations");
bool previewEnabled = builder.Configuration.GetValue("Casper:EnablePreview", false);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 256 * 1024);
builder.Services.ConfigureHttpJsonOptions(json =>
{
  json.SerializerOptions.PropertyNamingPolicy = CasperConfiguration.Json.PropertyNamingPolicy;
  json.SerializerOptions.UnmappedMemberHandling = CasperConfiguration.Json.UnmappedMemberHandling;
  json.SerializerOptions.RespectRequiredConstructorParameters = true;
});
CameraViewStore views = new(config, configurationPath, calibrationDirectory);
ObservationStore store = new(config, views);
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
app.MapGet("/api/v1/cameras", () => views.Cameras(DateTimeOffset.UtcNow, store));
app.MapGet("/api/v1/cameras/{id}/calibration", (string id, HttpContext context) => views.Get(id, Bearer(context)));
app.MapPut("/api/v1/cameras/{id}/calibration", (string id, CameraViewCalibration view, HttpContext context) => views.Save(id, Bearer(context), view));
app.MapPost("/api/v1/cameras/{id}/preview", async (string id, HttpContext context) =>
{
  if (!previewEnabled)
  {
    throw new RequestError(404, "preview_disabled", "Camera preview is disabled");
  }
  if (context.Request.ContentType != "image/jpeg")
  {
    throw new RequestError(415, "invalid_preview", "Expected image/jpeg");
  }
  if (!DateTimeOffset.TryParse(context.Request.Headers["X-Observed-At"], out DateTimeOffset observedAt))
  {
    throw new RequestError(400, "invalid_preview", "Missing UTC preview time");
  }
  using MemoryStream buffer = new();
  await context.Request.Body.CopyToAsync(buffer);
  views.PutPreview(id, Bearer(context), context.Request.Headers["X-Calibration-Revision"],
    context.Request.Headers["X-Measurement-Status"], observedAt, buffer.ToArray());
  return Results.Ok();
});
app.MapGet("/api/v1/cameras/{id}/preview", (string id) =>
{
  if (!previewEnabled)
  {
    throw new RequestError(404, "preview_disabled", "Camera preview is disabled");
  }
  CameraPreview? preview = views.Preview(id);
  if (preview is null)
  {
    return Results.NotFound();
  }
  return Results.Bytes(preview.Image, "image/jpeg");
});
app.MapPost("/api/v1/cameras/{id}/observations", (string id, CameraObservation frame, HttpContext context) =>
{
  return store.Ingest(id, Bearer(context), frame, DateTimeOffset.UtcNow);
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

static string? Bearer(HttpContext context)
{
  string authorization = context.Request.Headers.Authorization.ToString();
  return authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null;
}

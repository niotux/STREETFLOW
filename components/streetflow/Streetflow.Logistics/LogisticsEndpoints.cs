using Streetflow.Traffic.Contracts;

namespace Streetflow.Logistics;

public static class LogisticsEndpoints
{
  public static void MapLogisticsApi(this WebApplication app)
  {
    app.MapGet("/health", () => new HealthResponse("ok", "Melchior", "manual"));
    RouteGroupBuilder api = app.MapGroup("/api/v1");
    api.MapGet("/connection", (ConnectionStore connections) => connections.Get());
    api.MapPut("/connection", (TlcConnection request, ConnectionStore connections, CancellationToken token) => connections.SaveAsync(request, token));
    api.MapGet("/session", (TlcApiClient client, CancellationToken token) => client.SessionAsync(token));
    api.MapGet("/sessions/{id}/map", (string id, TlcApiClient client, CancellationToken token) => client.MapAsync(id, token));
    api.MapGet("/sessions/{id}/devices", (string id, TlcApiClient client, CancellationToken token) => client.DevicesAsync(id, token));
    api.MapPut("/sessions/{id}/devices/{deviceId}", (string id, string deviceId, TlcDeviceCommand request, TlcApiClient client, CancellationToken token) => client.ControlAsync(id, deviceId, request, token));
  }
}

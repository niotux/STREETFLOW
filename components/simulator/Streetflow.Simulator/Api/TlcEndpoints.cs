using Streetflow.Simulator.City;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public static class TlcEndpoints
{
  public static void MapTlcApi(this WebApplication app)
  {
    RouteGroupBuilder api = app.MapGroup("/api/v1/tlc");
    api.MapGet("/session", (CityRunCoordinator coordinator, CancellationToken token) => coordinator.TlcSessionAsync(token));
    api.MapGet("/sessions/{id}/map", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.TlcMapAsync(id, token));
    api.MapGet("/sessions/{id}/devices", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.DevicesAsync(id, token));
    api.MapPut("/sessions/{id}/devices/{deviceId}", (string id, string deviceId, TlcDeviceCommand request, CityRunCoordinator coordinator, CancellationToken token) => coordinator.ControlDeviceAsync(id, deviceId, request, token));
  }
}

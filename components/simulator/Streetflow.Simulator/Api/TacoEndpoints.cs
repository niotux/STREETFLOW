using Streetflow.Simulator.City;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Api;

public static class TacoEndpoints
{
  public static void MapTacoApi(this WebApplication app)
  {
    RouteGroupBuilder api = app.MapGroup("/api/v1/taco");
    api.MapGet("/session", (CityRunCoordinator coordinator, CancellationToken token) => coordinator.TacoSessionAsync(token));
    api.MapGet("/sessions/{id}/map", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.TacoMapAsync(id, token));
    api.MapGet("/sessions/{id}/devices", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.DevicesAsync(id, token));
    api.MapGet("/sessions/{id}/traffic", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.TacoTrafficAsync(id, token));
    api.MapGet("/capabilities", () => new { traffic = true, signal_control = true, source = "simulation" });
    api.MapPut("/sessions/{id}/devices/{deviceId}", (string id, string deviceId, TacoDeviceCommand request, CityRunCoordinator coordinator, CancellationToken token) => coordinator.ControlDeviceAsync(id, deviceId, request, token));
  }
}

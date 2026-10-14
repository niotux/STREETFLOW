using Streetflow.Simulator.City;

namespace Streetflow.Simulator.Api;

public static class CityRunEndpoints
{
  public static void MapCityRunApi(this WebApplication app)
  {
    app.MapPost("/api/v1/projects/{id}/runs", async (string id, CreateCityRunRequest request, CityRunCoordinator coordinator, CancellationToken token) =>
    {
      CityRunStatus status = await coordinator.CreateAsync(id, request, token);
      return Results.Created($"/api/v1/city-runs/{status.Id}", status);
    });
    RouteGroupBuilder runs = app.MapGroup("/api/v1/city-runs");
    runs.MapGet("/", (CityRunCoordinator coordinator, CancellationToken token) => coordinator.ListAsync(token));
    runs.MapGet("/{id}", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.GetAsync(id, token));
    runs.MapGet("/{id}/snapshot", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.SnapshotAsync(id, token));
    runs.MapGet("/{id}/map", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.MapAsync(id, token));
    runs.MapGet("/{id}/signals", (string id, CityRunCoordinator coordinator, CancellationToken token) => coordinator.SignalsAsync(id, token));
    runs.MapPut("/{id}/signals/{signalId}", (string id, string signalId, SignalControlRequest request, CityRunCoordinator coordinator, CancellationToken token) => coordinator.ControlSignalAsync(id, signalId, request, token));
    runs.MapPost("/{id}/{command}", (string id, string command, CityRunCommand request, CityRunCoordinator coordinator, CancellationToken token) => coordinator.CommandAsync(id, command, request, token));
  }
}

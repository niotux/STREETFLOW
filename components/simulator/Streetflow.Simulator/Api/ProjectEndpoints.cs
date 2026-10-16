using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Api;

public static class ProjectEndpoints
{
  private const int DefaultPageSize = 1000;
  private const int MaximumPageSize = 10000;
  public static void MapProjectApi(this WebApplication app)
  {
    RouteGroupBuilder api = app.MapGroup("/api/v1/projects");
    api.MapGet("/sources", (ProjectCatalog catalog, CancellationToken token) => catalog.ListSourcesAsync(token));
    api.MapGet("/", (ProjectCatalog catalog, CancellationToken token) => catalog.ListAsync(token));
    api.MapGet("/{id}", async (string id, ProjectCatalog catalog, CancellationToken token) => (await catalog.GetAsync(id, token)).Info);
    api.MapGet("/{id}/demand", async (string id, ProjectCatalog catalog, CancellationToken token) => ReadDemand(await catalog.GetAsync(id, token)));
    api.MapGet("/{id}/model", async (string id, ProjectCatalog catalog, CancellationToken token) => ReadModel(await catalog.GetAsync(id, token)));
    api.MapGet("/{id}/weights", async (string id, ProjectCatalog catalog, CancellationToken token) => Results.Ok(ReadWeights(await catalog.GetAsync(id, token))));
    api.MapGet("/{id}/map", async (string id, int? offset, int? limit, ProjectCatalog catalog, CancellationToken token) => Results.Ok(await ReadMapAsync(catalog, id, offset, limit, token)));
  }

  private static IResult ReadDemand(CityProject project)
  {
    if (project.Demand is not null)
    {
      return Results.Ok(project.Demand);
    }

    return ReadModel(project);
  }

  private static IResult ReadModel(CityProject project)
  {
    if (project.NeuralModel is not null)
    {
      return Results.Ok(project.NeuralModel);
    }

    return Results.Ok(project.TransformerModel ?? throw new ApiException(409, "wrong_project_format", "This project uses Edge Population; read its demand profiles"));
  }

  private static TransformerWeights ReadWeights(CityProject project) => project.TransformerWeights ?? throw new ApiException(409, "wrong_project_format", "This project has no version 2 Transformer Routes weights");

  private static async Task<ProjectMapResponse> ReadMapAsync(ProjectCatalog catalog, string id, int? offset, int? limit, CancellationToken token)
  {
    int start = offset ?? 0;
    int size = limit ?? DefaultPageSize;
    if (start < 0 || size < 1 || size > MaximumPageSize)
    {
      throw new ApiException(400, "invalid_pagination", "offset must be nonnegative; limit must be 1..10000");
    }

    CityProject project = await catalog.GetAsync(id, token);
    LaneMap[] lanes = project.Lanes;
    List<ProjectMapEdge> edges = [];
    int totalEdges = 0;
    foreach (ProjectEdge edge in project.Edges)
    {
      if (!edge.Ordinary)
      {
        continue;
      }

      if (totalEdges >= start && edges.Count < size)
      {
        edges.Add(new ProjectMapEdge(edge.Id, edge.Name, edge.From, edge.To));
      }

      totalEdges++;
    }

    return new ProjectMapResponse(project.Manifest.Id, project.Revision, project.Manifest.Network.Attribution, "SUMO local meters", lanes.Length, project.Junctions.Length, totalEdges, start, Endpoints.Page(lanes, start, size), Endpoints.Page(project.Junctions, start, size), edges.ToArray(), project.SignalIds, project.TerminalPoints, project.SignalPoints) { TrafficDevices = project.TrafficDevices };
  }
}

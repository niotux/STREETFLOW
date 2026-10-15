using Microsoft.AspNetCore.Http.Features;
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
    api.MapGet("/folders", (ProjectCatalog catalog) => catalog.ListFolders());
    api.MapPost("/folders", async (FolderImportRequest request, ProjectCatalog catalog, CancellationToken token) =>
    {
      ProjectInfo project = await catalog.ImportFolderAsync(request.Path, token);
      return Results.Created(ProjectLocation(project), project);
    });
    api.MapDelete("/folders", async ([Microsoft.AspNetCore.Mvc.FromBody] FolderImportRequest request, ProjectCatalog catalog, CancellationToken token) =>
    {
      await catalog.RemoveFolderAsync(request.Path, token);
      return Results.NoContent();
    });
    api.MapGet("/", (ProjectCatalog catalog) => catalog.List());
    api.MapGet("/{id}", (string id, ProjectCatalog catalog) => catalog.Get(id).Info);
    api.MapGet("/{id}/demand", (string id, ProjectCatalog catalog) => ReadDemand(catalog.Get(id)));
    api.MapGet("/{id}/model", (string id, ProjectCatalog catalog) => ReadModel(catalog.Get(id)));
    api.MapGet("/{id}/weights", (string id, ProjectCatalog catalog) => Results.Ok(ReadWeights(catalog.Get(id))));
    api.MapGet("/{id}/map", (string id, int? offset, int? limit, ProjectCatalog catalog) => Results.Ok(ReadMap(catalog, id, offset, limit)));
    api.MapPost("/import/folder", async (FolderImportRequest request, ProjectCatalog catalog, CancellationToken token) =>
    {
      ProjectInfo project = await catalog.ImportFolderAsync(request.Path, token);
      return Results.Created(ProjectLocation(project), project);
    });
    api.MapPost("/import/zip", ImportZipAsync).Accepts<Stream>("application/zip", "application/octet-stream");
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

  private static ProjectMapResponse ReadMap(ProjectCatalog catalog, string id, int? offset, int? limit)
  {
    int start = offset ?? 0;
    int size = limit ?? DefaultPageSize;
    if (start < 0 || size < 1 || size > MaximumPageSize)
    {
      throw new ApiException(400, "invalid_pagination", "offset must be nonnegative; limit must be 1..10000");
    }

    CityProject project = catalog.Get(id);
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

  private static async Task<IResult> ImportZipAsync(HttpRequest request, ProjectCatalog catalog, SimulatorOptions options, CancellationToken token)
  {
    if (request.ContentLength > options.MaximumProjectBytes)
    {
      throw new ApiException(413, "project_too_large", $"ZIP exceeds {options.MaximumProjectBytes} bytes");
    }

    IHttpMaxRequestBodySizeFeature? feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (feature is not null)
    {
      if (!feature.IsReadOnly)
      {
        feature.MaxRequestBodySize = options.MaximumProjectBytes;
      }
    }

    ProjectInfo project = await catalog.ImportZipAsync(request.Body, token);
    return Results.Created(ProjectLocation(project), project);
  }

  private static string ProjectLocation(ProjectInfo project) => $"/api/v1/projects/{Uri.EscapeDataString(project.Manifest.Id)}";
}

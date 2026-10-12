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
    api.MapGet("/", (ProjectCatalog catalog) => catalog.List());
    api.MapGet("/{id}", (string id, ProjectCatalog catalog) => catalog.Get(id).Info);
    api.MapGet("/{id}/demand", (string id, ProjectCatalog catalog) => catalog.Get(id).Demand);
    api.MapGet("/{id}/map", (string id, int? offset, int? limit, ProjectCatalog catalog) => Results.Ok(ReadMap(catalog, id, offset, limit)));
    api.MapPost("/import/folder", async (FolderImportRequest request, ProjectCatalog catalog, CancellationToken token) =>
    {
      ProjectInfo project = await catalog.ImportFolderAsync(request.Path, token);
      return Results.Created(ProjectLocation(project), project);
    });
    api.MapPost("/import/zip", ImportZipAsync).Accepts<Stream>("application/zip", "application/octet-stream");
  }

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

    return new ProjectMapResponse(project.Manifest.Id, project.Revision, project.Manifest.Network.Attribution, "SUMO local meters", lanes.Length, project.Junctions.Length, totalEdges, start, Endpoints.Page(lanes, start, size), Endpoints.Page(project.Junctions, start, size), edges.ToArray(), project.SignalIds);
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class TransformerLoadingTests
{
  [Fact]
  public async Task EdgePopulationAndTransformerRoutesCanBeImportedTogether()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    string original = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "tiny-graph");
    Assert.Equal(HttpStatusCode.Created, (await Import(client, original)).StatusCode);
    Assert.Equal(HttpStatusCode.Created, (await Import(client, TransformerProjectFixture.Source)).StatusCode);

    CityProject oldProject = CityProjectLoader.Load(original);
    CityProject newProject = CityProjectLoader.Load(TransformerProjectFixture.Source);
    Assert.Equal("Edge Population", oldProject.ModelKind);
    Assert.NotNull(oldProject.Demand);
    Assert.Null(oldProject.TransformerModel);
    Assert.Equal("Transformer Routes", newProject.ModelKind);
    Assert.Null(newProject.Demand);
    Assert.NotNull(newProject.TransformerModel);
    Assert.NotNull(newProject.TransformerWeights);

    JsonElement oldDemand = await Body(await client.GetAsync("/api/v1/projects/tiny-graph/demand"));
    Assert.Equal(8, oldDemand.GetProperty("profiles").GetArrayLength());
    string prefix = $"/api/v1/projects/{TransformerProjectFixture.ProjectId}";
    JsonElement model = await Body(await client.GetAsync(prefix + "/model"));
    JsonElement weights = await Body(await client.GetAsync(prefix + "/weights"));
    JsonElement newDemand = await Body(await client.GetAsync(prefix + "/demand"));
    Assert.Equal(2, model.GetProperty("format_version").GetInt32());
    Assert.Equal(2, newDemand.GetProperty("format_version").GetInt32());
    Assert.False(newDemand.TryGetProperty("profiles", out _));
    Assert.Equal(4, model.GetProperty("axes").GetProperty("origin").GetArrayLength());
    Assert.Equal(960, weights.GetProperty("tensors").GetProperty("movement_logits").GetProperty("values").GetArrayLength());
    Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/v1/projects/tiny-graph/model")).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/v1/projects/tiny-graph/weights")).StatusCode);
    JsonElement projects = await Body(await client.GetAsync("/api/v1/projects"));
    Assert.Equal(2, projects.GetArrayLength());
    Assert.Equal(0, (await Body(await client.GetAsync("/api/v1/city-runs"))).GetArrayLength());
  }

  [Theory]
  [InlineData("arrival-shape", "weights.json")]
  [InlineData("journey-length", "weights.json")]
  [InlineData("movement-axis-order", "weights.json")]
  [InlineData("negative-arrival", "weights.json")]
  [InlineData("unknown-tensor", "weights.json")]
  [InlineData("period-gap", "journey-model.json")]
  [InlineData("unknown-origin", "journey-model.json")]
  [InlineData("wrong-terminal-role", "journey-model.json")]
  [InlineData("unknown-destination", "journey-model.json")]
  [InlineData("duplicate-movement", "journey-model.json")]
  [InlineData("missing-movement", "journey-model.json")]
  [InlineData("unknown-movement", "journey-model.json")]
  public async Task InvalidModelCoordinatesAndTensorsAreRejectedBeforeImport(string mutation, string expectedFile)
  {
    using TransformerProjectFixture fixture = new();
    if (expectedFile == "weights.json")
    {
      fixture.Change(expectedFile, node => ChangeWeights(node, mutation));
    }
    else
    {
      fixture.Change(expectedFile, node => ChangeModel(node, mutation));
    }

    if (mutation == "missing-movement")
    {
      fixture.Change("weights.json", weights =>
      {
        JsonNode tensor = weights["tensors"]!["movement_logits"]!;
        tensor["shape"]![5] = 11;
        JsonArray values = tensor["values"]!.AsArray();
        for (int row = 79; row >= 0; row--)
        {
          values.RemoveAt(row * 12);
        }
      });
    }

    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    HttpResponseMessage response = await Import(client, fixture.Path);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    ApiError error = (await response.Content.ReadFromJsonAsync<ApiError>(JsonDefaults.Options))!;
    Assert.Equal("invalid_project", error.Code);
    Assert.Contains(expectedFile, error.Message);
    Assert.Equal(0, (await Body(await client.GetAsync("/api/v1/projects"))).GetArrayLength());
  }

  [Theory]
  [InlineData("../outside.json")]
  [InlineData("/tmp/outside.json")]
  [InlineData("C:\\outside.json")]
  [InlineData("nested/../../outside.json")]
  public void WeightsReferencesCannotEscapeTheProject(string path)
  {
    using TransformerProjectFixture fixture = new();
    fixture.Change("journey-model.json", model => model["weights_file"] = path);
    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(fixture.Path));
    Assert.Equal("invalid_project", error.Code);
    Assert.Contains("Unsafe project path", error.Message);
  }

  [Fact]
  public void RevisionIncludesWeightsAndTheModel()
  {
    using TransformerProjectFixture fixture = new();
    string original = CityProjectLoader.Load(fixture.Path).Revision;
    fixture.Change("weights.json", weights => weights["tensors"]!["movement_logits"]!["values"]![0] = 0.125);
    string changedWeights = CityProjectLoader.Load(fixture.Path).Revision;
    Assert.NotEqual(original, changedWeights);
    fixture.Change("journey-model.json", model => model["evidence"]![0]!["note"] = "Changed authored assumption.");
    Assert.NotEqual(changedWeights, CityProjectLoader.Load(fixture.Path).Revision);
  }

  [Fact]
  public void UnsupportedMultilaneRoutingIsReportedInsteadOfIgnoringLaneFeasibility()
  {
    using TransformerProjectFixture fixture = new();
    string networkPath = Path.Combine(fixture.Path, "network.net.xml");
    XDocument network = XDocument.Load(networkPath);
    foreach (XElement edge in network.Root!.Elements("edge"))
    {
      if (edge.Attribute("id")!.Value != "south_in")
      {
        continue;
      }

      XElement lane = new(edge.Element("lane")!);
      lane.SetAttributeValue("id", "south_in_1");
      lane.SetAttributeValue("index", 1);
      edge.Add(lane);
      break;
    }

    network.Save(networkPath);
    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(fixture.Path));
    Assert.Equal("invalid_project", error.Code);
    Assert.Contains("lane", error.Message);
    Assert.Contains("south_in", error.Message);
  }

  [Fact]
  public void MovementPermissionsIncludeLaterInternalConnectorLanes()
  {
    using TransformerProjectFixture fixture = new();
    string networkPath = Path.Combine(fixture.Path, "network.net.xml");
    XDocument network = XDocument.Load(networkPath);
    bool changed = false;
    foreach (XElement lane in network.Descendants("lane"))
    {
      if (lane.Attribute("id")!.Value == ":center_12_0")
      {
        lane.SetAttributeValue("allow", null);
        lane.SetAttributeValue("disallow", "passenger");
        changed = true;
        break;
      }
    }

    Assert.True(changed);
    network.Save(networkPath);
    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(fixture.Path));
    Assert.Equal("invalid_project", error.Code);
    Assert.Contains("north_in", error.Message);
    Assert.Contains("east_out", error.Message);
  }

  private static void ChangeWeights(JsonNode node, string mutation)
  {
    JsonNode tensors = node["tensors"]!;
    switch (mutation)
    {
      case "arrival-shape":
        tensors["arrival_rates"]!["shape"]![1] = 5;
        break;
      case "journey-length":
        tensors["journey_logits"]!["values"]!.AsArray().RemoveAt(0);
        break;
      case "movement-axis-order":
        tensors["movement_logits"]!["axis_order"]![0] = "origin";
        break;
      case "negative-arrival":
        tensors["arrival_rates"]!["values"]![0] = -1;
        break;
      case "unknown-tensor":
        tensors["extra"] = tensors["arrival_rates"]!.DeepClone();
        break;
    }
  }

  private static void ChangeModel(JsonNode node, string mutation)
  {
    JsonNode axes = node["axes"]!;
    switch (mutation)
    {
      case "period-gap":
        axes["period"]![1]!["start_seconds"] = 25201;
        break;
      case "unknown-origin":
        axes["origin"]![0] = "absent-entry";
        break;
      case "wrong-terminal-role":
        axes["origin"]![0] = "south-exit";
        break;
      case "unknown-destination":
        axes["destination"]![0]!["terminal_ids"]![0] = "absent-exit";
        break;
      case "duplicate-movement":
        axes["movement"]![1] = axes["movement"]![0]!.DeepClone();
        break;
      case "missing-movement":
        axes["movement"]!.AsArray().RemoveAt(0);
        break;
      case "unknown-movement":
        axes["movement"]![0]!["outgoing_edge_id"] = "absent-road";
        break;
    }
  }

  private static Task<HttpResponseMessage> Import(HttpClient client, string path) => client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(path), JsonDefaults.Options);

  private static async Task<JsonElement> Body(HttpResponseMessage response)
  {
    string body = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, body);
    return JsonSerializer.Deserialize<JsonElement>(body);
  }
}

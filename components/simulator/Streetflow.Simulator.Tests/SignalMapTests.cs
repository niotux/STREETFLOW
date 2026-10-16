using System.Net.Http.Json;
using System.Xml.Linq;
using Streetflow.Simulator.Api;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class SignalMapTests
{
  private static string PoduRos => Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "podu-ros");

  [Fact]
  public void JoinedControllersKeepMovementIndicesAndStopLineLocations()
  {
    CityProject project = CityProjectLoader.Load(PoduRos);
    SignalMapPoint[] points = project.SignalPoints;
    Assert.Equal(81, points.Length);
    SignalMapPoint? selected = null;
    foreach (SignalMapPoint point in points)
    {
      if (point.SignalId == "cluster_335640349_9804878311_9804878312_9804878316" && point.LinkIndex == 0)
      {
        selected = point;
        break;
      }
    }

    Assert.NotNull(selected);
    Assert.NotEqual(selected.SignalId, selected.LaneId);
    LaneMap? lane = null;
    foreach (LaneMap candidate in project.Lanes)
    {
      if (candidate.Id == selected.LaneId)
      {
        lane = candidate;
        break;
      }
    }

    Assert.NotNull(lane);
    Point end = lane.Shape[lane.Shape.Count - 1];
    Assert.Equal(end.X, selected.X);
    Assert.Equal(end.Y, selected.Y);
    Assert.NotEmpty(selected.ToLaneId);
  }

  [Fact]
  public async Task PagedMapIncludesAllSignalMovementsBeforeStartingTraffic()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, PoduRos);
    ProjectMapResponse? map = await client.GetFromJsonAsync<ProjectMapResponse>("/api/v1/projects/podu-ros/map?limit=1", JsonDefaults.Options);
    Assert.NotNull(map);
    Assert.Single(map.Lanes);
    Assert.Equal(81, map.SignalPoints.Length);
    Assert.Equal(CityProjectLoader.Load(PoduRos).SignalPoints, map.SignalPoints);
  }

  [Fact]
  public async Task AdditionalProgramsRemainOffUntilControlledThroughApi()
  {
    using ProjectFixture fixture = new();
    foreach (string file in Directory.GetFiles(PoduRos))
    {
      File.Copy(file, Path.Combine(fixture.Path, Path.GetFileName(file)), overwrite: true);
    }

    XElement logic = new("tlLogic", new XAttribute("id", "1846743533"), new XAttribute("type", "static"), new XAttribute("programID", "red-yellow-test"), new XAttribute("offset", "0"));
    logic.Add(new XElement("phase", new XAttribute("duration", "3"), new XAttribute("state", "uuuuu")));
    new XDocument(new XElement("additional", logic)).Save(Path.Combine(fixture.Path, "signals.add.xml"));
    fixture.ChangeManifest(manifest => manifest["additional_files"] = new System.Text.Json.Nodes.JsonArray("signals.add.xml"));

    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/podu-ros/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(status.State == "paused", status.Failure);
    Snapshot initial = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{status.Id}/snapshot", JsonDefaults.Options))!;
    SignalState off = Assert.Single(initial.Signals, signal => signal.Id == "1846743533");
    Assert.DoesNotContain('u', off.State);
    Assert.All(off.State, code => Assert.True(code == 'o' || code == 'O'));
    response = await client.PutAsJsonAsync($"/api/v1/city-runs/{status.Id}/signals/1846743533", new SignalControlRequest("red-yellow"), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    response = await client.PostAsJsonAsync($"/api/v1/city-runs/{status.Id}/step", new CityRunCommand(1), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    Snapshot snapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{status.Id}/snapshot", JsonDefaults.Options))!;
    SignalState signal = Assert.Single(snapshot.Signals, signal => signal.Id == "1846743533");
    Assert.Equal("uuuuu", signal.State);
    response = await client.PostAsJsonAsync($"/api/v1/city-runs/{status.Id}/stop", new CityRunCommand(), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
  }
}

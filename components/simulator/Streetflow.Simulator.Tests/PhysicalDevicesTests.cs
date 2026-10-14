using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Tests;

public sealed class PhysicalDevicesTests
{
  [Theory]
  [InlineData("tiny-graph-devices")]
  [InlineData("four-way-transformer-routes-devices")]
  public async Task TlcPublishesOnlyPhysicalDevicesAndTheirMap(string projectId)
  {
    using PhysicalDevicesFixture fixture = new(projectId);
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    Assert.Null((await client.GetFromJsonAsync<TlcSession>("/api/v1/tlc/session", JsonDefaults.Options))!.Id);
    CityRunStatus run = await Start(client, fixture);
    TlcSession session = (await client.GetFromJsonAsync<TlcSession>("/api/v1/tlc/session", JsonDefaults.Options))!;
    Assert.Equal(run.Id, session.Id);
    Assert.Equal("four-way", session.WorldId);
    string mapText = await client.GetStringAsync($"/api/v1/tlc/sessions/{run.Id}/map");
    TlcMap map = JsonSerializer.Deserialize<TlcMap>(mapText, JsonDefaults.Options)!;
    Assert.Equal(4, map.Devices.Length);
    Assert.NotEmpty(map.Lanes);
    Assert.Equal(session.Revision, map.Revision);
    Assert.DoesNotContain("signal_points", mapText);
    Assert.DoesNotContain("link_index", mapText);
    Assert.DoesNotContain("signal_id", mapText);
    Assert.DoesNotContain("center", JsonSerializer.Serialize(map.Devices, JsonDefaults.Options));
    TlcDeviceState[] devices = (await client.GetFromJsonAsync<TlcDeviceState[]>($"/api/v1/tlc/sessions/{run.Id}/devices", JsonDefaults.Options))!;
    Assert.Equal(4, devices.Length);
    Assert.All(devices, device => Assert.Equal("off", device.State));
    await Stop(client, run.Id);
  }

  [Theory]
  [InlineData("tiny-graph-devices")]
  [InlineData("four-way-transformer-routes-devices")]
  public async Task DeviceCommandsChangeAllMappedMovementsAndPreserveOtherDevices(string projectId)
  {
    using PhysicalDevicesFixture fixture = new(projectId);
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await Start(client, fixture);
    TlcSession session = (await client.GetFromJsonAsync<TlcSession>("/api/v1/tlc/session", JsonDefaults.Options))!;
    string path = $"/api/v1/tlc/sessions/{run.Id}/devices/north";
    Snapshot initial = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
    string off = Assert.Single(initial.Signals).State;
    foreach (string action in new[] { "on", "green", "yellow", "red-yellow", "red", "off" })
    {
      HttpResponseMessage response = await client.PutAsJsonAsync(path, new TlcDeviceCommand(action, session.WorldId!, session.Revision!), JsonDefaults.Options);
      response.EnsureSuccessStatusCode();
      TlcDeviceState applied = (await response.Content.ReadFromJsonAsync<TlcDeviceState>(JsonDefaults.Options))!;
      Assert.Equal(action == "on" ? "red" : action, applied.State);
      Snapshot snapshot = (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{run.Id}/snapshot", JsonDefaults.Options))!;
      Assert.Equal(0, snapshot.SimulationTime);
      string state = Assert.Single(snapshot.Signals).State;
      for (int index = 0; index < state.Length; index++)
      {
        if (index < 3)
        {
          char expected = action switch { "off" => off[index], "green" => 'G', "yellow" => 'y', "red-yellow" => 'u', _ => 'r' };
          Assert.Equal(expected, state[index]);
        }
        else
        {
          Assert.Equal(off[index], state[index]);
        }
      }

      Assert.Equal(applied.State, Assert.Single(snapshot.DeviceStates, device => device.Id == "north").State);
    }

    await Stop(client, run.Id);
  }

  [Fact]
  public async Task WrongWorldStaleRevisionUnknownDevicesAndStoppedSessionsCannotBeControlled()
  {
    using PhysicalDevicesFixture fixture = new();
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await Start(client, fixture);
    TlcSession session = (await client.GetFromJsonAsync<TlcSession>("/api/v1/tlc/session", JsonDefaults.Options))!;
    string path = $"/api/v1/tlc/sessions/{run.Id}/devices/";
    Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path + "north", new TlcDeviceCommand("green", "other-world", session.Revision!), JsonDefaults.Options)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path + "north", new TlcDeviceCommand("green", session.WorldId!, "outdated"), JsonDefaults.Options)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(path + "center", new TlcDeviceCommand("green", session.WorldId!, session.Revision!), JsonDefaults.Options)).StatusCode);
    HttpResponseMessage raw = await client.PutAsync(path + "north", new StringContent("{\"action\":\"green\",\"world_id\":\"four-way\",\"revision\":\"x\",\"link_index\":0}", System.Text.Encoding.UTF8, "application/json"));
    Assert.Equal(HttpStatusCode.BadRequest, raw.StatusCode);
    await Stop(client, run.Id);
    Assert.Null((await client.GetFromJsonAsync<TlcSession>("/api/v1/tlc/session", JsonDefaults.Options))!.Id);
    Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path + "north", new TlcDeviceCommand("green", session.WorldId!, session.Revision!), JsonDefaults.Options)).StatusCode);
  }

  [Fact]
  public void ChangingTrafficWeightsDoesNotInvalidateTheWorldButChangingDevicesDoes()
  {
    using PhysicalDevicesFixture fixture = new("four-way-transformer-routes-devices");
    CityProject before = CityProjectLoader.Load(fixture.Path);
    fixture.Change("weights.json", node => node["tensors"]!["arrival_rates"]!["values"]![0] = 31);
    CityProject trafficChanged = CityProjectLoader.Load(fixture.Path);
    Assert.NotEqual(before.Revision, trafficChanged.Revision);
    Assert.Equal(before.WorldRevision, trafficChanged.WorldRevision);
    fixture.Change("traffic-lights.json", node => node["devices"]![0]!["label"] = "Renamed north signal");
    CityProject infrastructureChanged = CityProjectLoader.Load(fixture.Path);
    Assert.NotEqual(trafficChanged.WorldRevision, infrastructureChanged.WorldRevision);
  }

  [Fact]
  public void SameWorldCanBeReusedAcrossTrafficFormats()
  {
    using PhysicalDevicesFixture oldFormat = new();
    using PhysicalDevicesFixture newFormat = new("four-way-transformer-routes-devices");
    CityProject old = CityProjectLoader.Load(oldFormat.Path);
    CityProject fresh = CityProjectLoader.Load(newFormat.Path);
    Assert.Equal(old.WorldId, fresh.WorldId);
    Assert.Equal(old.WorldRevision, fresh.WorldRevision);
    Assert.NotEqual(old.Revision, fresh.Revision);
  }

  [Fact]
  public async Task UnmappedLegacyProjectsExposeNoInventedPhysicalDevices()
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    string source = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "tiny-graph");
    await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(source), JsonDefaults.Options);
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/projects/tiny-graph/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus run = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    TlcMap map = (await client.GetFromJsonAsync<TlcMap>($"/api/v1/tlc/sessions/{run.Id}/map", JsonDefaults.Options))!;
    Assert.NotEmpty(map.Lanes);
    Assert.Empty(map.Devices);
    await Stop(client, run.Id);
  }

  [Theory]
  [InlineData("unknown-link")]
  [InlineData("shared-binding")]
  [InlineData("wrong-lane")]
  [InlineData("duplicate-device")]
  [InlineData("missing-lane")]
  [InlineData("raw-path")]
  public void InvalidPhysicalMappingsAreRejected(string mutation)
  {
    using PhysicalDevicesFixture fixture = new();
    if (mutation == "raw-path")
    {
      fixture.Change("experiment.json", node => node["traffic_lights_file"] = "../traffic-lights.json");
    }
    else
    {
      fixture.Change("traffic-lights.json", node =>
      {
        switch (mutation)
        {
          case "unknown-link": node["devices"]![0]!["link_indices"]![0] = 999; break;
          case "shared-binding": node["devices"]![1]!["link_indices"]![0] = 0; break;
          case "wrong-lane": node["devices"]![0]!["lane_ids"]![0] = "east_in_0"; break;
          case "duplicate-device": node["devices"]![1]!["id"] = "north"; break;
          case "missing-lane": node["devices"]![0]!["lane_ids"] = null; break;
        }
      });
    }

    ApiException error = Assert.Throws<ApiException>(() => CityProjectLoader.Load(fixture.Path));
    Assert.Equal("invalid_project", error.Code);
  }

  [Fact]
  public async Task DeviceInputsAreSavedAndReplayWithoutDependingOnSourceFolder()
  {
    using PhysicalDevicesFixture fixture = new();
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    CityRunStatus run = await Start(client, fixture);
    string saved = Path.Combine(factory.DataRoot, "city-runs", run.Id, "package", "traffic-lights.json");
    Assert.True(File.Exists(saved));
    fixture.Change("traffic-lights.json", node => node["devices"]![0]!["label"] = "Source changed");
    await Stop(client, run.Id);
    HttpResponseMessage restarted = await client.PostAsJsonAsync($"/api/v1/city-runs/{run.Id}/restart", new CityRunCommand(), JsonDefaults.Options);
    restarted.EnsureSuccessStatusCode();
    CityRunStatus replay = (await restarted.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    TlcMap map = (await client.GetFromJsonAsync<TlcMap>($"/api/v1/tlc/sessions/{replay.Id}/map", JsonDefaults.Options))!;
    Assert.Equal("North approach", Assert.Single(map.Devices, device => device.Id == "north").Label);
    await Stop(client, replay.Id);
  }

  private static async Task<CityRunStatus> Start(HttpClient client, PhysicalDevicesFixture fixture)
  {
    HttpResponseMessage imported = await client.PostAsJsonAsync("/api/v1/projects/import/folder", new FolderImportRequest(fixture.Path), JsonDefaults.Options);
    imported.EnsureSuccessStatusCode();
    HttpResponseMessage created = await client.PostAsJsonAsync($"/api/v1/projects/{fixture.Id}/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    created.EnsureSuccessStatusCode();
    CityRunStatus run = (await created.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.Equal("paused", run.State);
    return run;
  }

  private static async Task Stop(HttpClient client, string id)
  {
    (await client.PostAsJsonAsync($"/api/v1/city-runs/{id}/stop", new CityRunCommand(), JsonDefaults.Options)).EnsureSuccessStatusCode();
  }
}

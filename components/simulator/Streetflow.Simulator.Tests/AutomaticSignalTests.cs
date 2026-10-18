extern alias logistics;

using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Traffic.Contracts;
using Logistics = logistics::Streetflow.Logistics;

namespace Streetflow.Simulator.Tests;

public sealed class AutomaticSignalTests
{
  [Theory]
  [InlineData("tiny-graph-devices")]
  [InlineData("podu-ros-neural")]
  public async Task MelchiorEnablesSimulationTimeCyclesAndRespectsManualOverride(string project)
  {
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, Path.Combine(ProjectLoadingTests.Workspace, "projects", "drafts", project));
    using HttpResponseMessage started = await client.PostAsJsonAsync($"/api/v1/projects/{project}/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    started.EnsureSuccessStatusCode();
    CityRunStatus run = (await started.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(run.State == "paused", run.Failure);
    TacoSession session = (await client.GetFromJsonAsync<TacoSession>("/api/v1/taco/session", JsonDefaults.Options))!;
    string modePath = $"/api/v1/taco/sessions/{run.Id}/signal-mode";
    TacoSignalMode initialMode = (await client.GetFromJsonAsync<TacoSignalMode>(modePath, JsonDefaults.Options))!;
    Assert.Equal("manual", initialMode.Mode);
    Assert.True(initialMode.AutomaticSupported);

    var connections = new Logistics.ConnectionStore(new Logistics.LogisticsOptions
    {
      TacoApiBaseUrl = client.BaseAddress!.GetLeftPart(UriPartial.Authority),
      ConnectionFile = Path.Combine(factory.DataRoot, "logistics-connection.json")
    });
    var bridge = new Logistics.TacoApiClient(client, connections, new Logistics.TacoMapCache());
    var automation = new Logistics.SignalAutomation(connections);
    await automation.DiscoverAsync(bridge, session, CancellationToken.None);
    Assert.Equal("automatic", (await bridge.SignalModeAsync(run.Id, CancellationToken.None)).Mode);
    Snapshot first = await Snapshot(client, run.Id);
    Assert.Equal(0, first.SimulationTime);
    Assert.All(first.Signals, signal => Assert.DoesNotContain(signal.State, c => c is 'o' or 'O'));
    await Task.Delay(50);
    Assert.Equal(first.Signals, (await Snapshot(client, run.Id)).Signals);

    TacoMap map = await bridge.MapAsync(run.Id, CancellationToken.None);
    using HttpResponseMessage blocked = await client.PutAsJsonAsync($"/api/v1/taco/sessions/{run.Id}/devices/{map.Devices[0].Id}",
      new TacoDeviceCommand("green", session.WorldId!, session.Revision!), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    using HttpResponseMessage rawBlocked = await client.PutAsJsonAsync($"/api/v1/city-runs/{run.Id}/signals/{Uri.EscapeDataString(first.Signals[0].Id)}",
      new SignalControlRequest("green"), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Conflict, rawBlocked.StatusCode);

    Dictionary<string, HashSet<string>> observed = first.Signals.ToDictionary(s => s.Id, s => new HashSet<string> { s.State });
    for (int second = 0; second < 100; second++)
    {
      await Step(client, run.Id, 2);
      foreach (SignalState signal in (await Snapshot(client, run.Id)).Signals) observed[signal.Id].Add(signal.State);
    }
    Assert.All(observed.Values, states => Assert.True(states.Count > 1, "Every controller must advance its timing program"));
    Assert.Contains(observed.Values.SelectMany(states => states), state => state.Contains('y'));

    await automation.SetAsync(run.Id, new TacoSignalModeCommand("manual", session.WorldId!, session.Revision!), bridge, CancellationToken.None);
    Snapshot held = await Snapshot(client, run.Id);
    await automation.DiscoverAsync(bridge, session, CancellationToken.None);
    Assert.Equal("manual", (await bridge.SignalModeAsync(run.Id, CancellationToken.None)).Mode);
    await Step(client, run.Id, 200);
    Assert.Equal(held.Signals, (await Snapshot(client, run.Id)).Signals);
    await bridge.ControlAsync(run.Id, map.Devices[0].Id, new TacoDeviceCommand("red", session.WorldId!, session.Revision!), CancellationToken.None);

    using HttpResponseMessage restarted = await client.PostAsJsonAsync($"/api/v1/city-runs/{run.Id}/restart", new CityRunCommand(), JsonDefaults.Options);
    restarted.EnsureSuccessStatusCode();
    TacoSession next = (await client.GetFromJsonAsync<TacoSession>("/api/v1/taco/session", JsonDefaults.Options))!;
    Assert.NotEqual(session.Id, next.Id);
    await automation.DiscoverAsync(bridge, next, CancellationToken.None);
    Assert.Equal("automatic", (await bridge.SignalModeAsync(next.Id!, CancellationToken.None)).Mode);
  }

  [Fact]
  public async Task NamedProgramsAndStaleCommandsAreHandledWithoutHardCodedProgramIds()
  {
    using PhysicalDevicesFixture fixture = new();
    string networkFile = Path.Combine(fixture.Path, "network.net.xml");
    XDocument network = XDocument.Load(networkFile);
    foreach (XElement logic in network.Root!.Elements("tlLogic")) logic.SetAttributeValue("programID", "named-morning-plan");
    network.Save(networkFile);
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    using HttpResponseMessage started = await client.PostAsJsonAsync($"/api/v1/projects/{fixture.Id}/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    CityRunStatus run = (await started.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(run.State == "paused", run.Failure);
    TacoSession session = (await client.GetFromJsonAsync<TacoSession>("/api/v1/taco/session", JsonDefaults.Options))!;
    string path = $"/api/v1/taco/sessions/{run.Id}/signal-mode";
    foreach (TacoSignalModeCommand bad in new[]
    {
      new TacoSignalModeCommand("automatic", "wrong-world", session.Revision!),
      new TacoSignalModeCommand("automatic", session.WorldId!, "stale")
    })
    {
      using HttpResponseMessage rejected = await client.PutAsJsonAsync(path, bad, JsonDefaults.Options);
      Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }
    using HttpResponseMessage invalid = await client.PutAsJsonAsync(path, new TacoSignalModeCommand("invalid", session.WorldId!, session.Revision!), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    foreach (double invalidK in new[] { .19, 1.81 })
    {
      using HttpResponseMessage rejected = await client.PutAsJsonAsync(path,
        new TacoSignalModeCommand("automatic", session.WorldId!, session.Revision!) { Policy = new TacoSignalPolicy(Multiplier: invalidK) }, JsonDefaults.Options);
      Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
    using HttpResponseMessage enabled = await client.PutAsJsonAsync(path, new TacoSignalModeCommand("automatic", session.WorldId!, session.Revision!), JsonDefaults.Options);
    enabled.EnsureSuccessStatusCode();
    Snapshot first = await Snapshot(client, run.Id);
    using HttpResponseMessage repeated = await client.PutAsJsonAsync(path, new TacoSignalModeCommand("automatic", session.WorldId!, session.Revision!), JsonDefaults.Options);
    repeated.EnsureSuccessStatusCode();
    Assert.Equal(first.Sequence, (await Snapshot(client, run.Id)).Sequence);
    await Step(client, run.Id, 100);
    Assert.NotEqual(first.Signals[0].State, (await Snapshot(client, run.Id)).Signals[0].State);
    using HttpResponseMessage stopped = await client.PostAsJsonAsync($"/api/v1/city-runs/{run.Id}/stop", new CityRunCommand(), JsonDefaults.Options);
    stopped.EnsureSuccessStatusCode();
    using HttpResponseMessage stale = await client.PutAsJsonAsync(path, new TacoSignalModeCommand("automatic", session.WorldId!, session.Revision!), JsonDefaults.Options);
    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
  }

  private static async Task<Snapshot> Snapshot(HttpClient client, string id) =>
    (await client.GetFromJsonAsync<Snapshot>($"/api/v1/city-runs/{id}/snapshot", JsonDefaults.Options))!;

  private static async Task Step(HttpClient client, string id, int steps)
  {
    using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/city-runs/{id}/step", new CityRunCommand(steps), JsonDefaults.Options);
    response.EnsureSuccessStatusCode();
    CityRunStatus status = (await response.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    Assert.True(status.State == "paused", status.Failure);
  }
}

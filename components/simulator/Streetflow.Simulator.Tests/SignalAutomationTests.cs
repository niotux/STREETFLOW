extern alias logistics;

using System.Net;
using System.Net.Http.Json;
using Streetflow.Traffic.Contracts;
using Logistics = logistics::Streetflow.Logistics;

namespace Streetflow.Simulator.Tests;

public sealed class SignalAutomationTests
{
  [Theory]
  [InlineData(HttpStatusCode.NotFound)]
  [InlineData(HttpStatusCode.NotImplemented)]
  public async Task ReadOnlyProvidersDoNotReceiveAutomaticCommands(HttpStatusCode unsupported)
  {
    int writes = 0;
    using HttpClient http = new(new Handler(request =>
    {
      if (request.Method == HttpMethod.Put) writes++;
      return new HttpResponseMessage(unsupported) { Content = new StringContent("unsupported") };
    }));
    var connection = Connections();
    var client = new Logistics.TacoApiClient(http, connection, new Logistics.TacoMapCache());
    var automation = new Logistics.SignalAutomation(connection);
    await automation.DiscoverAsync(client, new TacoSession("camera", "world", "revision", null, "running"), CancellationToken.None);
    Assert.Equal(0, writes);
    Assert.False((await client.SignalModeAsync("camera", CancellationToken.None)).AutomaticSupported);
  }

  [Fact]
  public async Task FailedActivationIsRetriedButSuccessfulActivationIsNotRepeated()
  {
    int writes = 0;
    using HttpClient http = new(new Handler(request =>
    {
      if (request.Method == HttpMethod.Put && ++writes == 1)
      {
        return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("changed session") };
      }
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(new TacoSignalMode(request.Method == HttpMethod.Put ? "automatic" : "manual", true), options: Logistics.JsonDefaults.Options)
      };
    }));
    var connection = Connections();
    var client = new Logistics.TacoApiClient(http, connection, new Logistics.TacoMapCache());
    var automation = new Logistics.SignalAutomation(connection);
    var session = new TacoSession("session", "world", "revision", "project", "paused");
    await Assert.ThrowsAsync<Logistics.ApiException>(() => automation.DiscoverAsync(client, session, CancellationToken.None));
    await automation.DiscoverAsync(client, session, CancellationToken.None);
    await automation.DiscoverAsync(client, session, CancellationToken.None);
    Assert.Equal(2, writes);
  }

  private static Logistics.ConnectionStore Connections() => new(new Logistics.LogisticsOptions
  {
    TacoApiBaseUrl = "http://test-provider:8080",
    ConnectionFile = Path.Combine(Path.GetTempPath(), "streetflow-signals", Guid.NewGuid() + ".json")
  });

  [Fact]
  public async Task MultiplierPreferenceSurvivesMelchiorRestartAndIsSentToTheNextSession()
  {
    TacoSignalPolicy? applied = null;
    using HttpClient http = new(new Handler(request =>
    {
      if (request.Method == HttpMethod.Put)
      {
        var command = System.Text.Json.JsonSerializer.Deserialize<TacoSignalModeCommand>(
          request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), Logistics.JsonDefaults.Options)!;
        applied = command.Policy;
      }
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(new TacoSignalMode(request.Method == HttpMethod.Put ? "automatic" : "manual", true)
        { AdaptiveSupported = true, Policy = applied }, options: Logistics.JsonDefaults.Options)
      };
    }));
    var options = new Logistics.LogisticsOptions
    {
      TacoApiBaseUrl = "http://test-provider:8080",
      ConnectionFile = Path.Combine(Path.GetTempPath(), "streetflow-policy", Guid.NewGuid().ToString(), "connection.json")
    };
    var connections = new Logistics.ConnectionStore(options);
    var client = new Logistics.TacoApiClient(http, connections, new Logistics.TacoMapCache());
    var first = new Logistics.SignalAutomation(connections, options);
    var policy = new TacoSignalPolicy(Multiplier: .6);
    await first.SetAsync("old", new TacoSignalModeCommand("automatic", "world", "revision") { Policy = policy }, client, CancellationToken.None);
    applied = null;
    var restarted = new Logistics.SignalAutomation(connections, options);
    await restarted.DiscoverAsync(client, new TacoSession("next", "world", "revision", "project", "paused"), CancellationToken.None);
    Assert.Equal(policy, applied);
  }

  private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
  }
}

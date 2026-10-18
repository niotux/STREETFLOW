using System.Text.Json;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Logistics;

// One bounded session identity. A manual choice lasts for this loaded session;
// a new simulation gets its own automatic timing, even with the browser closed.
public sealed class SignalAutomation
{
  private readonly ConnectionStore connections;
  private readonly string? settingsFile;
  private readonly SemaphoreSlim gate = new(1, 1);
  private (string Provider, string Id, string World, string Revision)? initialized;
  private TacoSignalPolicy policy = new();

  public SignalAutomation(ConnectionStore connections, LogisticsOptions? options = null)
  {
    this.connections = connections;
    settingsFile = options is null ? null : options.ConnectionFile + ".signals.json";
    if (settingsFile is not null && File.Exists(settingsFile))
      policy = JsonSerializer.Deserialize<TacoSignalPolicy>(File.ReadAllText(settingsFile), JsonDefaults.Options) ?? new();
  }

  public async Task DiscoverAsync(TacoApiClient client, TacoSession session, CancellationToken token)
  {
    if (session.Id is null || session.WorldId is null || session.Revision is null) return;
    await gate.WaitAsync(token);
    try
    {
      var key = (connections.Get().BaseUrl, session.Id, session.WorldId, session.Revision);
      if (initialized == key) return;
      TacoSignalMode mode = await client.SignalModeAsync(session.Id, token);
      if (connections.Get().BaseUrl != key.Item1) return;
      if (mode.AutomaticSupported && (mode.Mode != "automatic" || mode.AdaptiveSupported && mode.Policy != policy))
      {
        await client.SetSignalModeAsync(session.Id, new TacoSignalModeCommand("automatic", session.WorldId, session.Revision) { Policy = policy }, token);
      }
      initialized = key;
    }
    finally { gate.Release(); }
  }

  public async Task<TacoSignalMode> SetAsync(string id, TacoSignalModeCommand request, TacoApiClient client, CancellationToken token)
  {
    await gate.WaitAsync(token);
    try
    {
      string provider = connections.Get().BaseUrl;
      TacoSignalMode mode = await client.SetSignalModeAsync(id, request, token);
      if (request.Policy is not null)
      {
        policy = request.Policy;
        if (settingsFile is not null)
        {
          Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
          string temporary = settingsFile + ".tmp";
          await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(policy, JsonDefaults.Options), CancellationToken.None);
          File.Move(temporary, settingsFile, true);
        }
      }
      initialized = (provider, id, request.WorldId, request.Revision);
      return mode;
    }
    finally { gate.Release(); }
  }
}

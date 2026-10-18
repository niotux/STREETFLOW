using System.Net.Http.Json;
using System.Text.Json;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Logistics;

public sealed class TacoApiClient
{
  private readonly HttpClient client;
  private readonly ConnectionStore connections;
  private readonly TacoMapCache maps;

  public TacoApiClient(HttpClient client, ConnectionStore connections, TacoMapCache maps)
  {
    this.client = client;
    this.connections = connections;
    this.maps = maps;
  }

  public Task<TacoSession> SessionAsync(CancellationToken token) => GetAsync<TacoSession>("/api/v1/taco/session", token);
  public Task<TacoDeviceState[]> DevicesAsync(string id, CancellationToken token) => GetAsync<TacoDeviceState[]>(SessionPath(id) + "/devices", token);

  public async Task<TacoSignalMode> SignalModeAsync(string id, CancellationToken token)
  {
    try
    {
      return await GetAsync<TacoSignalMode>(SessionPath(id) + "/signal-mode", token);
    }
    catch (ApiException exception) when (exception.StatusCode is 404 or 501)
    {
      return new TacoSignalMode("unavailable", false);
    }
  }

  public async Task<TacoSignalMode> SetSignalModeAsync(string id, TacoSignalModeCommand command, CancellationToken token)
  {
    if (command.Policy?.Multiplier is double k && (!double.IsFinite(k) || k < .2 || k > 1.8))
      throw new ApiException(400, "invalid_signal_multiplier", "Multiplier k must be between 0.2 and 1.8, or null for automatic demand weighting");
    if (command.Mode is not "automatic" and not "manual" || string.IsNullOrWhiteSpace(command.WorldId) || string.IsNullOrWhiteSpace(command.Revision))
    {
      throw new ApiException(400, "invalid_signal_mode", "mode must be automatic or manual; world_id and revision are required");
    }
    using HttpRequestMessage request = new(HttpMethod.Put, Address(SessionPath(id) + "/signal-mode"));
    request.Content = JsonContent.Create(command, options: JsonDefaults.Options);
    return await SendAsync<TacoSignalMode>(request, token);
  }
  public async Task<byte[]> QueuesAsync(string id, CancellationToken token)
  {
    using HttpRequestMessage request = new(HttpMethod.Get, Address(SessionPath(id) + "/queues"));
    return await SendAsync(request, token, async (content, cancel) =>
    {
      byte[] frame = await content.ReadAsByteArrayAsync(cancel);
      try
      {
        TacoQueueFrame.Decode(frame);
      }
      catch (FormatException)
      {
        throw new ApiException(502, "invalid_taco_response", "The connected server did not return a taco queue frame");
      }
      return frame;
    });
  }

  public Task<TacoTraffic> TrafficAsync(string id, CancellationToken token) => GetAsync<TacoTraffic>(SessionPath(id) + "/traffic", token);

  public async Task<TacoMap> MapAsync(string id, CancellationToken token)
  {
    string provider = connections.Get().BaseUrl;
    TacoSession session = await SessionAsync(token);
    if (connections.Get().BaseUrl != provider || session.Id != id || session.WorldId is null || session.Revision is null)
    {
      throw new ApiException(409, "changed_session", "The provider's active session changed; refresh the connection");
    }

    await maps.Gate.WaitAsync(token);
    try
    {
      TacoMap? cached = maps.Read(provider, session.WorldId, session.Revision);
      if (cached is not null)
      {
        return cached;
      }

      TacoMap map = await GetAsync<TacoMap>(SessionPath(id) + "/map", token);
      if (connections.Get().BaseUrl != provider || map.WorldId != session.WorldId || map.Revision != session.Revision)
      {
        throw new ApiException(409, "changed_world", "The provider or world changed while loading its map; refresh the connection");
      }

      maps.Store(provider, map);
      return map;
    }
    finally
    {
      maps.Gate.Release();
    }
  }

  public async Task<TacoDeviceState> ControlAsync(string id, string deviceId, TacoDeviceCommand command, CancellationToken token)
  {
    ValidateCommand(command);
    using HttpRequestMessage request = new(HttpMethod.Put, Address(SessionPath(id) + "/devices/" + Uri.EscapeDataString(deviceId)));
    request.Content = JsonContent.Create(command, options: JsonDefaults.Options);
    return await SendAsync<TacoDeviceState>(request, token);
  }

  private async Task<T> GetAsync<T>(string path, CancellationToken token)
  {
    using HttpRequestMessage request = new(HttpMethod.Get, Address(path));
    return await SendAsync<T>(request, token);
  }

  private Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken token) =>
    SendAsync(request, token, async (content, cancel) =>
      await content.ReadFromJsonAsync<T>(JsonDefaults.Options, cancel) ?? throw new ApiException(502, "invalid_taco_response", "taco returned an empty response"));

  private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken token, Func<HttpContent, CancellationToken, Task<T>> read)
  {
    try
    {
      using HttpResponseMessage response = await client.SendAsync(request, token);
      if (!response.IsSuccessStatusCode)
      {
        ApiError? error = null;
        try
        {
          error = await response.Content.ReadFromJsonAsync<ApiError>(JsonDefaults.Options, token);
        }
        catch (JsonException)
        {
          // A provider may return a non-JSON error.
        }

        throw new ApiException((int)response.StatusCode, error?.Code ?? "taco_error", error?.Message ?? $"taco returned HTTP {(int)response.StatusCode}");
      }

      return await read(response.Content, token);
    }
    catch (HttpRequestException)
    {
      throw new ApiException(502, "taco_unavailable", "Cannot reach taco. Check the server address and port in Connection.");
    }
    catch (OperationCanceledException) when (!token.IsCancellationRequested)
    {
      throw new ApiException(504, "taco_timeout", "taco did not respond before the request timeout");
    }
    catch (JsonException)
    {
      throw new ApiException(502, "invalid_taco_response", "The connected server did not return the expected taco JSON");
    }
  }

  private string Address(string path) => connections.Get().BaseUrl + path;
  private static string SessionPath(string id) => "/api/v1/taco/sessions/" + Uri.EscapeDataString(id);

  private static void ValidateCommand(TacoDeviceCommand command)
  {
    bool valid = command.Action == "on" || command.Action == "off" || command.Action == "red" || command.Action == "green" || command.Action == "yellow" || command.Action == "red-yellow";
    if (!valid || string.IsNullOrWhiteSpace(command.WorldId) || string.IsNullOrWhiteSpace(command.Revision))
    {
      throw new ApiException(400, "invalid_device_command", "A device command requires a supported action, world_id, and revision");
    }
  }
}

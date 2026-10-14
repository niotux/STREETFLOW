using Streetflow.Traffic.Contracts;

namespace Streetflow.Logistics;

public sealed class WorldDiscoveryService(IServiceScopeFactory scopes, ConnectionStore connections, LogisticsOptions options, ILogger<WorldDiscoveryService> logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (options.DiscoveryIntervalMilliseconds <= 0)
    {
      throw new InvalidOperationException("World discovery interval must be positive");
    }

    TlcSession? previous = null;
    string? previousProvider = null;
    string? lastFailure = null;
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using IServiceScope scope = scopes.CreateScope();
        TlcApiClient client = scope.ServiceProvider.GetRequiredService<TlcApiClient>();
        string provider = connections.Get().BaseUrl;
        TlcSession current = await client.SessionAsync(stoppingToken);
        bool changed = previousProvider != provider || previous?.WorldId != current.WorldId || previous?.Revision != current.Revision;
        if (current.Id is not null && changed)
        {
          await client.MapAsync(current.Id, stoppingToken);
        }

        previous = current;
        previousProvider = provider;
        lastFailure = null;
      }
      catch (ApiException exception)
      {
        if (lastFailure != exception.Code)
        {
          logger.LogWarning("World discovery unavailable: {Reason}", exception.Message);
          lastFailure = exception.Code;
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        return;
      }

      try
      {
        await Task.Delay(options.DiscoveryIntervalMilliseconds, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        return;
      }
    }
  }
}

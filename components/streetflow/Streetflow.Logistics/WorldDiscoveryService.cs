using Streetflow.Traffic.Contracts;

namespace Streetflow.Logistics;

public sealed class WorldDiscoveryService(IServiceScopeFactory scopes, ConnectionStore connections, SignalAutomation automation, LogisticsOptions options, ILogger<WorldDiscoveryService> logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (options.DiscoveryIntervalMilliseconds <= 0)
    {
      throw new InvalidOperationException("World discovery interval must be positive");
    }

    TacoSession? previous = null;
    string? previousProvider = null;
    string? lastFailure = null;
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using IServiceScope scope = scopes.CreateScope();
        TacoApiClient client = scope.ServiceProvider.GetRequiredService<TacoApiClient>();
        string provider = connections.Get().BaseUrl;
        TacoSession current = await client.SessionAsync(stoppingToken);
        bool changed = previousProvider != provider || previous?.WorldId != current.WorldId || previous?.Revision != current.Revision;
        if (current.Id is not null && changed)
        {
          await client.MapAsync(current.Id, stoppingToken);
        }

        await automation.DiscoverAsync(client, current, stoppingToken);

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

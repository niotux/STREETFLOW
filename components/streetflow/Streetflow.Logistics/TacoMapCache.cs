using Streetflow.Traffic.Contracts;

namespace Streetflow.Logistics;

// One bounded cached world, shared by typed HTTP clients and keyed by provider and revision.
public sealed class TacoMapCache
{
  private string? provider;
  private TacoMap? map;
  public SemaphoreSlim Gate { get; } = new(1, 1);

  public TacoMap? Read(string address, string worldId, string revision)
  {
    if (provider == address && map is not null && map.WorldId == worldId && map.Revision == revision)
    {
      return map;
    }

    return null;
  }

  public void Store(string address, TacoMap value)
  {
    provider = address;
    map = value;
  }
}

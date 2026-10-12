namespace Streetflow.Simulator.Runtime;

public sealed class EngineOwnership
{
  private readonly object gate = new();
  private string? owner;

  public void Acquire(string id)
  {
    lock (gate)
    {
      if (owner is not null)
      {
        throw new ApiException(409, "active_run", "Stop the active simulation before starting another");
      }

      owner = id;
    }
  }

  public void Release(string id)
  {
    lock (gate)
    {
      if (owner == id)
      {
        owner = null;
      }
    }
  }
}

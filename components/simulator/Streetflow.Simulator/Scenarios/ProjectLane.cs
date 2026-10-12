using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectLane(LaneMap Map, int Index, string[]? Allow, string[] Disallow)
{
  public bool Permits(string vehicleClass)
  {
    if (vehicleClass == "ignoring")
    {
      return true;
    }

    bool allowed = Allow is null;
    if (Allow is not null)
    {
      allowed = ContainsPermission(Allow, vehicleClass);
    }

    return allowed && !ContainsPermission(Disallow, vehicleClass);
  }

  private static bool ContainsPermission(string[] permissions, string vehicleClass)
  {
    foreach (string permission in permissions)
    {
      if (permission == "all" || permission == vehicleClass)
      {
        return true;
      }
    }

    return false;
  }
}

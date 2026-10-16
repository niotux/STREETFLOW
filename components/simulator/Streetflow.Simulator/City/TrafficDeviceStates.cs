using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

public static class TrafficDeviceStates
{
  public static TacoDeviceState[] Read(CityProject project, SignalState[] signals)
  {
    if (project.TrafficLights is null)
    {
      return [];
    }

    TacoDeviceState[] result = new TacoDeviceState[project.TrafficLights.Devices.Length];
    for (int index = 0; index < result.Length; index++)
    {
      ProjectTrafficDevice device = project.TrafficLights.Devices[index];
      string state = "unknown";
      foreach (SignalState signal in signals)
      {
        if (signal.Id != device.SignalId)
        {
          continue;
        }

        state = ReadState(device, signal.State);
        break;
      }

      result[index] = new TacoDeviceState(device.Id, state);
    }

    return result;
  }

  private static string ReadState(ProjectTrafficDevice device, string state)
  {
    string? result = null;
    foreach (int link in device.LinkIndices)
    {
      if (link >= state.Length)
      {
        return "unknown";
      }

      string color = state[link] switch
      {
        'r' => "red",
        'u' => "red-yellow",
        'y' or 'Y' => "yellow",
        'g' or 'G' or 's' => "green",
        'o' or 'O' => "off",
        _ => "unknown"
      };
      if (result is not null && result != color)
      {
        return "mixed";
      }

      result = color;
    }

    return result ?? "unknown";
  }
}

using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Scenarios;

public static class TrafficDeviceMapping
{
  public static TacoDevice[] Read(CityProject project)
  {
    if (project.TrafficLights is null)
    {
      return [];
    }

    TacoDevice[] devices = new TacoDevice[project.TrafficLights.Devices.Length];
    for (int index = 0; index < devices.Length; index++)
    {
      ProjectTrafficDevice device = project.TrafficLights.Devices[index];
      devices[index] = new TacoDevice(device.Id, device.Label, device.X, device.Y, device.LaneIds);
    }

    return devices;
  }
}

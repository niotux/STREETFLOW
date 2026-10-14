using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Scenarios;

public static class TrafficDeviceMapping
{
  public static TlcDevice[] Read(CityProject project)
  {
    if (project.TrafficLights is null)
    {
      return [];
    }

    TlcDevice[] devices = new TlcDevice[project.TrafficLights.Devices.Length];
    for (int index = 0; index < devices.Length; index++)
    {
      ProjectTrafficDevice device = project.TrafficLights.Devices[index];
      devices[index] = new TlcDevice(device.Id, device.Label, device.X, device.Y, device.LaneIds);
    }

    return devices;
  }
}

using System.Net.Http.Json;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Tests;

public sealed class TacoQueueTests
{
  [Fact]
  public void QueueFramesRoundTrip()
  {
    int?[][] devices = [[0, 3, null], [], [65534]];
    byte[] frame = TacoQueueFrame.Encode(42, 12.5, true, devices);
    (long sequence, double time, bool available, int?[][] decoded) = TacoQueueFrame.Decode(frame);
    Assert.Equal(42, sequence);
    Assert.Equal(12.5, time);
    Assert.True(available);
    Assert.Equal(devices, decoded);
    Assert.Equal(23 + 1 + 6 + 1 + 1 + 2, frame.Length);
    Assert.Throws<FormatException>(() => TacoQueueFrame.Decode(frame.AsSpan(0, frame.Length - 1)));
  }

  [Fact]
  public void ApproachCountsFollowShortPiecesUpstreamAndIncludeMovingCars()
  {
    CityProject project = CityProjectLoader.Load(Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "podu-ros-neural"));
    QueuePaths paths = new(project);
    Dictionary<string, LaneMap> lanes = project.Lanes.ToDictionary(lane => lane.Id);
    // light-05's approach lane is under a metre long; its queue lives on the lanes before it.
    string stopLine = project.TrafficDevices.Single(device => device.Id == "light-05").LaneIds[0];
    Assert.True(lanes[stopLine].Length < 1);
    Assert.True(paths.Upstream.ContainsKey(stopLine));

    (string Lane, double Position) At(double distance)
    {
      string lane = stopLine;
      while (distance > lanes[lane].Length)
      {
        distance -= lanes[lane].Length;
        lane = paths.Upstream[lane];
      }
      return (lane, lanes[lane].Length - distance);
    }
    Dictionary<string, List<VehicleState>> Vehicles(params (double Distance, double Speed)[] cars)
    {
      Dictionary<string, List<VehicleState>> result = [];
      for (int index = 0; index < cars.Length; index++)
      {
        (string lane, double position) = At(cars[index].Distance);
        if (!result.TryGetValue(lane, out List<VehicleState>? list))
        {
          result[lane] = list = [];
        }
        list.Add(new VehicleState($"car-{index}", lane, position, 0, 0, cars[index].Speed, 0, lanes[lane].EdgeId));
      }
      return result;
    }

    Assert.Equal(3, paths.Count(stopLine, Vehicles((3.5, 0), (11, 0.5), (18.5, 0))));
    Assert.Equal(3, paths.Count(stopLine, Vehicles((3.5, 10), (11, 0), (18.5, 13))));
    Assert.Equal(2, paths.Count(stopLine, Vehicles((3.5, 0), (40, 12))));
    Assert.Equal(0, paths.Count(stopLine, Vehicles()));
  }

  [Fact]
  public async Task BinaryFrameAndJsonTrafficReportTheSameApproachCounts()
  {
    using PhysicalDevicesFixture fixture = new("podu-ros-neural");
    await using BackendFactory factory = new();
    using HttpClient client = factory.CreateClient();
    await factory.AddProjectAsync(client, fixture.Path);
    HttpResponseMessage created = await client.PostAsJsonAsync($"/api/v1/projects/{fixture.Id}/runs", new CreateCityRunRequest(42), JsonDefaults.Options);
    created.EnsureSuccessStatusCode();
    CityRunStatus run = (await created.Content.ReadFromJsonAsync<CityRunStatus>(JsonDefaults.Options))!;
    TacoSession session = (await client.GetFromJsonAsync<TacoSession>("/api/v1/taco/session", JsonDefaults.Options))!;
    TacoMap map = (await client.GetFromJsonAsync<TacoMap>($"/api/v1/taco/sessions/{run.Id}/map", JsonDefaults.Options))!;

    CityProject project = CityProjectLoader.Load(fixture.Path);
    HashSet<string> pedestrian = project.Lanes.Where(lane => lane.RenderKind is "sidewalk" or "crossing" or "walkingarea").Select(lane => lane.Id).ToHashSet();
    Assert.NotEmpty(pedestrian);
    Assert.DoesNotContain(map.Lanes, lane => pedestrian.Contains(lane.Id));
    Assert.Contains(map.Lanes, lane => lane.UpstreamLaneId is not null);

    foreach (TacoDevice device in map.Devices)
    {
      TacoDeviceCommand command = new("red", session.WorldId!, session.Revision!);
      (await client.PutAsJsonAsync($"/api/v1/taco/sessions/{run.Id}/devices/{device.Id}", command, JsonDefaults.Options)).EnsureSuccessStatusCode();
    }
    (await client.PostAsJsonAsync($"/api/v1/city-runs/{run.Id}/step", new CityRunCommand(600), JsonDefaults.Options)).EnsureSuccessStatusCode();

    HttpResponseMessage response = await client.GetAsync($"/api/v1/taco/sessions/{run.Id}/queues");
    response.EnsureSuccessStatusCode();
    Assert.Equal(TacoQueueFrame.ContentType, response.Content.Headers.ContentType?.MediaType);
    (_, _, bool available, int?[][] queues) = TacoQueueFrame.Decode(await response.Content.ReadAsByteArrayAsync());
    Assert.True(available);
    Assert.Equal(map.Devices.Length, queues.Length);
    TacoTraffic traffic = (await client.GetFromJsonAsync<TacoTraffic>($"/api/v1/taco/sessions/{run.Id}/traffic", JsonDefaults.Options))!;
    Assert.DoesNotContain(traffic.Lanes, lane => pedestrian.Contains(lane.LaneId));
    for (int device = 0; device < queues.Length; device++)
    {
      Assert.Equal(map.Devices[device].LaneIds.Length, queues[device].Length);
      for (int lane = 0; lane < queues[device].Length; lane++)
      {
        TacoLaneTraffic measured = Assert.Single(traffic.Lanes, item => item.LaneId == map.Devices[device].LaneIds[lane]);
        Assert.Equal(measured.QueuedVehicleCount, queues[device][lane]);
      }
    }
    Assert.True(queues.Sum(lanes => lanes.Sum(count => count ?? 0)) > 0, "Approaches should hold at least one vehicle");

    (await client.PostAsJsonAsync($"/api/v1/city-runs/{run.Id}/stop", new CityRunCommand(), JsonDefaults.Options)).EnsureSuccessStatusCode();
  }
}

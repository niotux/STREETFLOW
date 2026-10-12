using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Tests;

public sealed class ObservationTests
{
  private static Scenario Fixture(params Camera[] cameras)
  {
    return new()
    {
      Directory = "unused",
      Manifest = new("test", "1", "Test", "net-1", "cameras-1", "synthetic", "net", "routes", "synthetic"),
      Lanes = [],
      Junctions = [],
      Trips = [],
      Cameras = cameras
    };
  }

  private static Camera Camera(string id = "camera")
  {
    return new(id, "junction", [new("approach", "lane", 0, 100, 50)]);
  }

  private static VehicleState Car(double position, double speed = 10, string lane = "lane")
  {
    return new("car", lane, position, position, 0, speed, 0, "edge");
  }

  private static EngineSnapshot Step(double time, params VehicleState[] cars)
  {
    return new(time, cars, [], [], [], [], cars.Length);
  }

  [Fact]
  public void CrossingAtWindowEndBelongsToFollowingWindow()
  {
    ObservationBuilder builder = new ObservationBuilder(Fixture(Camera()), "run", 5);
    Assert.Empty(builder.Advance(Step(4, Car(40))));
    TrafficObservation first = Assert.Single(builder.Advance(Step(5, Car(50))));
    Assert.Equal(0, first.Lanes[0].CrossedVehicleCount);
    TrafficObservation second = Assert.Single(builder.Advance(Step(10, Car(100))));
    Assert.Equal(1, second.Lanes[0].CrossedVehicleCount);
    Assert.Equal(2, second.Sequence);
    Assert.Equal(5, second.Window.Start);
  }

  [Fact]
  public void CountsUseCoverageAndStoppedDurationRatherThanAllTraffic()
  {
    ObservationBuilder builder = new ObservationBuilder(Fixture(Camera()), "run", 5);
    builder.Advance(Step(4, Car(25, 0)));
    VehicleState outside = new VehicleState("outside", "lane", 120, 120, 0, 0, 0, "edge");
    TrafficObservation frame = Assert.Single(builder.Advance(Step(5, Car(25, 0), outside)));
    LaneObservation lane = Assert.Single(frame.Lanes);
    Assert.Equal(1, lane.VisibleVehicleCount);
    Assert.Equal(1, lane.QueuedVehicleCount);
    Assert.Equal(0, lane.CrossedVehicleCount);
    Assert.Equal(0, lane.MeanSpeedMps);
  }

  [Fact]
  public void EmptyLaneHasMeasuredZeroCountsAndNoMeanSpeed()
  {
    TrafficObservation frame = Assert.Single(new ObservationBuilder(Fixture(Camera()), "run", 5).Advance(Step(5)));
    Assert.Equal(0, frame.Lanes[0].VisibleVehicleCount);
    Assert.Null(frame.Lanes[0].MeanSpeedMps);
  }

  [Fact]
  public void SharedLinesAndRepeatedPassagesAreCountedOncePerCameraAndPassage()
  {
    ObservationBuilder builder = new ObservationBuilder(Fixture(Camera("z"), Camera("a")), "run", 5);
    builder.Advance(Step(1, Car(40)));
    builder.Advance(Step(2, Car(60)));
    builder.Advance(Step(3, Car(40)));
    TrafficObservation[] frames = builder.Advance(Step(5, Car(60)));
    Assert.Equal(2, frames.Length);
    Assert.Equal("a", frames[0].DetectorId);
    Assert.Equal("z", frames[1].DetectorId);
    for (int index = 0; index < frames.Length; index++)
    {
      Assert.Equal(index + 1, frames[index].Sequence);
      Assert.Equal(2, frames[index].Lanes[0].CrossedVehicleCount);
    }
  }

  [Fact]
  public void LaneChangeDoesNotCreateAFictitiousLongitudinalPassage()
  {
    ObservationBuilder builder = new ObservationBuilder(Fixture(Camera()), "run", 5);
    builder.Advance(Step(1, Car(40, lane: "other")));
    TrafficObservation frame = Assert.Single(builder.Advance(Step(5, Car(60))));
    Assert.Equal(0, frame.Lanes[0].CrossedVehicleCount);
  }

  [Fact]
  public void NativeLineEventCountsACarThatAlreadyLeftTheSnapshot()
  {
    ObservationBuilder builder = new ObservationBuilder(Fixture(Camera()), "run", 5);
    TrafficObservation frame = Assert.Single(builder.Advance(new(5, [], [], [], ["departed-car"], [], 0, [new("departed-car", "lane", 50, 4.5)])));
    Assert.Equal(0, frame.Lanes[0].VisibleVehicleCount);
    Assert.Equal(1, frame.Lanes[0].CrossedVehicleCount);
  }
}

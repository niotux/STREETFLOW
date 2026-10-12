using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Tests;

public sealed class PassageTests
{
  [Fact]
  public void NativeEventsMeasureBothCheckpointsEvenWithoutVehicleSnapshots()
  {
    PassageRecorder recorder = new PassageRecorder(new([new("A", "short-a", 5), new("B", "short-b", 5)], [new("A-B", "A", "B")]));
    recorder.Advance(new(5, [], [], [], ["car"], [], 0, [new("car", "short-a", 5, 1.25), new("car", "short-b", 5, 2.75)]));
    Passage passage = Assert.Single(recorder.Records());
    Assert.Equal("completed", passage.Status);
    Assert.Equal(1.5, passage.DurationSeconds);
  }

  [Fact]
  public void FirstAThenNextBDefinesDurationAndLaterPassagesArePreserved()
  {
    PassageRecorder recorder = new PassageRecorder(new([new("A", "lane", 20), new("B", "lane", 80)], [new("A-B", "A", "B")]));
    EngineSnapshot Step(double time, double position) => new(time, [new("car", "lane", position, position, 0, 10, 0, "edge")], [], [], [], [], 1);
    recorder.Advance(Step(1, 10));
    recorder.Advance(Step(2, 30));
    Passage incomplete = Assert.Single(recorder.Records());
    Assert.Equal("incomplete", incomplete.Status);
    Assert.Null(incomplete.DurationSeconds);
    recorder.Advance(Step(3, 90));
    Passage first = Assert.Single(recorder.Records());
    Assert.Equal("completed", first.Status);
    Assert.Equal(4.0 / 3, first.DurationSeconds!.Value, 6);
    recorder.Advance(Step(4, 10));
    recorder.Advance(Step(5, 30));
    recorder.Advance(Step(6, 90));
    Assert.Equal(2, recorder.Records().Length);
  }
}

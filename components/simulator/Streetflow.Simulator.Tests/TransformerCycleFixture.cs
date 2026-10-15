using System.Text.Json.Nodes;

namespace Streetflow.Simulator.Tests;

internal sealed class TransformerCycleFixture : IDisposable
{
  private readonly TransformerProjectFixture fixture = new();
  public string Path => fixture.Path;

  public TransformerCycleFixture()
  {
    fixture.Change("experiment.json", manifest => manifest["traffic_lights_file"] = null);
    File.WriteAllText(System.IO.Path.Combine(Path, "network.net.xml"), """
      <net>
        <edge id="a" from="one" to="two"><lane id="a_0" index="0" speed="10" length="100" shape="0,0 100,0"/></edge>
        <edge id="b" from="two" to="one"><lane id="b_0" index="0" speed="10" length="100" shape="100,0 0,0"/></edge>
        <edge id="goal" from="two" to="three"><lane id="goal_0" index="0" speed="10" length="100" shape="100,0 200,0"/></edge>
        <edge id="wrong" from="one" to="four"><lane id="wrong_0" index="0" speed="10" length="100" shape="0,0 -100,0"/></edge>
        <junction id="one" type="priority" x="0" y="0" shape="0,0 1,0"/>
        <junction id="two" type="priority" x="100" y="0" shape="100,0 101,0"/>
        <junction id="three" type="dead_end" x="200" y="0" shape="200,0 201,0"/>
        <junction id="four" type="dead_end" x="-100" y="0" shape="-100,0 -101,0"/>
        <connection from="a" to="b" fromLane="0" toLane="0" dir="t"/>
        <connection from="a" to="goal" fromLane="0" toLane="0" dir="s"/>
        <connection from="b" to="a" fromLane="0" toLane="0" dir="t"/>
        <connection from="b" to="wrong" fromLane="0" toLane="0" dir="s"/>
      </net>
      """);
    File.WriteAllText(System.IO.Path.Combine(Path, "terminals.json"), """
      [
        {"id":"entry","lane_id":"a_0","role":"entry","position_m":0},
        {"id":"goal-exit","lane_id":"goal_0","role":"exit","position_m":100},
        {"id":"wrong-exit","lane_id":"wrong_0","role":"exit","position_m":100}
      ]
      """);
    fixture.Change("journey-model.json", model => model["axes"] = JsonNode.Parse("""
      {
        "period":[{"start_seconds":0,"end_seconds":86400}],
        "origin":["entry"],
        "destination":[{"id":"goal","terminal_ids":["goal-exit"]},{"id":"wrong","terminal_ids":["wrong-exit"]}],
        "category":["default"],
        "vehicle_type":[{"id":"car","vehicle_class":"passenger"}],
        "movement":[
          {"incoming_edge_id":"a","outgoing_edge_id":"b"},
          {"incoming_edge_id":"a","outgoing_edge_id":"goal"},
          {"incoming_edge_id":"b","outgoing_edge_id":"a"},
          {"incoming_edge_id":"b","outgoing_edge_id":"wrong"}
        ]
      }
      """));
    fixture.Change("weights.json", weights => weights["tensors"] = JsonNode.Parse("""
      {
        "arrival_rates":{"dtype":"float64","axis_order":["period","origin"],"shape":[1,1],"values":[3600]},
        "journey_logits":{"dtype":"float64","axis_order":["period","origin","destination","category","vehicle_type"],"shape":[1,1,2,1,1],"values":[0,-20]},
        "movement_logits":{"dtype":"float64","axis_order":["period","origin","destination","category","vehicle_type","movement"],"shape":[1,1,2,1,1,4],"values":[0,0,0,0,0,0,0,0]}
      }
      """));
  }

  public void Dispose() => fixture.Dispose();
}

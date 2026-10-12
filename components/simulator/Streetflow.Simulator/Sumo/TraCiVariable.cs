namespace Streetflow.Simulator.Sumo;

internal enum TraCiVariable : byte
{
  ActiveVehicleIds = 0x00,
  Speed = 0x40,
  Position = 0x42,
  Angle = 0x43,
  RoadId = 0x50,
  LaneId = 0x51,
  LanePosition = 0x56,
  TrafficLightState = 0x20,
  InductionLoopVehicleData = 0x17,
  SimulationTime = 0x66,
  DepartedVehicleIds = 0x74,
  TeleportingVehicleIds = 0x76,
  ArrivedVehicleIds = 0x7a,
  ExpectedVehicleCount = 0x7d,
  VehicleRoute = 0x54
}

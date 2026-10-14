namespace Streetflow.Simulator.Sumo;

internal enum TraCiCommandId : byte
{
  GetVersion = 0x00,
  SimulationStep = 0x02,
  Close = 0x7f,
  SubscribeVehicle = 0xd4,
  SetTrafficLight = 0xc2,
  SetRoute = 0xc6
}

namespace Streetflow.Simulator.Sumo;

internal enum TraCiValueType : byte
{
  Position = 0x01,
  Integer = 0x09,
  Double = 0x0b,
  String = 0x0c,
  StringList = 0x0e,
  Compound = 0x0f
}

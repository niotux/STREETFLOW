namespace Streetflow.Simulator.Sumo;

internal static class TraCiProtocol
{
  public const int QueryBatchSize = 100;
  public const int MaximumShortCommandLength = byte.MaxValue;
  public const int ShortCommandHeaderLength = 2;
  public const int LongCommandHeaderLength = 6;
  public const int ShortCommandLengthPrefixSize = 1;
  public const int PacketHeaderLength = 4;
  public const int ExtendedCommandLengthAdjustment = 5;
  public const int LoopVehicleFieldCount = 5;
  public const int ValueResponseOffset = 0x10;
  public const int MaximumMessageLength = 256 * 1024 * 1024;
  public const byte SuccessStatus = 0;
  public const byte ExtendedCommandLengthMarker = 0;
  public const byte VehicleSubscriptionResponse = 0xe4;
  public const double NextSimulationStepTarget = 0;
  public const double VehicleSubscriptionStartTime = 0;
  public const double VehicleSubscriptionEndTime = double.MaxValue;
}

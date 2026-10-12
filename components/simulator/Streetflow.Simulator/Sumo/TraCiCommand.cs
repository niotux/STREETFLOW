namespace Streetflow.Simulator.Sumo;

internal readonly struct TraCiCommand
{
  public byte Id { get; }
  public byte[] Body { get; }

  public TraCiCommand(byte id, byte[] body)
  {
    Id = id;
    Body = body;
  }
}

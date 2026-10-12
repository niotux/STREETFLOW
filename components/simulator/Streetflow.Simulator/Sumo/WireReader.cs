using System.Buffers.Binary;
using System.Text;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Sumo;

internal sealed class WireReader(ReadOnlyMemory<byte> bytes)
{
  private int offset;
  public byte Byte()
  {
    return Slice(sizeof(byte))[0];
  }

  public int Int()
  {
    return BinaryPrimitives.ReadInt32BigEndian(Slice(sizeof(int)));
  }

  public double Double()
  {
    return BinaryPrimitives.ReadDoubleBigEndian(Slice(sizeof(double)));
  }

  public string String()
  {
    return Encoding.UTF8.GetString(Slice(Int()));
  }

  private ReadOnlySpan<byte> Slice(int length)
  {
    return SliceMemory(length).Span;
  }

  private ReadOnlyMemory<byte> SliceMemory(int length)
  {
    if (length < 0 || length > bytes.Length - offset)
    {
      throw new InvalidDataException("Truncated TraCI message");
    }

    ReadOnlyMemory<byte> slice = bytes.Slice(offset, length);
    offset += length;
    return slice;
  }

  public WireReader Command()
  {
    byte shortLength = Byte();
    int length = shortLength == TraCiProtocol.ExtendedCommandLengthMarker ? Int() - TraCiProtocol.ExtendedCommandLengthAdjustment : shortLength - TraCiProtocol.ShortCommandLengthPrefixSize;
    return new(SliceMemory(length));
  }

  public double ReadDoubleValue()
  {
    ReadType(TraCiValueType.Double);
    return Double();
  }

  public int ReadIntegerValue()
  {
    ReadType(TraCiValueType.Integer);
    return Int();
  }

  public string ReadStringValue()
  {
    ReadType(TraCiValueType.String);
    return String();
  }

  public Point ReadPositionValue()
  {
    ReadType(TraCiValueType.Position);
    return new Point(Double(), Double());
  }

  public string[] ReadStringListValue()
  {
    ReadType(TraCiValueType.StringList);
    int count = Int();
    if (count < 0 || count > (bytes.Length - offset) / sizeof(int))
    {
      throw new InvalidDataException("Invalid string list size");
    }

    string[] values = new string[count];
    for (int index = 0; index < count; index++)
    {
      values[index] = String();
    }

    return values;
  }

  public LoopEntry[] ReadLoopEntries()
  {
    ReadType(TraCiValueType.Compound);
    int compoundLength = Int();
    if (compoundLength < 1 || compoundLength > bytes.Length - offset)
    {
      throw new InvalidDataException("Invalid compound size");
    }

    int vehicleCount = ReadIntegerValue();
    if (vehicleCount < 0 || vehicleCount > (compoundLength - 1) / TraCiProtocol.LoopVehicleFieldCount)
    {
      throw new InvalidDataException("Invalid induction-loop vehicle count");
    }

    if (compoundLength != 1 + vehicleCount * TraCiProtocol.LoopVehicleFieldCount)
    {
      throw new InvalidDataException("Invalid induction-loop vehicle data");
    }

    LoopEntry[] entries = new LoopEntry[vehicleCount];
    for (int index = 0; index < vehicleCount; index++)
    {
      string vehicleId = ReadStringValue();
      _ = ReadDoubleValue();
      double entryTime = ReadDoubleValue();
      _ = ReadDoubleValue();
      _ = ReadStringValue();
      entries[index] = new LoopEntry(vehicleId, entryTime);
    }

    return entries;
  }

  private void ReadType(TraCiValueType expected)
  {
    byte actual = Byte();
    if (actual != (byte)expected)
    {
      throw new InvalidDataException($"Unexpected TraCI value type 0x{actual:x2}; expected 0x{(byte)expected:x2}");
    }
  }
}

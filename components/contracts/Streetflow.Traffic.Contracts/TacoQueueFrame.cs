using System.Buffers.Binary;

namespace Streetflow.Traffic.Contracts;

// Binary queue frame, little-endian:
//   "TQ01"            4 bytes
//   sequence          int64
//   time              float64 (simulation seconds or Unix seconds; NaN when unknown)
//   available         uint8 (0 or 1)
//   device count      uint16, in TacoMap.Devices order
//   per device:       uint8 lane count, then one uint16 queued-vehicle count per lane in TacoDevice.LaneIds order
//                     (0xFFFF means unknown)
public static class TacoQueueFrame
{
  public const string ContentType = "application/vnd.streetflow.taco-queues";
  public const ushort Unknown = ushort.MaxValue;
  private static ReadOnlySpan<byte> Magic => "TQ01"u8;

  public static byte[] Encode(long sequence, double time, bool available, IReadOnlyList<int?[]> devices)
  {
    int size = 4 + 8 + 8 + 1 + 2;
    foreach (int?[] lanes in devices)
    {
      if (lanes.Length > byte.MaxValue)
      {
        throw new ArgumentException("A device can report at most 255 lanes", nameof(devices));
      }
      size += 1 + 2 * lanes.Length;
    }
    if (devices.Count > ushort.MaxValue)
    {
      throw new ArgumentException("A frame can report at most 65535 devices", nameof(devices));
    }

    byte[] frame = new byte[size];
    Span<byte> span = frame;
    Magic.CopyTo(span);
    BinaryPrimitives.WriteInt64LittleEndian(span[4..], sequence);
    BinaryPrimitives.WriteDoubleLittleEndian(span[12..], time);
    span[20] = available ? (byte)1 : (byte)0;
    BinaryPrimitives.WriteUInt16LittleEndian(span[21..], (ushort)devices.Count);
    int offset = 23;
    foreach (int?[] lanes in devices)
    {
      span[offset++] = (byte)lanes.Length;
      foreach (int? count in lanes)
      {
        ushort value = count is null ? Unknown : (ushort)Math.Clamp(count.Value, 0, Unknown - 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], value);
        offset += 2;
      }
    }
    return frame;
  }

  public static (long Sequence, double Time, bool Available, int?[][] Devices) Decode(ReadOnlySpan<byte> frame)
  {
    if (frame.Length < 23 || !frame[..4].SequenceEqual(Magic))
    {
      throw new FormatException("Not a TACO queue frame");
    }
    long sequence = BinaryPrimitives.ReadInt64LittleEndian(frame[4..]);
    double time = BinaryPrimitives.ReadDoubleLittleEndian(frame[12..]);
    bool available = frame[20] == 1;
    int?[][] devices = new int?[BinaryPrimitives.ReadUInt16LittleEndian(frame[21..])][];
    int offset = 23;
    for (int device = 0; device < devices.Length; device++)
    {
      if (offset >= frame.Length || offset + 1 + 2 * frame[offset] > frame.Length)
      {
        throw new FormatException("TACO queue frame is truncated");
      }
      int?[] lanes = new int?[frame[offset++]];
      for (int lane = 0; lane < lanes.Length; lane++)
      {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(frame[offset..]);
        lanes[lane] = value == Unknown ? null : value;
        offset += 2;
      }
      devices[device] = lanes;
    }
    if (offset != frame.Length)
    {
      throw new FormatException("TACO queue frame has trailing bytes");
    }
    return (sequence, time, available, devices);
  }
}

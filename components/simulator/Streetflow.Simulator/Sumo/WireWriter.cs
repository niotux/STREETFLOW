using System.Buffers.Binary;
using System.Text;

namespace Streetflow.Simulator.Sumo;

internal sealed class WireWriter
{
  private readonly MemoryStream bytes = new();
  public void Byte(byte value)
  {
    bytes.WriteByte(value);
  }

  public void Bytes(ReadOnlySpan<byte> value)
  {
    bytes.Write(value);
  }

  public void Int(int value)
  {
    Span<byte> buffer = stackalloc byte[sizeof(int)];
    BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    bytes.Write(buffer);
  }

  public void Double(double value)
  {
    Span<byte> buffer = stackalloc byte[sizeof(double)];
    BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
    bytes.Write(buffer);
  }

  public void String(string value)
  {
    byte[] buffer = Encoding.UTF8.GetBytes(value);
    Int(buffer.Length);
    Bytes(buffer);
  }

  public byte[] ToArray()
  {
    return bytes.ToArray();
  }
}

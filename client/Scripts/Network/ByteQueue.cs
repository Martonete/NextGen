using System;
using System.Text;

namespace ArgentumNextgen.Network;

/// <summary>
/// FIFO byte buffer for the 13.3 binary protocol.
/// Mirrors VB6 clsByteQueue.cls / Rust byte_queue.rs.
/// All data types are little-endian, matching VB6 memory layout.
///
/// Data types:
/// - Byte:    1 byte unsigned
/// - Boolean: 1 byte (0 = false, nonzero = true)
/// - Integer: 2 bytes LE signed (VB6 Integer = i16 / C# short)
/// - Long:    4 bytes LE signed (VB6 Long = i32 / C# int)
/// - Single:  4 bytes IEEE 754 (VB6 Single = f32 / C# float)
/// - Double:  8 bytes IEEE 754 (VB6 Double = f64 / C# double)
/// - String:  2-byte LE length prefix + N bytes Latin-1
/// </summary>
public class ByteQueue
{
    private byte[] _data;
    private int _writePos;
    private int _readPos;

    /// <summary>Create an empty ByteQueue for writing.</summary>
    public ByteQueue()
    {
        _data = new byte[256];
        _writePos = 0;
        _readPos = 0;
    }

    /// <summary>Create a ByteQueue from raw bytes for reading.</summary>
    public ByteQueue(byte[] data)
    {
        _data = data;
        _writePos = data.Length;
        _readPos = 0;
    }

    /// <summary>Create a ByteQueue from a portion of a byte array.</summary>
    public ByteQueue(byte[] data, int offset, int length)
    {
        _data = new byte[length];
        Array.Copy(data, offset, _data, 0, length);
        _writePos = length;
        _readPos = 0;
    }

    /// <summary>Number of bytes available to read.</summary>
    public int Available => _writePos - _readPos;

    /// <summary>Current read position (for save/restore).</summary>
    public int ReadPosition => _readPos;

    /// <summary>Restore read position (rollback on partial packet).</summary>
    public void RestorePosition(int pos) => _readPos = pos;

    // ── Write methods ──────────────────────────────────────────

    private void EnsureCapacity(int needed)
    {
        if (_writePos + needed > _data.Length)
        {
            int newSize = Math.Max(_data.Length * 2, _writePos + needed);
            Array.Resize(ref _data, newSize);
        }
    }

    public void WriteByte(byte val)
    {
        EnsureCapacity(1);
        _data[_writePos++] = val;
    }

    public void WriteBoolean(bool val) => WriteByte(val ? (byte)1 : (byte)0);

    public void WriteInteger(short val)
    {
        EnsureCapacity(2);
        _data[_writePos++] = (byte)(val & 0xFF);
        _data[_writePos++] = (byte)((val >> 8) & 0xFF);
    }

    public void WriteLong(int val)
    {
        EnsureCapacity(4);
        _data[_writePos++] = (byte)(val & 0xFF);
        _data[_writePos++] = (byte)((val >> 8) & 0xFF);
        _data[_writePos++] = (byte)((val >> 16) & 0xFF);
        _data[_writePos++] = (byte)((val >> 24) & 0xFF);
    }

    public void WriteSingle(float val)
    {
        byte[] bytes = BitConverter.GetBytes(val);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        EnsureCapacity(4);
        Array.Copy(bytes, 0, _data, _writePos, 4);
        _writePos += 4;
    }

    public void WriteDouble(double val)
    {
        byte[] bytes = BitConverter.GetBytes(val);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        EnsureCapacity(8);
        Array.Copy(bytes, 0, _data, _writePos, 8);
        _writePos += 8;
    }

    public void WriteString(string val)
    {
        byte[] strBytes = Encoding.Latin1.GetBytes(val);
        WriteInteger((short)strBytes.Length);
        EnsureCapacity(strBytes.Length);
        Array.Copy(strBytes, 0, _data, _writePos, strBytes.Length);
        _writePos += strBytes.Length;
    }

    // ── Read methods ───────────────────────────────────────────

    public byte ReadByte()
    {
        if (Available < 1) throw new InvalidOperationException("Not enough data");
        return _data[_readPos++];
    }

    public bool ReadBoolean() => ReadByte() != 0;

    public short ReadInteger()
    {
        if (Available < 2) throw new InvalidOperationException("Not enough data");
        short val = (short)(_data[_readPos] | (_data[_readPos + 1] << 8));
        _readPos += 2;
        return val;
    }

    public int ReadLong()
    {
        if (Available < 4) throw new InvalidOperationException("Not enough data");
        int val = _data[_readPos]
                | (_data[_readPos + 1] << 8)
                | (_data[_readPos + 2] << 16)
                | (_data[_readPos + 3] << 24);
        _readPos += 4;
        return val;
    }

    public float ReadSingle()
    {
        if (Available < 4) throw new InvalidOperationException("Not enough data");
        float val = BitConverter.ToSingle(_data, _readPos);
        _readPos += 4;
        return val;
    }

    public double ReadDouble()
    {
        if (Available < 8) throw new InvalidOperationException("Not enough data");
        double val = BitConverter.ToDouble(_data, _readPos);
        _readPos += 8;
        return val;
    }

    public string ReadString()
    {
        short len = ReadInteger();
        if (len < 0 || Available < len) throw new InvalidOperationException("Not enough data for string");
        string val = Encoding.Latin1.GetString(_data, _readPos, len);
        _readPos += len;
        return val;
    }

    // ── Utility ────────────────────────────────────────────────

    /// <summary>Get all written data as a byte array (for sending).</summary>
    public byte[] ToArray()
    {
        byte[] result = new byte[_writePos];
        Array.Copy(_data, result, _writePos);
        return result;
    }

    /// <summary>Peek at the next byte without advancing the read position.</summary>
    public byte PeekByte()
    {
        if (Available < 1) throw new InvalidOperationException("Not enough data");
        return _data[_readPos];
    }
}

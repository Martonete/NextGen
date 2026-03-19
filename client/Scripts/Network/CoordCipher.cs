namespace ArgentumNextgen.Network;

/// <summary>
/// Rolling coordinate cipher for anti-cheat protection.
///
/// Both client and server share a seed (sent at login) and maintain a
/// synchronized packet counter. Each coordinate-bearing packet (LC, RC, WLC)
/// encodes X/Y with an offset derived from the seed and counter.
/// A cheat that modifies the encoded bytes without knowing the current
/// offset will produce invalid coordinates on the server.
///
/// Algorithm: XorShift32-based PRNG seeded with (seed ^ counter).
/// Offset applied as addition with wrap (not XOR) to avoid trivial
/// known-plaintext attacks on small coordinate values.
/// </summary>
public class CoordCipher
{
    private uint _seed;
    private uint _counter;

    /// <summary>Initialize with seed from server.</summary>
    public void Init(uint seed)
    {
        _seed = seed;
        _counter = 0;
    }

    /// <summary>Advance counter and return the offset pair for this packet.</summary>
    private (ushort offsetX, ushort offsetY) NextOffset()
    {
        _counter++;
        uint state = _seed ^ _counter;

        // XorShift32 — fast, deterministic, non-trivial output
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        // Second round for Y independence
        uint stateY = state ^ (_counter * 2654435761u); // Knuth multiplicative hash
        stateY ^= stateY << 13;
        stateY ^= stateY >> 17;
        stateY ^= stateY << 5;

        return ((ushort)(state & 0xFFFF), (ushort)(stateY & 0xFFFF));
    }

    /// <summary>Encode coordinates before sending (client-side).</summary>
    public (short encodedX, short encodedY) Encode(short x, short y)
    {
        var (ox, oy) = NextOffset();
        return ((short)((ushort)x + ox), (short)((ushort)y + oy));
    }

    /// <summary>Decode coordinates after receiving (server-side).</summary>
    public (short decodedX, short decodedY) Decode(short encodedX, short encodedY)
    {
        var (ox, oy) = NextOffset();
        return ((short)((ushort)encodedX - ox), (short)((ushort)encodedY - oy));
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace ArgentumNextgen.Network;

/// <summary>
/// Async TCP client for the 13.3 binary protocol.
/// No encryption, no framing — raw binary bytes on the wire.
/// Packets are self-delimiting via their 1-byte opcode + known field sizes.
/// </summary>
public class AoTcpClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private const int MaxInboundQueueSize = 1000;
    private readonly ConcurrentQueue<byte[]> _inboundQueue = new();
    private readonly byte[] _readBuffer = new byte[8192];
    private volatile bool _connected;
    private readonly List<byte[]> _outboundQueue = new();
    private readonly object _writeLock = new();

    public bool IsConnected => _connected;
    public int PendingPackets => _inboundQueue.Count;

    /// <summary>
    /// Connect to the AO server asynchronously.
    /// </summary>
    public async Task ConnectAsync(string host, int port, int timeoutMs = 5000)
    {
        _client = new TcpClient();
        _cts = new CancellationTokenSource();

        using var connectCts = new CancellationTokenSource(timeoutMs);
        await _client.ConnectAsync(host, port, connectCts.Token);
        _client.NoDelay = true;             // Disable Nagle — send packets immediately
        _client.SendBufferSize = 65536;     // 64KB send buffer
        _client.ReceiveBufferSize = 65536;  // 64KB receive buffer
        _client.Client.SetSocketOption(
            System.Net.Sockets.SocketOptionLevel.Socket,
            System.Net.Sockets.SocketOptionName.KeepAlive,
            true);
        _stream = _client.GetStream();
        _connected = true;

        // Start reading in background
        _ = Task.Run(() => ReadLoop(_cts.Token));
    }

    /// <summary>
    /// Send raw binary bytes to the server.
    /// 13.3 protocol: no encryption, no framing.
    /// </summary>
    public bool SendPacket(byte[] data)
    {
        if (!_connected || _stream == null || data.Length == 0) return false;
        lock (_writeLock)
        {
            _outboundQueue.Add(data);
        }
        return true;
    }

    /// <summary>
    /// Flush all queued outbound packets in a single TCP write.
    /// Call once per frame from _Process(), after all game logic.
    /// </summary>
    public void FlushOutbound()
    {
        if (!_connected || _stream == null) return;

        byte[][] toSend;
        lock (_writeLock)
        {
            if (_outboundQueue.Count == 0) return;
            toSend = _outboundQueue.ToArray();
            _outboundQueue.Clear();
        }

        try
        {
            int totalSize = 0;
            foreach (var pkt in toSend) totalSize += pkt.Length;

            byte[] batch = new byte[totalSize];
            int offset = 0;
            foreach (var pkt in toSend)
            {
                Buffer.BlockCopy(pkt, 0, batch, offset, pkt.Length);
                offset += pkt.Length;
            }

            _stream.Write(batch, 0, batch.Length);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TCP] Flush error: {ex.Message}");
            Disconnect();
        }
    }

    /// <summary>
    /// Dequeue all pending inbound data chunks (call from _Process).
    /// Each chunk is raw bytes from one TCP read, potentially containing
    /// multiple packets. The packet handler uses ByteQueue to parse them.
    /// </summary>
    public List<byte[]> PollPackets()
    {
        var packets = new List<byte[]>();
        while (_inboundQueue.TryDequeue(out byte[]? pkt))
        {
            if (pkt != null && pkt.Length > 0)
                packets.Add(pkt);
        }
        return packets;
    }

    /// <summary>
    /// Background read loop: raw binary reads, enqueue directly.
    /// No encryption, no framing — raw bytes on the wire.
    /// </summary>
    private async Task ReadLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int bytesRead = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, ct);
                if (bytesRead == 0)
                {
                    Disconnect();
                    return;
                }

                // Enqueue the raw bytes for the packet handler
                byte[] data = new byte[bytesRead];
                Array.Copy(_readBuffer, data, bytesRead);
                if (_inboundQueue.Count < MaxInboundQueueSize)
                    _inboundQueue.Enqueue(data);
                else
                    GD.PrintErr("[TCP] Inbound queue full — dropping packet");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TCP] Read error: {ex.Message}");
            Disconnect();
        }
    }

    public void Disconnect()
    {
        _connected = false;
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}

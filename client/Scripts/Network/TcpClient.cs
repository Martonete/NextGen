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
        _stream = _client.GetStream();
        _connected = true;

        GD.Print($"[TCP] Connected to {host}:{port}");

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

        try
        {
            _stream.Write(data, 0, data.Length);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TCP] Send error: {ex.Message}");
            return false;
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
                    GD.Print("[TCP] Server closed connection");
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
        GD.Print("[TCP] Disconnected");
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}

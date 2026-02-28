using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace TierrasSagradasAO.Network;

/// <summary>
/// Async TCP client with 0x00 packet framing.
/// Manages connection, send/receive, and packet queue.
/// </summary>
public class AoTcpClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<string> _inboundQueue = new();
    private readonly AoCipher _cipher = new();
    private readonly byte[] _readBuffer = new byte[8192];
    private readonly List<byte> _accumulator = new();
    private bool _connected;

    public bool IsConnected => _connected;
    public int PendingPackets => _inboundQueue.Count;

    /// <summary>
    /// Connect to the AO server asynchronously.
    /// </summary>
    public async Task ConnectAsync(string host, int port)
    {
        _client = new TcpClient();
        _cts = new CancellationTokenSource();

        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();
        _connected = true;

        GD.Print($"[TCP] Connected to {host}:{port}");

        // Start reading in background
        _ = Task.Run(() => ReadLoop(_cts.Token));
    }

    /// <summary>
    /// Send a plaintext packet through the encryption pipeline.
    /// plaintext → XOR cipher → hex → base64 → +0x00
    /// </summary>
    public void SendPacket(string plaintext)
    {
        if (!_connected || _stream == null) return;

        byte[] data = Encoding.Latin1.GetBytes(plaintext);
        byte[] encrypted = AoEncryption.EncryptOutbound(data, _cipher);

        // Frame with 0x00
        byte[] frame = new byte[encrypted.Length + 1];
        Array.Copy(encrypted, frame, encrypted.Length);
        frame[^1] = 0x00;

        try
        {
            _stream.Write(frame, 0, frame.Length);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TCP] Send error: {ex.Message}");
            Disconnect();
        }
    }

    /// <summary>
    /// Dequeue all pending inbound packets (call from _Process).
    /// </summary>
    public List<string> PollPackets()
    {
        var packets = new List<string>();
        while (_inboundQueue.TryDequeue(out string? pkt))
        {
            if (pkt != null)
                packets.Add(pkt);
        }
        return packets;
    }

    /// <summary>
    /// Background read loop: accumulate TCP data, split by 0x00, decrypt.
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

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = _readBuffer[i];
                    if (b == 0x00)
                    {
                        if (_accumulator.Count > 0)
                        {
                            ProcessInboundPacket(_accumulator.ToArray());
                            _accumulator.Clear();
                        }
                    }
                    else
                    {
                        _accumulator.Add(b);
                    }
                }
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

    /// <summary>
    /// Decrypt an inbound packet and enqueue it.
    /// Server outbound: hex → base64 (no XOR cipher).
    /// </summary>
    private void ProcessInboundPacket(byte[] raw)
    {
        try
        {
            byte[] decrypted = AoEncryption.DecryptInbound(raw);
            string plaintext = Encoding.Latin1.GetString(decrypted);
            _inboundQueue.Enqueue(plaintext);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TCP] Decrypt error: {ex.Message}");
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

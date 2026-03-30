use std::net::SocketAddr;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::net::TcpStream;

/// Unique identifier for a client connection (maps to VB6 ConnID).
pub type ConnectionId = u32;

/// Maximum accumulated buffer size per connection (64 KB).
/// If a client sends data faster than we can parse it and the buffer
/// exceeds this, the connection is dropped. Prevents memory exhaustion
/// from slow-parse or partial-packet flooding.
pub const MAX_RECV_BUFFER: usize = 65_536;

/// Maximum pending outgoing bytes per connection (256 KB).
/// If a slow client causes the write buffer to grow beyond this limit,
/// the connection is flagged for removal to prevent unbounded memory growth.
const MAX_PENDING_BYTES: usize = 256 * 1024;

/// Read timeout: if no data arrives within this duration, the connection
/// is considered dead and dropped. Prevents slowloris attacks where
/// connections are opened and held idle to exhaust the connection pool.
const READ_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(20 * 60); // 20 minutes AFK timeout

/// Read half of a client connection — runs in a spawned task.
///
/// 13.3 binary protocol: raw bytes over TCP, no encryption.
/// Packets are self-delimiting (known sizes from 1-byte opcode).
/// We accumulate bytes and forward them as a contiguous buffer.
pub struct ConnectionReader {
    pub id: ConnectionId,
    reader: OwnedReadHalf,
    /// Accumulation buffer for partial TCP reads.
    buffer: Vec<u8>,
}

impl ConnectionReader {
    /// Read raw bytes from the socket.
    /// Returns the accumulated buffer contents, or None on disconnect/error.
    ///
    /// Security:
    /// - 30-second read timeout (kills idle/slowloris connections)
    /// - 64KB buffer cap (prevents memory exhaustion from partial-packet flood)
    pub async fn read_packets(&mut self) -> Option<Vec<Vec<u8>>> {
        let mut buf = [0u8; 4096];

        // Apply read timeout — if the client sends nothing for 30 seconds,
        // we close the connection. This is the primary defense against slowloris.
        let read_result = tokio::time::timeout(READ_TIMEOUT, self.reader.read(&mut buf)).await;

        let n = match read_result {
            Ok(Ok(0)) => return None,           // Clean disconnect
            Ok(Ok(n)) => n,                      // Got data
            Ok(Err(e)) => {
                tracing::debug!("Read error on connection {}: {}", self.id, e);
                return None;
            }
            Err(_) => {
                // Timeout — no data received within READ_TIMEOUT
                tracing::debug!("Connection #{} idle timeout ({}s)", self.id, READ_TIMEOUT.as_secs());
                return None;
            }
        };

        // Append new data to buffer
        self.buffer.extend_from_slice(&buf[..n]);

        // Buffer overflow protection — drop connection if buffer exceeds 64KB
        if self.buffer.len() > MAX_RECV_BUFFER {
            tracing::warn!(
                "Connection #{} buffer overflow ({} bytes > {}), dropping",
                self.id, self.buffer.len(), MAX_RECV_BUFFER
            );
            return None;
        }

        // Return the entire buffer as one chunk for the dispatcher to parse.
        // The dispatcher uses ByteQueue to extract individual packets.
        if self.buffer.is_empty() {
            return Some(Vec::new());
        }

        let data = std::mem::take(&mut self.buffer);
        Some(vec![data])
    }
}

/// Write half of a client connection — held by the game loop.
///
/// Uses application-level write batching: packets are accumulated in a buffer
/// during the game tick and flushed once at the end. This reduces syscalls
/// and TCP overhead (fewer small packets) while keeping the same latency
/// since flush happens every tick (~40ms at most).
pub struct ConnectionWriter {
    pub id: ConnectionId,
    pub addr: SocketAddr,
    writer: OwnedWriteHalf,
    /// Application-level write buffer. Packets accumulate here during a tick
    /// and are flushed to the socket in one write_all() call.
    buf: Vec<u8>,
    /// Set to true when the output buffer exceeds MAX_PENDING_BYTES.
    /// The game loop should remove this connection on the next tick.
    pub closed: bool,
}

impl ConnectionWriter {
    /// Buffer raw binary bytes for sending. Data is not written to the socket
    /// until flush() is called (once per game tick).
    ///
    /// If the pending buffer exceeds MAX_PENDING_BYTES the connection is
    /// flagged as closed and the data is discarded. The game loop must check
    /// `self.closed` and remove the connection on the next tick.
    pub fn send_packet(&mut self, data: &[u8]) {
        if self.buf.len() > MAX_PENDING_BYTES {
            tracing::warn!(
                conn_id = ?self.id,
                pending = self.buf.len(),
                "output buffer overflow — dropping connection"
            );
            self.closed = true;
            return;
        }
        self.buf.extend_from_slice(data);
    }

    /// Flush the write buffer to the TCP socket. Called once per game tick.
    /// Returns Ok(()) if buffer was empty or write succeeded.
    pub async fn flush(&mut self) -> Result<(), std::io::Error> {
        if self.buf.is_empty() {
            return Ok(());
        }
        let result = self.writer.write_all(&self.buf).await;
        self.buf.clear();
        result
    }

    /// Flush buffer and then shutdown the write half.
    pub async fn shutdown(&mut self) {
        let _ = self.flush().await;
        let _ = self.writer.shutdown().await;
    }

    /// Get the remote IP address as string.
    pub fn ip(&self) -> String {
        self.addr.ip().to_string()
    }
}

/// Split a TcpStream into reader and writer halves for a connection.
pub fn split_connection(
    id: ConnectionId,
    stream: TcpStream,
    addr: SocketAddr,
) -> (ConnectionReader, ConnectionWriter) {
    // Enable TCP keepalive at the OS level — detect dead connections
    // that didn't cleanly disconnect (e.g. network outage, client crash).
    let sock_ref = socket2::SockRef::from(&stream);
    let keepalive = socket2::TcpKeepalive::new()
        .with_time(std::time::Duration::from_secs(15))
        .with_interval(std::time::Duration::from_secs(5));
    let _ = sock_ref.set_tcp_keepalive(&keepalive);
    // Disable Nagle's algorithm for low-latency game packets
    let _ = stream.set_nodelay(true);

    let (read_half, write_half) = stream.into_split();

    let reader = ConnectionReader {
        id,
        reader: read_half,
        buffer: Vec::with_capacity(2048),
    };

    let writer = ConnectionWriter {
        id,
        addr,
        writer: write_half,
        buf: Vec::with_capacity(1024),
        closed: false,
    };

    (reader, writer)
}

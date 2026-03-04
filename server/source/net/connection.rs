use std::net::SocketAddr;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::net::TcpStream;

/// Unique identifier for a client connection (maps to VB6 ConnID).
pub type ConnectionId = u32;

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
    /// Returns the accumulated buffer contents, or None on disconnect.
    ///
    /// The game loop's ByteQueue handles parsing individual packets
    /// from the raw stream (peeking opcode, reading fields, handling
    /// partial data by saving/restoring position).
    pub async fn read_packets(&mut self) -> Option<Vec<Vec<u8>>> {
        let mut buf = [0u8; 4096];

        let n = match self.reader.read(&mut buf).await {
            Ok(0) => return None,
            Ok(n) => n,
            Err(e) => {
                tracing::debug!("Read error on connection {}: {}", self.id, e);
                return None;
            }
        };

        // Append new data to buffer
        self.buffer.extend_from_slice(&buf[..n]);

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
pub struct ConnectionWriter {
    pub id: ConnectionId,
    pub addr: SocketAddr,
    writer: OwnedWriteHalf,
}

impl ConnectionWriter {
    /// Send raw binary bytes to the client.
    ///
    /// 13.3 protocol: no encryption, no framing. Raw bytes on the wire.
    /// The ByteQueue on the client side knows field sizes from the opcode.
    pub async fn send_packet(&mut self, data: &[u8]) -> Result<(), std::io::Error> {
        self.writer.write_all(data).await
    }

    /// Shutdown the write half.
    pub async fn shutdown(&mut self) {
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
    };

    (reader, writer)
}

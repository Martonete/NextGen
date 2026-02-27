use std::net::SocketAddr;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::net::TcpStream;

use crate::crypto;
use crate::crypto::aodef_converter::numero2letra;
use crate::crypto::aodef_cipher::semilla;
use super::packet_framing::PacketFramer;

/// Unique identifier for a client connection (maps to VB6 ConnID).
pub type ConnectionId = u32;

/// Read half of a client connection — runs in a spawned task.
pub struct ConnectionReader {
    pub id: ConnectionId,
    reader: OwnedReadHalf,
    framer: PacketFramer,
    /// Packet counter for key derivation (VB6: clave2, wraps at 999999)
    packet_counter: i64,
}

impl ConnectionReader {
    /// Read raw data from the socket and extract complete decrypted packets.
    /// Returns None if the connection was closed.
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

        let raw_packets = self.framer.feed(&buf[..n]);

        if raw_packets.is_empty() {
            return Some(Vec::new());
        }

        let mut decrypted_packets = Vec::with_capacity(raw_packets.len());

        for raw in raw_packets {
            // Advance counter and derive key BEFORE decryption
            // Mirrors VB6 HandleData: clave2 += 1 → Numero2Letra → Semilla → DeCodificar
            self.packet_counter += 1;
            let key = derive_key(self.packet_counter);

            // Debug logging: raw bytes before decrypt
            let raw_preview: String = raw.iter().take(40).map(|b| format!("{:02X}", b)).collect::<Vec<_>>().join(" ");
            tracing::debug!("[CRYPTO] #{} pkt_counter={} raw({} bytes): {}", self.id, self.packet_counter, raw.len(), raw_preview);

            // Decrypt: AoDefDecode(raw) → DeCodificar(result, key)
            let decrypted = crypto::decrypt_inbound(&raw, &key);

            let dec_preview = String::from_utf8_lossy(&decrypted);
            let dec_hex: String = decrypted.iter().take(40).map(|b| format!("{:02X}", b)).collect::<Vec<_>>().join(" ");
            tracing::debug!("[CRYPTO] #{} decrypted({} bytes): {} | text: '{}'", self.id, decrypted.len(), dec_hex, dec_preview.chars().take(60).collect::<String>());

            decrypted_packets.push(decrypted);

            // Wrap counter at 999999 (VB6: If AoDefResult = 999999 Then AoDefResult = 1)
            if self.packet_counter >= 999999 {
                self.packet_counter = 1;
            }
        }

        Some(decrypted_packets)
    }
}

/// Write half of a client connection — held by the game loop.
pub struct ConnectionWriter {
    pub id: ConnectionId,
    pub addr: SocketAddr,
    writer: OwnedWriteHalf,
}

impl ConnectionWriter {
    /// Send an encrypted packet to the client.
    ///
    /// Mirrors VB6: `AoDefEncode(AoDefServEncrypt(data)) + ENDC`
    pub async fn send_packet(&mut self, data: &[u8]) -> Result<(), std::io::Error> {
        let encrypted = crypto::encrypt_outbound(data);
        let framed = PacketFramer::frame_packet(&encrypted);
        self.writer.write_all(&framed).await
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
        framer: PacketFramer::new(),
        packet_counter: 0,
    };

    let writer = ConnectionWriter {
        id,
        addr,
        writer: write_half,
    };

    (reader, writer)
}

/// Derive encryption key from a packet counter.
///
/// Mirrors VB6:
/// ```vb
/// SuperClave = AodefConv.Numero2Letra(clave2, , 2, "ZiPPy", "NoPPy", 1, 0)
/// ' Remove spaces
/// SuperClave = Semilla(SuperClave)
/// ```
fn derive_key(counter: i64) -> String {
    let text = numero2letra(counter);
    let text_no_spaces: String = text.chars().filter(|c| *c != ' ').collect();
    semilla(&text_no_spaces)
}

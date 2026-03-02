use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::Arc;
use tokio::net::TcpListener;
use tokio::sync::mpsc;
use tracing::{info, warn, error};

use super::connection::{self, ConnectionId, ConnectionWriter};

/// Events emitted by the TCP server to the game loop.
pub enum ServerEvent {
    /// A new client connected. Carries the write half for sending responses.
    NewConnection(ConnectionWriter),
    /// A client sent a decrypted packet (connection_id, data).
    PacketReceived(ConnectionId, Vec<u8>),
    /// A client disconnected.
    Disconnected(ConnectionId),
}

/// TCP server that listens for incoming connections.
///
/// Mirrors VB6's listening socket setup in TCP.bas:ConfigListeningSocket.
/// Port, backlog, and buffer size are configured via server.ini in VB6.
pub struct TcpServer;

impl TcpServer {
    /// Start listening and return a channel of server events.
    ///
    /// The server:
    /// 1. Accepts TCP connections on `addr:port`
    /// 2. Splits each into reader (spawned task) and writer (sent as event)
    /// 3. Reader decrypts packets and forwards them as events
    /// 4. Game loop receives events and uses writers to send responses
    pub async fn start(
        addr: &str,
        port: u16,
        max_connections: u32,
    ) -> Result<mpsc::Receiver<ServerEvent>, std::io::Error> {
        let bind_addr = format!("{}:{}", addr, port);
        let listener = TcpListener::bind(&bind_addr).await?;
        info!("Server listening on {}", bind_addr);

        let (tx, rx) = mpsc::channel::<ServerEvent>(1024);
        let next_id = AtomicU32::new(1);
        // Track active connection count so IDs can grow unbounded
        // without hitting max_connections prematurely.
        let active_count = Arc::new(AtomicU32::new(0));

        tokio::spawn(async move {
            loop {
                match listener.accept().await {
                    Ok((stream, addr)) => {
                        let current_active = active_count.load(Ordering::Relaxed);
                        if current_active >= max_connections {
                            warn!(
                                "Max active connections ({}/{}) reached, rejecting {}",
                                current_active, max_connections, addr
                            );
                            drop(stream);
                            continue;
                        }

                        let conn_id = next_id.fetch_add(1, Ordering::Relaxed);
                        active_count.fetch_add(1, Ordering::Relaxed);

                        info!("New connection #{} from {}", conn_id, addr);

                        let (mut reader, writer) = connection::split_connection(
                            conn_id, stream, addr,
                        );

                        let event_tx = tx.clone();

                        // Send the writer half to the game loop
                        if event_tx
                            .send(ServerEvent::NewConnection(writer))
                            .await
                            .is_err()
                        {
                            active_count.fetch_sub(1, Ordering::Relaxed);
                            break; // Game loop dropped the receiver
                        }

                        // Spawn per-connection read loop
                        let read_tx = tx.clone();
                        let active_clone = Arc::clone(&active_count);
                        tokio::spawn(async move {
                            loop {
                                match reader.read_packets().await {
                                    Some(packets) => {
                                        for packet in packets {
                                            if !packet.is_empty() {
                                                if read_tx
                                                    .send(ServerEvent::PacketReceived(
                                                        conn_id, packet,
                                                    ))
                                                    .await
                                                    .is_err()
                                                {
                                                    active_clone.fetch_sub(1, Ordering::Relaxed);
                                                    return;
                                                }
                                            }
                                        }
                                    }
                                    None => {
                                        info!("Connection #{} disconnected", conn_id);
                                        active_clone.fetch_sub(1, Ordering::Relaxed);
                                        let _ = read_tx
                                            .send(ServerEvent::Disconnected(conn_id))
                                            .await;
                                        return;
                                    }
                                }
                            }
                        });
                    }
                    Err(e) => {
                        error!("Accept error: {}", e);
                    }
                }
            }
        });

        Ok(rx)
    }
}

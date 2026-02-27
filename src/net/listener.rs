use std::sync::atomic::{AtomicU32, Ordering};
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
/// Port, backlog, and buffer size are configured via Server.ini in VB6.
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

        tokio::spawn(async move {
            loop {
                match listener.accept().await {
                    Ok((stream, addr)) => {
                        let conn_id = next_id.fetch_add(1, Ordering::Relaxed);

                        if conn_id > max_connections {
                            warn!(
                                "Max connections ({}) reached, rejecting {}",
                                max_connections, addr
                            );
                            drop(stream);
                            continue;
                        }

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
                            break; // Game loop dropped the receiver
                        }

                        // Spawn per-connection read loop
                        let read_tx = tx.clone();
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
                                                    return;
                                                }
                                            }
                                        }
                                    }
                                    None => {
                                        info!("Connection #{} disconnected", conn_id);
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

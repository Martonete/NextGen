/// Packet framing for AO protocol.
///
/// Packets are delimited by a null byte (0x00 = vbNullChar = ENDC).
/// The framer accumulates data from the TCP stream and extracts
/// complete packets when a null terminator is found.
///
/// Mirrors VB6 behavior in frmMain.frm Socket1_Read event (client)
/// and the server-side data reception in TCP.bas.

/// ENDC constant — null byte packet terminator.
pub const ENDC: u8 = 0x00;

/// Maximum packet size to prevent memory exhaustion from malformed data.
const MAX_PACKET_SIZE: usize = 8192;

/// Packet framer that accumulates TCP stream data and extracts
/// null-terminated packets.
pub struct PacketFramer {
    buffer: Vec<u8>,
}

impl PacketFramer {
    pub fn new() -> Self {
        Self {
            buffer: Vec::with_capacity(2048),
        }
    }

    /// Feed raw bytes from the TCP stream into the framer.
    /// Returns a vector of complete packets (without the null terminator).
    pub fn feed(&mut self, data: &[u8]) -> Vec<Vec<u8>> {
        let mut packets = Vec::new();

        for &byte in data {
            if byte == ENDC {
                if !self.buffer.is_empty() {
                    packets.push(std::mem::take(&mut self.buffer));
                    self.buffer = Vec::with_capacity(1024);
                }
            } else {
                if self.buffer.len() < MAX_PACKET_SIZE {
                    self.buffer.push(byte);
                } else {
                    // Packet too large — discard and reset
                    tracing::warn!("Packet exceeded max size ({}), discarding", MAX_PACKET_SIZE);
                    self.buffer.clear();
                }
            }
        }

        packets
    }

    /// Check if there's any buffered (incomplete) data.
    pub fn has_pending(&self) -> bool {
        !self.buffer.is_empty()
    }

    /// Append the null terminator to a packet for sending.
    pub fn frame_packet(data: &[u8]) -> Vec<u8> {
        let mut framed = Vec::with_capacity(data.len() + 1);
        framed.extend_from_slice(data);
        framed.push(ENDC);
        framed
    }
}

impl Default for PacketFramer {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn single_packet() {
        let mut framer = PacketFramer::new();
        let packets = framer.feed(b"HELLO\x00");
        assert_eq!(packets.len(), 1);
        assert_eq!(packets[0], b"HELLO");
    }

    #[test]
    fn multiple_packets_in_one_read() {
        let mut framer = PacketFramer::new();
        let packets = framer.feed(b"PKT1\x00PKT2\x00PKT3\x00");
        assert_eq!(packets.len(), 3);
        assert_eq!(packets[0], b"PKT1");
        assert_eq!(packets[1], b"PKT2");
        assert_eq!(packets[2], b"PKT3");
    }

    #[test]
    fn split_across_reads() {
        let mut framer = PacketFramer::new();

        let p1 = framer.feed(b"HEL");
        assert!(p1.is_empty());
        assert!(framer.has_pending());

        let p2 = framer.feed(b"LO\x00");
        assert_eq!(p2.len(), 1);
        assert_eq!(p2[0], b"HELLO");
    }

    #[test]
    fn empty_between_terminators() {
        let mut framer = PacketFramer::new();
        let packets = framer.feed(b"\x00\x00PKT\x00\x00");
        assert_eq!(packets.len(), 1);
        assert_eq!(packets[0], b"PKT");
    }

    #[test]
    fn frame_packet_appends_null() {
        let framed = PacketFramer::frame_packet(b"TEST");
        assert_eq!(framed, b"TEST\x00");
    }
}

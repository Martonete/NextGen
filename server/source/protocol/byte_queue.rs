/// ByteQueue — FIFO byte buffer for the 13.3 binary protocol.
///
/// Mirrors VB6 `clsByteQueue.cls` from Argentum Online 13.3.
/// All data types are little-endian, matching VB6 memory layout.
///
/// Data types:
/// - Byte:    1 byte unsigned
/// - Boolean: 1 byte (0 = false, nonzero = true)
/// - Integer: 2 bytes LE signed (VB6 Integer = i16)
/// - Long:    4 bytes LE signed (VB6 Long = i32)
/// - Single:  4 bytes IEEE 754 (VB6 Single = f32)
/// - Double:  8 bytes IEEE 754 (VB6 Double = f64)
/// - String:  2-byte LE length prefix + N bytes ASCII

use std::io;

/// Error raised when not enough data is available to read.
const NOT_ENOUGH_DATA: &str = "Not enough data in ByteQueue";

/// A FIFO byte buffer for serializing/deserializing binary game packets.
#[derive(Debug, Clone)]
pub struct ByteQueue {
    data: Vec<u8>,
    read_pos: usize,
}

impl ByteQueue {
    /// Create an empty ByteQueue for writing.
    pub fn new() -> Self {
        Self {
            data: Vec::with_capacity(256),
            read_pos: 0,
        }
    }

    /// Create a ByteQueue from raw bytes for reading.
    pub fn from_bytes(data: &[u8]) -> Self {
        Self {
            data: data.to_vec(),
            read_pos: 0,
        }
    }

    /// Create a ByteQueue wrapping existing data (no copy for read-only).
    pub fn wrap(data: Vec<u8>) -> Self {
        Self {
            data,
            read_pos: 0,
        }
    }

    /// Number of bytes available to read.
    pub fn available(&self) -> usize {
        self.data.len() - self.read_pos
    }

    /// Whether the buffer has been fully consumed.
    pub fn is_empty(&self) -> bool {
        self.available() == 0
    }

    /// Get the underlying data as a byte slice (for sending).
    pub fn as_bytes(&self) -> &[u8] {
        &self.data
    }

    /// Consume into the underlying Vec<u8> (for sending).
    pub fn into_bytes(self) -> Vec<u8> {
        self.data
    }

    /// Save current read position (for rollback on partial packet).
    pub fn save_position(&self) -> usize {
        self.read_pos
    }

    /// Restore read position (rollback on partial packet).
    pub fn restore_position(&mut self, pos: usize) {
        self.read_pos = pos;
    }

    /// Number of bytes remaining to read (alias for available).
    pub fn remaining(&self) -> usize {
        self.available()
    }

    /// Read and return all remaining unread bytes.
    pub fn read_remaining(&mut self) -> Vec<u8> {
        let rest = self.data[self.read_pos..].to_vec();
        self.read_pos = self.data.len();
        rest
    }

    // ── Write methods ──────────────────────────────────────────

    /// Write a single byte.
    pub fn write_byte(&mut self, val: u8) {
        self.data.push(val);
    }

    /// Write a boolean as 1 byte (0 or 1).
    pub fn write_boolean(&mut self, val: bool) {
        self.data.push(if val { 1 } else { 0 });
    }

    /// Write a 16-bit signed integer (little-endian). VB6 Integer.
    pub fn write_integer(&mut self, val: i16) {
        self.data.extend_from_slice(&val.to_le_bytes());
    }

    /// Write a 32-bit signed integer (little-endian). VB6 Long.
    pub fn write_long(&mut self, val: i32) {
        self.data.extend_from_slice(&val.to_le_bytes());
    }

    /// Write a 32-bit float (IEEE 754 little-endian). VB6 Single.
    pub fn write_single(&mut self, val: f32) {
        self.data.extend_from_slice(&val.to_le_bytes());
    }

    /// Write a 64-bit float (IEEE 754 little-endian). VB6 Double.
    pub fn write_double(&mut self, val: f64) {
        self.data.extend_from_slice(&val.to_le_bytes());
    }

    /// Write a variable-length Latin-1 string (2-byte LE length prefix + data).
    /// Encodes each Unicode char as its Latin-1 byte (codepoint & 0xFF).
    /// This matches the VB6/Windows-1252 encoding used by the client.
    pub fn write_ascii_string(&mut self, val: &str) {
        let bytes: Vec<u8> = val.chars().map(|c| {
            let cp = c as u32;
            if cp <= 0xFF { cp as u8 } else { b'?' }
        }).collect();
        let len = bytes.len().min(u16::MAX as usize);
        self.write_integer(len as i16);
        self.data.extend_from_slice(&bytes[..len]);
    }

    /// Write a fixed-length Latin-1 string (no length prefix, padded/truncated).
    pub fn write_ascii_string_fixed(&mut self, val: &str, len: usize) {
        let bytes: Vec<u8> = val.chars().map(|c| {
            let cp = c as u32;
            if cp <= 0xFF { cp as u8 } else { b'?' }
        }).collect();
        if bytes.len() >= len {
            self.data.extend_from_slice(&bytes[..len]);
        } else {
            self.data.extend_from_slice(&bytes);
            // Pad with zeros
            self.data.extend(std::iter::repeat(0u8).take(len - bytes.len()));
        }
    }

    // ── Read methods ───────────────────────────────────────────

    /// Read a single byte.
    pub fn read_byte(&mut self) -> io::Result<u8> {
        if self.available() < 1 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        let val = self.data[self.read_pos];
        self.read_pos += 1;
        Ok(val)
    }

    /// Read a boolean (1 byte, 0 = false).
    pub fn read_boolean(&mut self) -> io::Result<bool> {
        Ok(self.read_byte()? != 0)
    }

    /// Read a 16-bit signed integer (little-endian).
    pub fn read_integer(&mut self) -> io::Result<i16> {
        if self.available() < 2 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        let val = i16::from_le_bytes([
            self.data[self.read_pos],
            self.data[self.read_pos + 1],
        ]);
        self.read_pos += 2;
        Ok(val)
    }

    /// Read a 32-bit signed integer (little-endian).
    pub fn read_long(&mut self) -> io::Result<i32> {
        if self.available() < 4 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        let val = i32::from_le_bytes([
            self.data[self.read_pos],
            self.data[self.read_pos + 1],
            self.data[self.read_pos + 2],
            self.data[self.read_pos + 3],
        ]);
        self.read_pos += 4;
        Ok(val)
    }

    /// Read a 32-bit float (IEEE 754 little-endian).
    pub fn read_single(&mut self) -> io::Result<f32> {
        if self.available() < 4 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        let val = f32::from_le_bytes([
            self.data[self.read_pos],
            self.data[self.read_pos + 1],
            self.data[self.read_pos + 2],
            self.data[self.read_pos + 3],
        ]);
        self.read_pos += 4;
        Ok(val)
    }

    /// Read a 64-bit float (IEEE 754 little-endian).
    pub fn read_double(&mut self) -> io::Result<f64> {
        if self.available() < 8 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        let val = f64::from_le_bytes([
            self.data[self.read_pos],
            self.data[self.read_pos + 1],
            self.data[self.read_pos + 2],
            self.data[self.read_pos + 3],
            self.data[self.read_pos + 4],
            self.data[self.read_pos + 5],
            self.data[self.read_pos + 6],
            self.data[self.read_pos + 7],
        ]);
        self.read_pos += 8;
        Ok(val)
    }

    /// Read a variable-length ASCII string (2-byte LE length prefix + data).
    pub fn read_ascii_string(&mut self) -> io::Result<String> {
        let len = self.read_integer()? as usize;
        if self.available() < len {
            // Rollback the 2-byte length read
            self.read_pos -= 2;
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        // VB6 uses Latin-1/Windows-1252 — each byte maps to its Unicode codepoint
        let s: String = self.data[self.read_pos..self.read_pos + len]
            .iter()
            .map(|&b| b as char)
            .collect();
        self.read_pos += len;
        Ok(s)
    }

    /// Read a fixed-length ASCII string (no length prefix).
    pub fn read_ascii_string_fixed(&mut self, len: usize) -> io::Result<String> {
        if self.available() < len {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        let s: String = self.data[self.read_pos..self.read_pos + len]
            .iter()
            .map(|&b| b as char)
            .collect();
        self.read_pos += len;
        Ok(s)
    }

    // ── Peek methods ───────────────────────────────────────────

    /// Peek at the next byte without advancing.
    pub fn peek_byte(&self) -> io::Result<u8> {
        if self.available() < 1 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        Ok(self.data[self.read_pos])
    }

    /// Peek at the next 16-bit integer without advancing.
    pub fn peek_integer(&self) -> io::Result<i16> {
        if self.available() < 2 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, NOT_ENOUGH_DATA));
        }
        Ok(i16::from_le_bytes([
            self.data[self.read_pos],
            self.data[self.read_pos + 1],
        ]))
    }
}

impl Default for ByteQueue {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn byte_roundtrip() {
        let mut bq = ByteQueue::new();
        bq.write_byte(0);
        bq.write_byte(127);
        bq.write_byte(255);
        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        assert_eq!(reader.read_byte().unwrap(), 0);
        assert_eq!(reader.read_byte().unwrap(), 127);
        assert_eq!(reader.read_byte().unwrap(), 255);
        assert!(reader.is_empty());
    }

    #[test]
    fn boolean_roundtrip() {
        let mut bq = ByteQueue::new();
        bq.write_boolean(true);
        bq.write_boolean(false);
        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        assert!(reader.read_boolean().unwrap());
        assert!(!reader.read_boolean().unwrap());
    }

    #[test]
    fn integer_roundtrip() {
        let mut bq = ByteQueue::new();
        bq.write_integer(0);
        bq.write_integer(1234);
        bq.write_integer(-1234);
        bq.write_integer(i16::MAX);
        bq.write_integer(i16::MIN);
        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        assert_eq!(reader.read_integer().unwrap(), 0);
        assert_eq!(reader.read_integer().unwrap(), 1234);
        assert_eq!(reader.read_integer().unwrap(), -1234);
        assert_eq!(reader.read_integer().unwrap(), i16::MAX);
        assert_eq!(reader.read_integer().unwrap(), i16::MIN);
    }

    #[test]
    fn long_roundtrip() {
        let mut bq = ByteQueue::new();
        bq.write_long(0);
        bq.write_long(123456789);
        bq.write_long(-123456789);
        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        assert_eq!(reader.read_long().unwrap(), 0);
        assert_eq!(reader.read_long().unwrap(), 123456789);
        assert_eq!(reader.read_long().unwrap(), -123456789);
    }

    #[test]
    fn single_roundtrip() {
        let mut bq = ByteQueue::new();
        bq.write_single(3.14);
        bq.write_single(-0.5);
        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        let v1 = reader.read_single().unwrap();
        let v2 = reader.read_single().unwrap();
        assert!((v1 - 3.14).abs() < 0.001);
        assert!((v2 - (-0.5)).abs() < 0.001);
    }

    #[test]
    fn string_roundtrip() {
        let mut bq = ByteQueue::new();
        bq.write_ascii_string("Hello AO");
        bq.write_ascii_string("");
        bq.write_ascii_string("Argentum Nextgen");
        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        assert_eq!(reader.read_ascii_string().unwrap(), "Hello AO");
        assert_eq!(reader.read_ascii_string().unwrap(), "");
        assert_eq!(reader.read_ascii_string().unwrap(), "Argentum Nextgen");
    }

    #[test]
    fn not_enough_data() {
        let mut reader = ByteQueue::from_bytes(&[0x01]);
        assert!(reader.read_byte().is_ok());
        assert!(reader.read_byte().is_err());

        let mut reader = ByteQueue::from_bytes(&[0x01]);
        assert!(reader.read_integer().is_err());
    }

    #[test]
    fn peek_does_not_advance() {
        let mut reader = ByteQueue::from_bytes(&[42, 0x01, 0x00]);
        assert_eq!(reader.peek_byte().unwrap(), 42);
        assert_eq!(reader.peek_byte().unwrap(), 42); // still same
        assert_eq!(reader.read_byte().unwrap(), 42); // now advance
        assert_eq!(reader.peek_integer().unwrap(), 1);
        assert_eq!(reader.read_integer().unwrap(), 1);
    }

    #[test]
    fn save_restore_position() {
        let mut reader = ByteQueue::from_bytes(&[1, 2, 3, 4]);
        let pos = reader.save_position();
        reader.read_byte().unwrap();
        reader.read_byte().unwrap();
        assert_eq!(reader.available(), 2);
        reader.restore_position(pos);
        assert_eq!(reader.available(), 4);
        assert_eq!(reader.read_byte().unwrap(), 1);
    }

    #[test]
    fn mixed_types() {
        // Simulate a CharacterCreate packet (ID 29)
        let mut bq = ByteQueue::new();
        bq.write_byte(29);         // packet ID
        bq.write_integer(42);      // charIndex
        bq.write_integer(100);     // body
        bq.write_integer(10);      // head
        bq.write_byte(3);          // heading (South)
        bq.write_byte(50);         // x
        bq.write_byte(50);         // y
        bq.write_integer(0);       // weapon
        bq.write_integer(0);       // shield
        bq.write_integer(0);       // helmet
        bq.write_integer(0);       // fxIndex
        bq.write_integer(0);       // fxLoops
        bq.write_ascii_string("TestPlayer"); // name
        bq.write_byte(0);          // nickColor
        bq.write_byte(0);          // privileges

        let mut reader = ByteQueue::from_bytes(bq.as_bytes());
        assert_eq!(reader.read_byte().unwrap(), 29);
        assert_eq!(reader.read_integer().unwrap(), 42);
        assert_eq!(reader.read_integer().unwrap(), 100);
        assert_eq!(reader.read_integer().unwrap(), 10);
        assert_eq!(reader.read_byte().unwrap(), 3);
        assert_eq!(reader.read_byte().unwrap(), 50);
        assert_eq!(reader.read_byte().unwrap(), 50);
        assert_eq!(reader.read_integer().unwrap(), 0);
        assert_eq!(reader.read_integer().unwrap(), 0);
        assert_eq!(reader.read_integer().unwrap(), 0);
        assert_eq!(reader.read_integer().unwrap(), 0);
        assert_eq!(reader.read_integer().unwrap(), 0);
        assert_eq!(reader.read_ascii_string().unwrap(), "TestPlayer");
        assert_eq!(reader.read_byte().unwrap(), 0);
        assert_eq!(reader.read_byte().unwrap(), 0);
        assert!(reader.is_empty());
    }
}

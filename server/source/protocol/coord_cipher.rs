//! Rolling coordinate cipher for anti-cheat protection.
//!
//! Both client and server share a seed (sent at login) and maintain a
//! synchronized packet counter. Each coordinate-bearing packet (LC, RC, WLC)
//! encodes X/Y with an offset derived from the seed and counter.
//!
//! Algorithm must match the C# implementation in CoordCipher.cs exactly.

/// Per-connection coordinate cipher state.
#[derive(Debug, Clone)]
pub struct CoordCipher {
    seed: u32,
    counter: u32,
}

impl CoordCipher {
    /// Create a new cipher with the given seed.
    pub fn new(seed: u32) -> Self {
        Self { seed, counter: 0 }
    }

    /// Advance counter and compute offset pair for this packet.
    fn next_offset(&mut self) -> (u16, u16) {
        self.counter = self.counter.wrapping_add(1);
        let mut state = self.seed ^ self.counter;

        // XorShift32 — must match C# exactly
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        // Second round for Y independence
        let mut state_y = state ^ self.counter.wrapping_mul(2654435761u32); // Knuth multiplicative hash
        state_y ^= state_y << 13;
        state_y ^= state_y >> 17;
        state_y ^= state_y << 5;

        ((state & 0xFFFF) as u16, (state_y & 0xFFFF) as u16)
    }

    /// Decode encoded coordinates received from the client.
    pub fn decode(&mut self, encoded_x: i16, encoded_y: i16) -> (i16, i16) {
        let (ox, oy) = self.next_offset();
        let x = (encoded_x as u16).wrapping_sub(ox) as i16;
        let y = (encoded_y as u16).wrapping_sub(oy) as i16;
        (x, y)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encode_decode_roundtrip() {
        let seed = 0xDEADBEEF_u32;

        // Simulate client encoding
        let mut client = CoordCipher::new(seed);
        let (ox, oy) = client.next_offset();
        let x: i16 = 50;
        let y: i16 = 32;
        let encoded_x = (x as u16).wrapping_add(ox) as i16;
        let encoded_y = (y as u16).wrapping_add(oy) as i16;

        // Simulate server decoding
        let mut server = CoordCipher::new(seed);
        let (dx, dy) = server.decode(encoded_x, encoded_y);

        assert_eq!(dx, x);
        assert_eq!(dy, y);
    }

    #[test]
    fn different_seeds_produce_different_offsets() {
        let mut c1 = CoordCipher::new(1234);
        let mut c2 = CoordCipher::new(5678);
        let (o1x, _) = c1.next_offset();
        let (o2x, _) = c2.next_offset();
        assert_ne!(o1x, o2x);
    }

    #[test]
    fn sequential_packets_produce_different_offsets() {
        let mut c = CoordCipher::new(42);
        let (o1x, o1y) = c.next_offset();
        let (o2x, o2y) = c.next_offset();
        assert_ne!(o1x, o2x);
        assert_ne!(o1y, o2y);
    }
}

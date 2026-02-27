/// AoDefender Base64 Encoding/Decoding.
///
/// This is standard Base64 with CRLF line breaks every 72 characters of output.
/// Mirrors VB6: `AoDefEncode()` / `AoDefDecode()`
/// from AoDefenderEncryptClient.bas

const ENCODE_TABLE: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

/// Build decode table at compile time is not trivial, so we build it at runtime.
fn decode_table() -> [u8; 256] {
    let mut table = [0u8; 256];
    for (i, &ch) in ENCODE_TABLE.iter().enumerate() {
        table[ch as usize] = i as u8;
    }
    table
}

/// Encode bytes to Base64 string with CRLF line wrapping at 72 chars.
///
/// VB6 equivalent: `AoDefEncode(sString)`
pub fn aodef_encode(data: &[u8]) -> String {
    // Pad input to multiple of 3
    let pad = if data.len() % 3 != 0 {
        3 - (data.len() % 3)
    } else {
        0
    };

    let mut input = data.to_vec();
    input.resize(data.len() + pad, 0);

    let mut output = Vec::with_capacity((input.len() / 3) * 4 + (input.len() / 3 / 18) * 2);
    let mut line_len = 0u32;

    for chunk in input.chunks(3) {
        let trip: u32 = (chunk[0] as u32) << 16 | (chunk[1] as u32) << 8 | chunk[2] as u32;

        output.push(ENCODE_TABLE[((trip >> 18) & 0x3F) as usize]);
        output.push(ENCODE_TABLE[((trip >> 12) & 0x3F) as usize]);
        output.push(ENCODE_TABLE[((trip >> 6) & 0x3F) as usize]);
        output.push(ENCODE_TABLE[(trip & 0x3F) as usize]);

        line_len += 4;
        if line_len == 72 {
            output.push(b'\r');
            output.push(b'\n');
            line_len = 0;
        }
    }

    // Remove trailing CRLF if present
    if output.ends_with(&[b'\r', b'\n']) {
        output.pop();
        output.pop();
    }

    // Add padding '=' characters
    let out_len = output.len();
    if pad == 1 {
        output[out_len - 1] = b'=';
    } else if pad == 2 {
        output[out_len - 1] = b'=';
        output[out_len - 2] = b'=';
    }

    // Safe because we only used ASCII characters
    unsafe { String::from_utf8_unchecked(output) }
}

/// Decode Base64 string back to bytes.
///
/// VB6 equivalent: `AoDefDecode(sString)`
pub fn aodef_decode(data: &[u8]) -> Vec<u8> {
    let table = decode_table();

    // Strip CR and LF
    let cleaned: Vec<u8> = data.iter().copied().filter(|&b| b != b'\r' && b != b'\n').collect();

    if cleaned.len() % 4 != 0 {
        return Vec::new(); // Invalid base64
    }

    // Count padding
    let pad = if cleaned.len() >= 2 && cleaned[cleaned.len() - 1] == b'=' && cleaned[cleaned.len() - 2] == b'=' {
        2
    } else if !cleaned.is_empty() && cleaned[cleaned.len() - 1] == b'=' {
        1
    } else {
        0
    };

    let mut output = Vec::with_capacity((cleaned.len() / 4) * 3);

    for chunk in cleaned.chunks(4) {
        let quad: u32 = (table[chunk[0] as usize] as u32) << 18
            | (table[chunk[1] as usize] as u32) << 12
            | (table[chunk[2] as usize] as u32) << 6
            | table[chunk[3] as usize] as u32;

        output.push(((quad >> 16) & 0xFF) as u8);
        output.push(((quad >> 8) & 0xFF) as u8);
        output.push((quad & 0xFF) as u8);
    }

    // Remove padding bytes
    if pad > 0 {
        output.truncate(output.len() - pad);
    }

    output
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encode_empty() {
        assert_eq!(aodef_encode(b""), "");
    }

    #[test]
    fn encode_hello() {
        // Standard Base64 for "Hello" = "SGVsbG8="
        let encoded = aodef_encode(b"Hello");
        assert_eq!(encoded, "SGVsbG8=");
    }

    #[test]
    fn roundtrip_short() {
        let original = b"AO Server Test";
        let encoded = aodef_encode(original);
        let decoded = aodef_decode(encoded.as_bytes());
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_long() {
        // Test with data that will trigger line wrapping (> 54 input bytes → > 72 output chars)
        let original: Vec<u8> = (0..100).collect();
        let encoded = aodef_encode(&original);
        let decoded = aodef_decode(encoded.as_bytes());
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_exact_multiples() {
        // Exact multiple of 3 — no padding
        let original = b"123456789012"; // 12 bytes
        let encoded = aodef_encode(original);
        assert!(!encoded.contains('='));
        let decoded = aodef_decode(encoded.as_bytes());
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_all_bytes() {
        let original: Vec<u8> = (0..=255).collect();
        let encoded = aodef_encode(&original);
        let decoded = aodef_decode(encoded.as_bytes());
        assert_eq!(decoded, original);
    }
}

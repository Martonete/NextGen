/// AoDefender Hex Encoding — Server-side encryption layer.
///
/// This is a simple byte-to-hex encoding. Each byte is converted to two
/// hex characters (uppercase A-F for 10-15, digits for 0-9).
///
/// Mirrors VB6: `AoDefServEncrypt()` / `AoDefServDecrypt()`
/// from AoDefenderEncryptServer.bas

/// Convert a nibble (0-15) to its hex character.
fn conv_to_hex(x: u8) -> u8 {
    if x > 9 {
        // Maps 10->A(65), 11->B(66), ..., 15->F(70)
        x + 55
    } else {
        // Maps 0->0x30('0'), 1->0x31('1'), etc.
        x + b'0'
    }
}

/// Convert a two-character hex string back to a byte value.
fn conv_to_int(hi: u8, lo: u8) -> u8 {
    let h = if hi.is_ascii_digit() {
        (hi - b'0') as u16 * 16
    } else {
        (hi - 55) as u16 * 16
    };

    let l = if lo.is_ascii_digit() {
        (lo - b'0') as u16
    } else {
        (lo - 55) as u16
    };

    (h + l) as u8
}

/// Encode bytes to hex string (server encrypt).
/// Each input byte becomes 2 hex characters.
///
/// VB6 equivalent: `AoDefServEncrypt(DataValue)`
pub fn aodef_serv_encrypt(data: &[u8]) -> Vec<u8> {
    let mut result = Vec::with_capacity(data.len() * 2);

    for &byte in data {
        let hi = byte / 16;
        let lo = byte - (hi * 16);
        result.push(conv_to_hex(hi));
        result.push(if lo > 0 || byte != hi * 16 {
            conv_to_hex(lo)
        } else {
            b'0'
        });
    }

    result
}

/// Decode hex string back to bytes (server decrypt).
///
/// VB6 equivalent: `AoDefServDecrypt(DataValue)`
pub fn aodef_serv_decrypt(data: &[u8]) -> Vec<u8> {
    let mut result = Vec::with_capacity(data.len() / 2);

    let mut i = 0;
    while i + 1 < data.len() {
        result.push(conv_to_int(data[i], data[i + 1]));
        i += 2;
    }

    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encrypt_single_byte() {
        // byte 0x41 = 'A' = 65 decimal
        // hi = 65/16 = 4, lo = 65 - 64 = 1
        // hex: "41"
        let result = aodef_serv_encrypt(&[0x41]);
        assert_eq!(result, b"41");
    }

    #[test]
    fn encrypt_zero() {
        let result = aodef_serv_encrypt(&[0]);
        assert_eq!(result, b"00");
    }

    #[test]
    fn encrypt_255() {
        // 255 = 0xFF
        // hi = 15 -> F, lo = 15 -> F
        let result = aodef_serv_encrypt(&[255]);
        assert_eq!(result, b"FF");
    }

    #[test]
    fn roundtrip() {
        let data = b"Hello World!";
        let encrypted = aodef_serv_encrypt(data);
        let decrypted = aodef_serv_decrypt(&encrypted);
        assert_eq!(decrypted, data);
    }

    #[test]
    fn roundtrip_all_bytes() {
        let data: Vec<u8> = (0..=255).collect();
        let encrypted = aodef_serv_encrypt(&data);
        let decrypted = aodef_serv_decrypt(&encrypted);
        assert_eq!(decrypted, data);
    }
}

/// AoDefender Cipher — XOR-like stream cipher with seed.
///
/// This is the per-packet cipher layer. Each connection has a rotating key
/// derived from a packet counter via `Numero2Letra` → `Semilla`.
///
/// The seed is a string of format "num1,num2" where num1 and num2 are derived
/// from the key string.
///
/// Mirrors VB6: `Semilla()`, `Codificar()`, `DeCodificar()`
/// from AoDefenderEncrypt2.bas

/// Generate a seed pair from a key string.
///
/// The seed is "seed1,seed2" where:
///   seed1 = sum of (ascii(char[i]) * (i+1)) for all chars
///   seed2 = sum of (ascii(char[i]) * (len-i)) for all chars
///
/// VB6 equivalent: `Semilla(strClave)`
pub fn semilla(key: &str) -> String {
    let bytes = key.as_bytes();
    let len = bytes.len();
    let mut seed1: i64 = 0;
    let mut seed2: i64 = 0;

    for (i, &b) in bytes.iter().enumerate() {
        // VB6 uses 1-based index: i+1 for seed1, len-i for seed2
        seed1 += b as i64 * (i as i64 + 1);
        seed2 += b as i64 * (len as i64 - i as i64);
    }

    format!("{},{}", seed1, seed2)
}

/// Parse a seed string "num1,num2" into (seed1, seed2).
fn parse_seed(seed: &str) -> (i64, i64) {
    let comma_pos = seed.find(',').unwrap_or(seed.len());
    let s1: i64 = seed[..comma_pos].parse().unwrap_or(0);
    let s2: i64 = if comma_pos < seed.len() {
        seed[comma_pos + 1..].parse().unwrap_or(0)
    } else {
        0
    };
    (s1, s2)
}

/// Encode (encrypt) data using the seed-based cipher.
///
/// For each byte at position i (1-based):
///   - seed1 decreases by i each step
///   - seed2 increases by i each step
///   - If even position: byte = (byte - seed1) & 0xFF
///   - If odd position:  byte = (byte + seed2) & 0xFF
///
/// VB6 equivalent: `Codificar(strCadena, strSemilla)`
pub fn codificar(data: &[u8], seed: &str) -> Vec<u8> {
    let (mut s1, mut s2) = parse_seed(seed);
    let mut result = Vec::with_capacity(data.len());

    for (i, &byte) in data.iter().enumerate() {
        let pos = i + 1; // VB6 is 1-based
        s1 -= pos as i64;
        s2 += pos as i64;

        let new_byte = if pos % 2 == 0 {
            // Even: subtract seed1
            ((byte as i64 - s1) & 0xFF) as u8
        } else {
            // Odd: add seed2
            ((byte as i64 + s2) & 0xFF) as u8
        };

        result.push(new_byte);
    }

    result
}

/// Decode (decrypt) data using the seed-based cipher.
///
/// Inverse of `codificar`:
///   - If even position: byte = (byte + seed1) & 0xFF
///   - If odd position:  byte = (byte - seed2) & 0xFF
///
/// VB6 equivalent: `DeCodificar(strCadena, strSemilla)`
pub fn decodificar(data: &[u8], seed: &str) -> Vec<u8> {
    let (mut s1, mut s2) = parse_seed(seed);
    let mut result = Vec::with_capacity(data.len());

    for (i, &byte) in data.iter().enumerate() {
        let pos = i + 1; // VB6 is 1-based
        s1 -= pos as i64;
        s2 += pos as i64;

        let new_byte = if pos % 2 == 0 {
            // Even: add seed1 (reverse of subtract)
            ((byte as i64 + s1) & 0xFF) as u8
        } else {
            // Odd: subtract seed2 (reverse of add)
            ((byte as i64 - s2) & 0xFF) as u8
        };

        result.push(new_byte);
    }

    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn semilla_basic() {
        // "AB" → seed1 = 65*1 + 66*2 = 65+132 = 197
        //       → seed2 = 65*2 + 66*1 = 130+66 = 196
        assert_eq!(semilla("AB"), "197,196");
    }

    #[test]
    fn semilla_single_char() {
        // "A" → seed1 = 65*1 = 65, seed2 = 65*1 = 65
        assert_eq!(semilla("A"), "65,65");
    }

    #[test]
    fn roundtrip_basic() {
        let seed = "100,200";
        let original = b"Hello World";
        let encoded = codificar(original, seed);
        let decoded = decodificar(&encoded, seed);
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_with_semilla() {
        let key = semilla("ZiPPyNoPPy");
        let original = b"ALOGIN test,pass,1.0.1";
        let encoded = codificar(original, &key);
        // Encoded should differ from original
        assert_ne!(encoded, original);
        let decoded = decodificar(&encoded, &key);
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_empty() {
        let seed = "100,200";
        let original = b"";
        let encoded = codificar(original, seed);
        let decoded = decodificar(&encoded, seed);
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_all_bytes() {
        let seed = semilla("ComplexKey123!");
        let original: Vec<u8> = (0..=255).collect();
        let encoded = codificar(&original, &seed);
        let decoded = decodificar(&encoded, &seed);
        assert_eq!(decoded, original);
    }
}

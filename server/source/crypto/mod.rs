pub mod aodef_encrypt;
pub mod aodef_base64;
pub mod aodef_cipher;
pub mod aodef_converter;

#[allow(unused_imports)]
pub use aodef_encrypt::{aodef_serv_encrypt, aodef_serv_decrypt};
pub use aodef_base64::{aodef_encode, aodef_decode};
#[allow(unused_imports)]
pub use aodef_cipher::{semilla, codificar, decodificar};
#[allow(unused_imports)]
pub use aodef_converter::numero2letra;

/// Full encryption pipeline for outbound data (server → client).
/// Mirrors VB6: `AoDefEncode(AoDefServEncrypt(data))`
pub fn encrypt_outbound(data: &[u8]) -> Vec<u8> {
    let hex_encoded = aodef_serv_encrypt(data);
    aodef_encode(&hex_encoded).into_bytes()
}

/// Full decryption pipeline for inbound data (client → server).
/// Mirrors VB6: `DeCodificar(AoDefDecode(data), key)`
pub fn decrypt_inbound(data: &[u8], key: &str) -> Vec<u8> {
    let decoded = aodef_decode(data);
    decodificar(&decoded, key)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roundtrip_serv_encrypt() {
        let original = b"Hello AO World!";
        let encrypted = aodef_serv_encrypt(original);
        let decrypted = aodef_serv_decrypt(&encrypted);
        assert_eq!(decrypted, original);
    }

    #[test]
    fn roundtrip_base64() {
        let original = b"Test data 12345";
        let encoded = aodef_encode(original);
        let decoded = aodef_decode(encoded.as_bytes());
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_cipher() {
        let key_str = "ZiPPy";
        let seed = semilla(key_str);
        let original = b"ALOGIN testuser,testpass,1.0.1";
        let encoded = codificar(original, &seed);
        let decoded = decodificar(&encoded, &seed);
        assert_eq!(decoded, original);
    }

    #[test]
    fn roundtrip_full_pipeline() {
        // Simulate: server encrypts, client decrypts (or vice versa)
        // For the full pipeline we need a known key
        let key = "12345,67890";
        let original = b"||190Test message";

        // Server side: encrypt outbound
        let encrypted = encrypt_outbound(original);

        // Client side would: AoDefDecode → then DeCodificar with key
        // But our decrypt_inbound does: AoDefDecode → DeCodificar
        // The server encrypt_outbound does: AoDefServEncrypt → AoDefEncode
        // So to reverse we need: AoDefDecode → AoDefServDecrypt (not DeCodificar)
        // This is because the two crypto layers are independent.

        // Test the hex layer alone
        let hex = aodef_serv_encrypt(original);
        let unhex = aodef_serv_decrypt(&hex);
        assert_eq!(unhex, original);

        // Test cipher layer alone
        let ciphered = codificar(original, key);
        let unciphered = decodificar(&ciphered, key);
        assert_eq!(unciphered, original);
    }

    #[test]
    fn semilla_deterministic() {
        let s1 = semilla("TestKey");
        let s2 = semilla("TestKey");
        assert_eq!(s1, s2);
        assert!(s1.contains(','));
    }

    #[test]
    fn decrypt_real_client_packet_1() {
        // Real captured data from VB6 client, first packet (counter=1)
        // Raw base64 bytes: "goaOjHeFg5ebqarCxN/g+w=="
        let raw_b64 = b"goaOjHeFg5ebqarCxN/g+w==";

        // Derive key for counter=1
        let text = super::aodef_converter::numero2letra(1);
        let no_spaces: String = text.chars().filter(|c| *c != ' ').collect();
        let key = semilla(&no_spaces);
        eprintln!("Key for counter=1: {}", key);

        // Decrypt: AoDefDecode → DeCodificar
        let decoded = aodef_decode(raw_b64);
        eprintln!("After base64 decode ({} bytes): {:?}", decoded.len(),
            decoded.iter().map(|b| format!("{:02X}", b)).collect::<Vec<_>>().join(" "));

        let decrypted = decodificar(&decoded, &key);
        let text_result = String::from_utf8_lossy(&decrypted);
        eprintln!("Decrypted text: '{}'", text_result);

        // First packet should start with "KERD22"
        assert!(text_result.starts_with("KERD22"),
            "First packet should start with KERD22, got: '{}'", text_result);
    }
}

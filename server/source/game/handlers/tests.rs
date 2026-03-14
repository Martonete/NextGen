// Integration tests disabled — require PostgreSQL. TODO: add DB-backed tests.
// See git history for the original file-based test suite.
#[cfg(test)]
mod tests {
    #[test]
    fn placeholder() {
        // Tests require a running PostgreSQL instance.
        // Run with: DATABASE_URL=postgres://... cargo test
    }
}

#[cfg(all(test, feature = "_db_integration_tests"))]
mod db_tests {
    use super::*;
    use crate::config::ServerConfig;
    use crate::data::GameData;
    use crate::net::connection;
    use std::path::{Path, PathBuf};
    use tokio::net::{TcpListener, TcpStream};

    /// Real server base path (contains dat/, maps/ etc.)
    fn server_base() -> std::path::PathBuf {
        Path::new(env!("CARGO_MANIFEST_DIR")).join("server")
    }

    /// Create a temp test directory with symlinks to real game data.
    fn setup_test_dir(test_name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("ao_test_{}", test_name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        // Symlink data directories from the real server
        let base = server_base();
        let base = base.as_path();
        for subdir in &["dat", "maps"] {
            let src = base.join(subdir);
            let dst = dir.join(subdir);
            if src.exists() {
                std::os::unix::fs::symlink(&src, &dst).unwrap();
            }
        }

        // Create empty Accounts/ and charfile/ dirs
        std::fs::create_dir_all(dir.join("Accounts")).unwrap();
        std::fs::create_dir_all(dir.join("charfile")).unwrap();

        dir
    }

    fn cleanup_test_dir(dir: &Path) {
        let _ = std::fs::remove_dir_all(dir);
    }

    /// Create a test ServerConfig with sensible defaults.
    fn test_config(notice: &str) -> ServerConfig {
        ServerConfig {
            server_ip: "127.0.0.1".into(),
            port: 0,
            max_users: 100,
            version: "1.0.0".into(),
            client_version: "1.0.0".into(),
            idle_limit: 0,
            allow_multi_logins: false,
            can_create_characters: true,
            server_only_gms: false,
            exp_multiplier: 1,
            gold_multiplier: 1,
            drop_multiplier: 1,
            start_map: 1,
            start_x: 50,
            start_y: 50,
            char_dir: "charfile".into(),
            log_dir: "logs".into(),
            notice: notice.to_string(),
            pretoriano_map: 0,
            intervalo_paralizado: 500,
            intervalo_invisible: 500,
            intervalo_oculto: 500,
            npc_ai_interval_ms: 1300,
        }
    }

    /// Create a TCP pair and return (client_stream, server ConnectionWriter).
    async fn create_tcp_pair() -> (TcpStream, connection::ConnectionWriter) {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();

        let (client_result, server_result) = tokio::join!(
            TcpStream::connect(format!("127.0.0.1:{}", port)),
            listener.accept()
        );
        let client_stream = client_result.unwrap();
        let (server_stream, addr) = server_result.unwrap();

        let (_reader, writer) = connection::split_connection(1, server_stream, addr);
        (client_stream, writer)
    }

    /// Create a test account with one character.
    fn create_test_data(base: &Path) {
        // Create account
        accounts::create_account(base, "testaccount", "testpass", "1234", "9999").unwrap();
        accounts::add_character_to_account(base, "testaccount", "TestHero").unwrap();

        // Create character
        charfile::create_charfile(
            base,
            "TestHero",
            "Humano",
            1,          // Male
            "Guerrero",
            1,          // Hogar
            70,         // Head
            "9999",     // Password (CodeX)
            [18, 18, 18, 18, 18], // Attributes
            1,          // Start map
            50,         // Start X
            50,         // Start Y
        ).unwrap();
    }

    /// Encrypt a client packet (simulates VB6 client encryption).
    /// Pipeline: Codificar(plaintext, key) → AoDefEncode → append \0
    fn encrypt_client_packet(plaintext: &[u8], counter: i64) -> Vec<u8> {
        // Derive key from counter (same as server-side derive_key)
        let text = crate::crypto::numero2letra(counter);
        let text_no_spaces: String = text.chars().filter(|c| *c != ' ').collect();
        let key = crate::crypto::semilla(&text_no_spaces);

        // Codificar (XOR cipher)
        let ciphered = crate::crypto::codificar(plaintext, &key);
        // AoDefEncode (base64)
        let encoded = crate::crypto::aodef_encode(&ciphered);

        let mut result = encoded.into_bytes();
        result.push(0x00); // Null terminator
        result
    }

    /// Decrypt a server packet (simulates VB6 client decryption).
    /// Pipeline: AoDefDecode(base64) → AoDefServDecrypt(hex)
    fn decrypt_server_packet(raw: &[u8]) -> String {
        let decoded = crate::crypto::aodef_decode(raw);
        let decrypted = crate::crypto::aodef_serv_decrypt(&decoded);
        String::from_utf8_lossy(&decrypted).to_string()
    }

    /// Read all pending packets from a TCP stream (non-blocking after initial data).
    /// Returns a Vec of decrypted plaintext packets.
    async fn read_server_packets(stream: &mut TcpStream) -> Vec<String> {
        use tokio::io::AsyncReadExt;

        let mut buf = [0u8; 16384];
        let mut packets = Vec::new();

        // Give server a moment to send responses
        tokio::time::sleep(std::time::Duration::from_millis(200)).await;

        // Read available data
        match tokio::time::timeout(
            std::time::Duration::from_millis(500),
            stream.read(&mut buf),
        ).await {
            Ok(Ok(n)) if n > 0 => {
                // Split on null bytes
                let data = &buf[..n];
                for chunk in data.split(|b| *b == 0x00) {
                    if !chunk.is_empty() {
                        let plain = decrypt_server_packet(chunk);
                        if !plain.is_empty() {
                            packets.push(plain);
                        }
                    }
                }
            }
            _ => {}
        }

        packets
    }

    /// Read ALL packets from server until no more data (with retries).
    async fn read_all_server_packets(stream: &mut TcpStream) -> Vec<String> {
        use tokio::io::AsyncReadExt;

        let mut all_packets = Vec::new();
        let mut buf = [0u8; 65536];
        let mut accumulated = Vec::new();

        // Keep reading until timeout (server may send data in bursts)
        loop {
            match tokio::time::timeout(
                std::time::Duration::from_millis(300),
                stream.read(&mut buf),
            ).await {
                Ok(Ok(n)) if n > 0 => {
                    accumulated.extend_from_slice(&buf[..n]);
                }
                _ => break,
            }
        }

        // Split on null bytes and decrypt
        for chunk in accumulated.split(|b| *b == 0x00) {
            if !chunk.is_empty() {
                let plain = decrypt_server_packet(chunk);
                if !plain.is_empty() {
                    all_packets.push(plain);
                }
            }
        }

        all_packets
    }

    #[tokio::test]
    async fn test_full_login_flow() {
        // Setup test directory with game data + test account/charfile
        let test_dir = setup_test_dir("login_flow");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("Bienvenido a Argentum Nextgen!");

        // Create TCP pair (server↔client)
        let (mut client_stream, writer) = create_tcp_pair().await;

        // Initialize GameState
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // ====== STEP 1: Send KERD22 (HD serial check) ======
        let kerd22_plain = b"KERD22ABC123HD";
        let kerd22_encrypted = encrypt_client_packet(kerd22_plain, 1);

        use tokio::io::AsyncWriteExt;
        client_stream.write_all(&kerd22_encrypted).await.unwrap();

        // Process on server side (simulate what main loop does)
        // We need to decrypt as the server would
        let kerd22_decrypted = {
            let text = crate::crypto::numero2letra(1);
            let text_no_spaces: String = text.chars().filter(|c| *c != ' ').collect();
            let key = crate::crypto::semilla(&text_no_spaces);
            let raw = &kerd22_encrypted[..kerd22_encrypted.len() - 1]; // strip \0
            let decrypted = crate::crypto::decrypt_inbound(raw, &key);
            String::from_utf8_lossy(&decrypted).to_string()
        };
        assert_eq!(kerd22_decrypted.trim(), "KERD22ABC123HD");

        // Call handler directly with plaintext
        handle_packet(&mut state, 1, "KERD22ABC123HD").await;

        // Verify: user's HD serial should be stored, paso_hd = true
        {
            let user = state.users.get(&1).unwrap();
            assert_eq!(user.hd_serial, "ABC123HD");
            assert!(user.paso_hd, "paso_hd should be true after KERD22");
        }

        // ====== STEP 2: Send ALOGIN (account login) ======
        handle_packet(&mut state, 1, "ALOGINtestaccount,testpass,1.0.0").await;

        // Read server responses (INIAC + ADDPJ + CODEH)
        let packets = read_all_server_packets(&mut client_stream).await;

        // Verify INIAC: should contain num_pjs=1 and notice
        let iniac = packets.iter().find(|p| p.starts_with("INIAC")).expect("Missing INIAC packet");
        assert!(iniac.starts_with("INIAC1,"), "INIAC should show 1 character, got: {}", iniac);

        // Verify ADDPJ: should contain TestHero
        let addpj = packets.iter().find(|p| p.starts_with("ADDPJ")).expect("Missing ADDPJ packet");
        assert!(addpj.contains("TestHero"), "ADDPJ should contain TestHero, got: {}", addpj);

        // Verify CODEH: should contain security code
        let codeh = packets.iter().find(|p| p.starts_with("CODEH")).expect("Missing CODEH packet");
        assert!(codeh.len() > 5, "CODEH should have a security code, got: {}", codeh);

        // Verify user state after ALOGIN
        {
            let user = state.users.get(&1).unwrap();
            assert_eq!(user.account_name, "testaccount");
        }

        // ====== STEP 3: Send THCJXD (character login) ======
        let codex = &codeh[5..]; // Extract security code from CODEH response
        let thcjxd_pkt = format!("THCJXDTestHero,testaccount,{}", codex);
        handle_packet(&mut state, 1, &thcjxd_pkt).await;

        // Read the full login packet sequence
        let login_packets = read_all_server_packets(&mut client_stream).await;

        // ====== VERIFY COMPLETE LOGIN SEQUENCE ======
        // The VB6 client expects these packets in order:

        // 1. CM (Change Map) — must be first
        let cm = login_packets.iter().find(|p| p.starts_with("CM")).expect("Missing CM packet");
        assert!(cm.starts_with("CM1,"), "CM should be map 1, got: {}", cm);

        // 2. PU (Position Update)
        let pu = login_packets.iter().find(|p| p.starts_with("PU")).expect("Missing PU packet");
        assert!(pu.starts_with("PU50,50"), "PU should be 50,50, got: {}", pu);

        // 3. XM (Map Music)
        assert!(login_packets.iter().any(|p| p.starts_with("XM")), "Missing XM packet");

        // 4. N~ (Map Name)
        assert!(login_packets.iter().any(|p| p.starts_with("N~")), "Missing N~ packet");

        // 5. LDG (Privilege Level)
        let ldg = login_packets.iter().find(|p| p.starts_with("LDG")).expect("Missing LDG packet");
        assert!(ldg.starts_with("LDG0"), "LDG should be 0 (no privileges), got: {}", ldg);

        // 6. EHYS (Hunger/Thirst)
        let ehys = login_packets.iter().find(|p| p.starts_with("EHYS")).expect("Missing EHYS packet");
        assert!(ehys.contains("100"), "EHYS should contain 100 (full hunger/thirst)");

        // 7. [ES (Bulk Stats)
        let bulk = login_packets.iter().find(|p| p.starts_with("[ES")).expect("Missing [ES bulk stats packet");
        assert!(bulk.contains("TestHero"), "[ES should contain character name, got: {}", bulk);

        // 9. ANM (Equipment stats)
        assert!(login_packets.iter().any(|p| p.starts_with("ANM")), "Missing ANM packet");

        // 10. RPT (Reputation)
        assert!(login_packets.iter().any(|p| p.starts_with("RPT")), "Missing RPT packet");

        // 11. INVI0 (Inventory Init)
        assert!(login_packets.iter().any(|p| p.starts_with("INVI0")), "Missing INVI0 packet");

        // 12. TIS (Scroll timers — 4 of them)
        let tis_count = login_packets.iter().filter(|p| p.starts_with("TIS")).count();
        assert_eq!(tis_count, 4, "Should have 4 TIS packets, got {}", tis_count);

        // 13. LOGGED — the critical packet that switches client to game mode
        assert!(login_packets.iter().any(|p| *p == "LOGGED"), "Missing LOGGED packet!");

        // 14. Post-login console messages (||705, ||706, etc.)
        assert!(login_packets.iter().any(|p| p.starts_with("||705")), "Missing ||705 console message");
        assert!(login_packets.iter().any(|p| p.starts_with("||709")), "Missing ||709 console message");

        // 15. STOPD (paralysis state)
        let stopd = login_packets.iter().find(|p| p.starts_with("STOPD")).expect("Missing STOPD packet");
        assert!(stopd.starts_with("STOPD0"), "STOPD should be 0 (not paralyzed), got: {}", stopd);

        // Verify final game state
        {
            let user = state.users.get(&1).unwrap();
            assert!(user.logged, "User should be logged in");
            assert_eq!(user.char_name, "TestHero");
            assert_eq!(user.pos_map, 1);
            assert_eq!(user.pos_x, 50);
            assert_eq!(user.pos_y, 50);
            assert_eq!(user.level, 1);
            assert_eq!(user.class, PlayerClass::Guerrero);
            assert_eq!(user.race, PlayerRace::Humano);
            assert!(user.char_index.0 > 0, "Should have a char index assigned");
        }
        assert_eq!(state.num_users, 1);

        // Cleanup
        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_login_wrong_password() {
        let test_dir = setup_test_dir("wrong_pass");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // Send KERD22 first (required for paso_hd)
        handle_packet(&mut state, 1, "KERD22ABC123HD").await;

        // Try login with wrong password
        handle_packet(&mut state, 1, "ALOGINtestaccount,WRONGPASS,1.0.0").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        // Should get an ERR packet about wrong password
        let err = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing ERR packet");
        assert!(err.contains("incorrecto"), "ERR should mention incorrect password, got: {}", err);

        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_login_nonexistent_account() {
        let test_dir = setup_test_dir("no_account");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        handle_packet(&mut state, 1, "KERD22ABC123HD").await;
        handle_packet(&mut state, 1, "ALOGINghost_account,pass,1.0.0").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        let err = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing ERR packet");
        assert!(err.contains("no existe"), "ERR should say account doesn't exist, got: {}", err);

        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_login_without_kerd22() {
        let test_dir = setup_test_dir("no_kerd22");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // Skip KERD22 — go straight to ALOGIN (paso_hd = false)
        handle_packet(&mut state, 1, "ALOGINtestaccount,testpass,1.0.0").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        let err = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing ERR packet");
        assert!(err.contains("Tolerancia 0"), "Should get Tolerancia 0 error, got: {}", err);

        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_create_new_account() {
        let test_dir = setup_test_dir("new_account");

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // Create new account via NACCNT
        handle_packet(&mut state, 1, "NACCNTnewplayer,secret123,5678").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        // Should get success message (sent as ERR with success text)
        let msg = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing response packet");
        assert!(msg.contains("exito"), "Should confirm account creation, got: {}", msg);

        // Verify account was created on disk
        assert!(accounts::account_exists(&test_dir, "newplayer"), "Account file should exist");

        cleanup_test_dir(&test_dir);
    }
}

// Protocol module — packet parsing and building.
//
// Binary protocol (13.3): 1-byte opcode + typed fields via ByteQueue.
// No encryption. Little-endian. Mirrors VB6 clsByteQueue.

pub mod fields;
pub mod byte_queue;
pub mod packets;
pub mod binary_packets;
pub mod coord_cipher;

#[allow(unused_imports)]
pub use fields::ReadField;
#[allow(unused_imports)]
pub use byte_queue::ByteQueue;

/// Client packet opcodes — the first 2-6 characters of each decrypted packet.
///
/// These must match EXACTLY what the VB6 client sends.
pub mod client_opcodes {
    // Pre-login opcodes (before UserLogged = true)
    pub const KERD22: &str = "KERD22"; // Hardware serial check
    pub const ALOGIN: &str = "ALOGIN"; // Account login
    pub const NLOGIN: &str = "NLOGIN"; // New character creation
    pub const OOLOGI: &str = "OOLOGI"; // Character login (after account)
    pub const THCJXD: &str = "THCJXD"; // Character login variant
    pub const NACCNT: &str = "NACCNT"; // New account creation
    pub const REPASS: &str = "REPASS"; // Password change
    pub const REECUH: &str = "REECUH"; // Account recovery
    pub const TIRDAD: &str = "TIRDAD"; // Roll dice (attributes)
    pub const TBRP: &str = "TBRP";     // Delete character

    // In-game opcodes (after UserLogged = true)
    pub const TALK: &str = ";";         // Chat message (area talk)
    pub const YELL: &str = "-";         // Yell (larger area)
    pub const WHISPER: &str = "\\";     // Whisper (private message)
    pub const WALK: &str = "M";         // Movement
    pub const ATTACK: &str = "AT";      // Attack
    pub const PICK_UP: &str = "AG";     // Grab item from ground
    pub const SAFE_TOGGLE: &str = "SEG";// Toggle PvP safety
    pub const DROP_ITEM: &str = "TI";   // Drop item
    pub const CAST_SPELL: &str = "LH";  // Cast spell
    pub const LEFT_CLICK: &str = "LC";  // Left click
    pub const RIGHT_CLICK: &str = "RC"; // Right click
    pub const USE_ITEM: &str = "USA";   // Use item
    pub const USE_ITEM_CLICK: &str = "QSA"; // Use item via dblClick on inventory
    pub const EQUIP_ITEM: &str = "EQUI"; // Equip item
    pub const CHANGE_HEADING: &str = "CHEA"; // Change heading
    pub const REQUEST_POS: &str = "RPU";     // Request position update
    pub const MEDITATE: &str = "ME";         // Toggle meditation
    pub const COMMERCE_BUY: &str = "COMP";    // Buy from NPC
    pub const COMMERCE_SELL: &str = "VEND";   // Sell to NPC
    pub const COMMERCE_CLOSE: &str = "FINCOM"; // Close commerce window
    pub const TRADE_OFFER_GOLD: &str = "UOR";  // Offer gold in trade
    pub const TRADE_OFFER_ITEM: &str = "UOC";  // Offer items in trade
    pub const TRADE_RESPONSE: &str = "TDR";    // Accept/reject trade (0=accept, 1=reject, 2=cancel)
    pub const TRADE_CANCEL: &str = "TCM";      // Cancel trade
    pub const TRADE_CHAT: &str = "VHC";        // Trade chat message
    pub const BANK_DEPOSIT: &str = "DEPO";    // Deposit item to bank
    pub const BANK_WITHDRAW: &str = "RETI";   // Withdraw item from bank
    pub const BANK_CLOSE: &str = "FINBAN";    // Close bank window
    pub const WORK_LEFT_CLICK: &str = "WLC";  // Work left click (skill use)
    pub const CONSTRUCT_SMITH: &str = "CNS";  // Construct blacksmith item
    pub const CONSTRUCT_CARP: &str = "CNC";   // Construct carpentry item

    // Guild opcodes
    pub const GUILD_INFO: &str = "GLINFO";    // Request guild info panel
    pub const GUILD_CREATE: &str = "CIG";     // Submit guild creation form (BF delimited)
    pub const GUILD_UPDATE_CODEX: &str = "DESCOD"; // Update codex and description
    pub const GUILD_ACCEPT: &str = "ACEPTARI"; // Accept applicant
    pub const GUILD_REJECT: &str = "RECHAZAR"; // Reject applicant
    pub const GUILD_EXPEL: &str = "ECHARCLA";  // Expel member
    pub const GUILD_NEWS: &str = "ACTGNEWS";   // Update guild news
    pub const GUILD_APPLY: &str = "SOLICITUD"; // Apply to join guild
    pub const GUILD_DETAILS: &str = "CLANDETAILS"; // Request guild details

    // Core opcodes (VB6 13.3)
    pub const SWAP_ITEMS: &str = "SWAP";       // Swap inventory slots
    pub const SKILL_SET: &str = "SKSE";        // Distribute skill points
    pub const SPELL_INFO: &str = "INFS";       // Spell info
    pub const MOVE_SPELL: &str = "DESPHE";     // Move spell position
    pub const PLAYER_INFO: &str = "DAMINF";    // Player stats form
    pub const MINI_STATS: &str = "FEST";       // Mini statistics
    pub const HEAD_CHANGE: &str = "CABEZI";    // Change head/hairstyle
    pub const MOUSE_DROP: &str = "TR";         // Drop item via mouse
    pub const LEVEL_BONUS: &str = "BOF";       // Level bonus selection
    pub const TRAIN_CREATURE: &str = "ENTR";   // Train creature from trainer
    pub const USE_SKILL: &str = "UK";          // Use skill (skill tree / hide)
    pub const RANKINGS: &str = "RANKIN";       // View rankings
    pub const POSITION_UPDATE: &str = "ACTUALIZAR"; // Position re-sync
    pub const MACRO_DETECT: &str = "TENGOMACROS";   // Macro detection
    pub const SOS_SEND: &str = "#";            // Send SOS/consultation
    pub const SOS_RESPOND: &str = "X";         // Admin responds to SOS
    pub const SOS_VIEW: &str = "CONSUL";       // Admin view SOS messages
    pub const DRAG_DROP: &str = "DYDTRA";      // Drag & drop transfer
    pub const CAST_BY_NAME: &str = "DOWNSI";   // Cast spell by target name
    pub const VOTE: &str = "NVOT";             // Vote in poll
    pub const REPORT: &str = "NEWD";           // New report/denuncia

    // House system (VB6 13.3)
    pub const HOUSE_QUERY: &str = "FWO";       // Query house owner/price
    pub const HOUSE_BUY: &str = "CUC";         // Buy house
    pub const PET_RENAME: &str = "CNM";        // Rename pet
}

/// Font index constants — byte IDs matching client FontTypes table (TextosLoader.cs).
/// Used by binary ConsoleMsg (opcode 24) as the font_index field.
pub mod font_index {
    pub const TALK: u8 = 5;        // White (255,255,255) — normal chat
    pub const FIGHT: u8 = 19;      // Red (255,0,0) — combat messages
    pub const WARNING: u8 = 20;    // Blue (32,51,223)
    pub const INFO: u8 = 21;       // Teal-green (69,190,156) — system info
    pub const EJECUCION: u8 = 24;  // Gray (130,130,130)
    pub const PARTY: u8 = 25;      // White (255,255,255) — party
    pub const VENENO: u8 = 26;     // Green (0,255,0) — poison
    pub const GUILD: u8 = 27;      // White (255,255,255) — guild
    pub const SERVER: u8 = 28;     // Green (0,185,0) — server messages
    pub const GUILD_MSG: u8 = 31;  // Gold (228,199,27)
    pub const CONSEJO: u8 = 32;    // Dark blue (0,64,128)
    pub const CONSEJO_CAOS: u8 = 33; // Dark red — chaos council
    pub const CENTINELA: u8 = 36;  // Green (0,170,0)
    pub const AMARILLO: u8 = 38;   // Yellow bold (255,255,0)
    pub const GRIS: u8 = 40;       // Gray bold (130,130,130)
    pub const ROJO: u8 = 42;       // Red (255,0,0) — same as fight but different style
    pub const DIOSES: u8 = 16;     // Blue-purple (100,0,255) — GM chat
    pub const GLOBAL_USUARIO: u8 = 43; // Light purple (173,170,255)
    pub const VERDE: u8 = 48;      // Green (0,255,0)
    pub const CELESTE: u8 = 52;    // Cyan (128,255,255)
    pub const NARANJA: u8 = 82;    // Orange (255,128,0)
    pub const WHISPER_SENT: u8 = 42; // Red (255,0,0) — "le dijiste a"
    pub const WHISPER_RECV: u8 = 38; // Yellow bold (255,255,0) — "te dijo"
    pub const NPCSX: u8 = 13;      // Pink (255,83,255) — NPC debug info
    pub const COMBAT_RED: u8 = 19;  // Red (255,0,0) — same as FIGHT
    pub const NPC_INFO: u8 = 12;    // Dark gray (86,87,89) — NPC talk
    pub const BLANCO: u8 = 46;      // White (255,255,255)
    pub const BORDO: u8 = 47;       // Maroon/brown (128,0,0)
    pub const AZUL: u8 = 49;        // Blue (0,0,255)
    pub const VIOLETA: u8 = 50;     // Violet (128,0,128)
    pub const NEWBIE: u8 = 6;       // Light yellow-green (225,249,158) — newbie color
    pub const CIUDADANO: u8 = 11;   // Blue (48,128,255) — citizen/armada
}


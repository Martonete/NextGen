// Protocol module — packet parsing and building.
//
// This module handles the interpretation of decrypted packet data:
// - Parsing client opcodes (ALOGIN, KERD22, AT, LH, etc.)
// - Building server response packets (LOGGED, CC, CM, etc.)
// - Field parsing using delimiters (comma, tilde, etc.)

pub mod fields;

pub use fields::ReadField;

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
    pub const GUILD_BANK_PERMS_QUERY: &str = "VLKG"; // Query bank permissions
    pub const GUILD_BANK_PERMS_SET: &str = "BOVC";  // Set bank permissions
    pub const GUILD_BANK_OPEN: &str = "INIBOV";     // Open guild bank
    pub const GUILD_BANK_SAVE: &str = "CCBG";       // Save guild bank inventory
    pub const GUILD_BANK_DEPOSIT: &str = "CCDO";    // Deposit gold to guild bank
    pub const GUILD_BANK_WITHDRAW: &str = "CCRO";   // Withdraw gold from guild bank
    pub const CLAN_BANK_WITHDRAW_ITEM: &str = "RETB"; // Withdraw item from clan bank
    pub const CLAN_BANK_DEPOSIT_ITEM: &str = "DEPB";  // Deposit item into clan bank

    // Quest opcodes
    pub const QUEST_LIST: &str = "IQUEST";    // Request quest list
    pub const QUEST_INFO: &str = "INFD";      // Get quest details for selection
    pub const QUEST_ACCEPT: &str = "ACQT";    // Accept quest

    // Mail opcodes
    pub const MAIL_SEND: &str = "CZM";       // Send mail
    pub const MAIL_OPEN: &str = "CZC";       // Open/read mail slot
    pub const MAIL_EXTRACT: &str = "CZR";    // Extract items from mail
    pub const MAIL_DELETE: &str = "CZB";      // Delete mail

    // Friend list
    pub const FRIEND_ADD: &str = "ADDCON";    // Add friend
    pub const FRIEND_REMOVE: &str = "BORRAC"; // Remove friend

    // Missing high-priority opcodes (HandleData1/4)
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
    pub const SEND_POINTS: &str = "ACTPT";     // Request tournament/donation points
    pub const RANKINGS: &str = "RANKIN";       // View rankings
    pub const POSITION_UPDATE: &str = "ACTUALIZAR"; // Position re-sync
    pub const DUEL_ARENA_INFO: &str = "IDUELOS";    // Duel arena info
    pub const MACRO_DETECT: &str = "TENGOMACROS";   // Macro detection
    pub const SOS_SEND: &str = "#";            // Send SOS/consultation
    pub const SOS_RESPOND: &str = "X";         // Admin responds to SOS
    pub const SOS_VIEW: &str = "CONSUL";       // Admin view SOS messages
    pub const CLOSE_GUILD_BANK: &str = "FINCBN"; // Close guild bank
    pub const DONATION_MENU: &str = "DCANJE";  // Donation exchange menu
    pub const DONATION_PREVIEW: &str = "DPX";  // Donation item preview
    pub const DONATION_REDEEM: &str = "DRX";   // Redeem donation
    pub const TOURNAMENT_MENU: &str = "CCANJE"; // Tournament prize menu
    pub const PRIZE_INFO: &str = "IPX";        // Prize item info
    pub const PRIZE_BUY: &str = "SPX";         // Buy tournament prize
    pub const FPZ_REPORT: &str = "ENVFPZ";     // FPZ anti-hack report
    pub const DRAG_DROP: &str = "DYDTRA";      // Drag & drop transfer
    pub const CAST_BY_NAME: &str = "DOWNSI";   // Cast spell by target name
    pub const TOINFO: &str = "TOINFO";         // Tournament info
    pub const VOTE: &str = "NVOT";             // Vote in poll
    pub const REPORT: &str = "NEWD";           // New report/denuncia
    pub const INIT_CHAT: &str = "INCHAT";      // Init chat with friend
    pub const CHAT_MSG: &str = "KKCHAT";       // Chat message to friend
    pub const GUILD_DONATE_PTS: &str = "ADDPTS"; // Donate points to guild

    // Missing handlers
    pub const HOUSE_QUERY: &str = "FWO";       // Query house owner/price
    pub const HOUSE_BUY: &str = "CUC";         // Buy house
    pub const PET_RENAME: &str = "CNM";        // Rename pet
    pub const GEM_EXCHANGE: &str = "GEMS";     // Gem exchange (7 gems)
    pub const MEDAL_EXCHANGE: &str = "GEPS";   // Medal exchange
    pub const DIVINE_OFFER: &str = "OFDIOZ";   // Divine offering
    pub const TS_SHOP: &str = "FTSPTS";        // TS points shop
    pub const UPGRADE_QUERY: &str = "SPH";     // Upgrade item info (Mejorados)
    pub const UPGRADE_DO: &str = "SP\u{00C9}";  // Upgrade item (SPÉ)
    pub const ARENA_SPECTATE: &str = "ARE";    // Arena spectate
    pub const CLAN_VALID_NAME: &str = "NANVAME";   // Clan name valid notification
    pub const CLAN_INVALID_NAME: &str = "NANVAMX"; // Clan name invalid notification
    pub const PCGF: &str = "PCGF";            // Party/clan GUI forwarding
    pub const PCWC: &str = "PCWC";            // Party/clan window command
    pub const PCCC: &str = "PCCC";            // Party/clan caption command
}

/// Server packet opcodes — the first characters of each outbound packet.
///
/// These must match EXACTLY what the VB6 client expects to parse.
pub mod server_opcodes {
    pub const ERROR: &str = "ERR";      // Error message (disconnects)
    pub const ERROR_SHOW: &str = "ERO"; // Error message (shows dialog)
    pub const LOGGED: &str = "LOGGED";  // Login successful
    pub const DEAD: &str = "MUERT";     // Character died
    pub const CHANGE_MAP: &str = "CM";  // Switch to new map
    pub const CREATE_CHAR: &str = "CC"; // Create character in view
    pub const REMOVE_CHAR: &str = "BP"; // Remove character from view
    pub const MOVE_CHAR: &str = "MP";   // Move character
    pub const POSITION_UPDATE: &str = "PU"; // Player position
    pub const NPC_HIT_USER: &str = "N2";    // NPC attacks user
    pub const USER_HIT_NPC: &str = "U2";    // User attacks NPC
    pub const CONSOLE_MSG: &str = "P|";     // Console/system message with inline text
    pub const CONSOLE_MSG_ID: &str = "||";  // Console message by text ID (Textos.tsao lookup)
    pub const DICE_ROLL: &str = "DADOS";    // Dice roll result
    pub const FINISH_OK: &str = "FINOK";    // Operation finished
    pub const ACCOUNT_DATA: &str = "ACDA";  // Account data (character list)

    // Login flow packets
    pub const INIT_ACCOUNT: &str = "INIAC"; // Initial account data (num chars + notice)
    pub const ADD_PJ: &str = "ADDPJ";       // Add character to selection screen
    pub const SECURITY_CODE: &str = "CODEH"; // Security code
    pub const PRIVILEGE_LEVEL: &str = "LDG"; // Privilege level
    pub const MAP_MUSIC: &str = "XM";        // Map music ID
    pub const MAP_NAME: &str = "N~";         // Map name
    pub const FRIEND_LIST: &str = "LDM";     // Friend list data

    // Chat packets
    pub const TALK: &str = "T|";        // Talk message (area)
    pub const YELL: &str = "N|";        // Yell message (larger area)
    pub const WHISPER: &str = "P|";     // Private message
    pub const GUILD_CHAT: &str = "G|";  // Guild chat

    // Inventory packets
    pub const INV_SLOT: &str = "CSI";   // Inventory slot data
    pub const INV_INIT: &str = "INVI0"; // Inventory init signal
    pub const SPELL_SLOT: &str = "SHS"; // Spell slot data

    // Stat update packets (brackets are part of the opcode)
    pub const STAT_HP: &str = "[H]";
    pub const STAT_MANA: &str = "[M]";
    pub const STAT_STA: &str = "[S]";
    pub const STAT_GOLD: &str = "[G]";
    pub const STAT_EXP: &str = "[E]";
    pub const STAT_LEVEL: &str = "[L]";
    pub const STAT_NAME: &str = "[N]";
    pub const STAT_BULK: &str = "[ES";  // Bulk stats on login (no closing bracket)

    // Combat packets
    pub const SAFE_ON: &str = "SEGON";
    pub const SAFE_OFF: &str = "SEGOFF";
    pub const SAFE_RESU_ON: &str = "SEGONR";
    pub const SAFE_RESU_OFF: &str = "SEGOFR";
    pub const USER_SWING: &str = "U1";  // User attack missed
    pub const USER_HIT: &str = "U2";    // User dealt damage
    pub const NPC_SWING: &str = "N1";   // NPC attack missed
    pub const NPC_HIT: &str = "N2";     // NPC dealt damage to user
    pub const PVP_DAMAGE_RECV: &str = "N4"; // PvP damage received
    pub const PVP_DAMAGE_DEAL: &str = "N5"; // PvP damage dealt
    pub const USER_MISS: &str = "U3";   // User's attack was evaded
    pub const YOU_DIED: &str = "6";     // Death notification
    pub const PLAY_SOUND: &str = "TW";  // Play sound effect
    pub const CHAR_FX: &str = "CFX";    // Character visual effect
    pub const CHANGE_CHAR: &str = "CP";   // Change character appearance
    pub const CHAR_FX_ALT: &str = "CFF";  // Character FX (alternate format)

    // Ground item packets
    pub const SHOW_OBJ: &str = "HO";    // Show object on ground: HO{grh},{x},{y}
    pub const ERASE_OBJ: &str = "BO";   // Remove object from ground: BO{x},{y}

    // Work/skill packets
    pub const WORK_MODE: &str = "T01";           // Activate work mode: T01<skillId>
    pub const OPEN_SMITH: &str = "SFH";          // Open blacksmith UI
    pub const OPEN_CARP: &str = "SFC";           // Open carpentry UI
    pub const SMITH_WEAPONS: &str = "LAH";       // Buildable weapons list
    pub const SMITH_ARMORS: &str = "LAR";        // Buildable armors list
    pub const CARP_ITEMS: &str = "OBR";          // Buildable carpentry items
    pub const MED_OK: &str = "MEDOK";            // Stop meditation
    pub const HIDE_CHAR: &str = "NOVER";         // Toggle visibility: NOVER<charIndex>,<0|1>
    pub const NAVIGATION: &str = "NAVEG";        // Toggle navigation
    pub const INIT_BANK: &str = "INITBANCO";    // Open bank window
    pub const BANK_SLOT: &str = "SBO";          // Bank slot data
    pub const BANK_GOLD: &str = "[BG";          // Bank gold amount
    pub const BANK_CLOSE_OK: &str = "FINBANOK"; // Bank close confirmation

    // Player trading packets
    pub const TRADE_INIT: &str = "ICO";         // Init commerce (trade started)
    pub const TRADE_OFFER_RECV: &str = "IOR";   // Received trade offer (gold + items)
    pub const TRADE_ITEMS: &str = "ICI";        // Trade inventory items
    pub const TRADE_CHAT_MSG: &str = "VCC";     // Trade chat message
    pub const TRADE_OK: &str = "TRADEOK";       // Trade completed
    pub const TRADE_CANCEL_OK: &str = "CANCELTRADE"; // Trade cancelled

    // Commerce packets
    pub const NPC_INV_RESET: &str = "NPCR";     // Reset NPC inventory display
    pub const NPC_INV_ITEM: &str = "NPCI";       // NPC inventory item data
    pub const NPC_INV_SLOT: &str = "NPC|";       // Single NPC inv slot update
    pub const INIT_COMMERCE: &str = "INITCOM";   // Open commerce window
    pub const TRANS_OK: &str = "TRANSOK";        // Transaction success
    pub const COMMERCE_CLOSE_OK: &str = "FINCOMOK"; // Commerce close confirmation

    // Guild packets
    pub const GUILD_LIST: &str = "GL";              // Guild list: GL<count>,<name>-<align>-<level>,...
    pub const GUILD_INFO_LEADER: &str = "IREDAEL";  // Guild info for leader/sublider
    pub const GUILD_INFO_MEMBER: &str = "IREDAEK";  // Guild info for regular member
    pub const GUILD_SHOW_FORM: &str = "SHOWFUN";    // Show guild creation form
    pub const GUILD_DETAILS_RESP: &str = "DTLC";    // Guild details response
    pub const GUILD_BANK_PERMS_RESP: &str = "KHEKD"; // Bank permissions response
    pub const CLAN_CHAT: &str = "C|";               // Clan chat message

    // Quest packets
    pub const QUEST_LIST_RESP: &str = "QTL";         // Quest list: QTL{num},{name1},{name2},...
    pub const QUEST_CURRENT: &str = "MQC";           // Current quest progress
    pub const QUEST_SELECTED: &str = "MQS";          // Quest details for selection
    pub const QUEST_NPC_LIST: &str = "DAMEQUEST";    // NPC quest list signal

    // Mail response packets
    pub const MAIL_LIST: &str = "IFO";        // Mail list header
    pub const MAIL_PLAYER_INFO: &str = "IDO"; // Player info + inventory
    pub const MAIL_FRIENDS: &str = "IAO";     // Friend list for quick select
    pub const MAIL_CONTENT: &str = "ILO";     // Full mail content
    pub const MAIL_ITEMS: &str = "ITO";       // Mail items list
}

/// Font type constants — appended to chat messages as ~r~g~b~bold~italic.
/// Must match VB6 Declares.bas font types.
pub mod font_types {
    pub const TALK: &str = "~255~255~255~0~0";       // White, normal
    pub const TALK_GM: &str = "~255~255~0~1~0";      // Yellow, bold (GM talk)
    pub const TALK_DEAD: &str = "~192~192~192~0~1";  // Gray, italic (dead)
    pub const YELL: &str = "~255~0~0~1~0";           // Red, bold
    pub const WHISPER_SENT: &str = "~255~0~0~0~1";   // Red, italic (you told)
    pub const WHISPER_RECV: &str = "~255~255~0~1~0";  // Yellow, bold (told you)
    pub const SYSTEM: &str = "~0~255~0~0~0";         // Green, normal
    pub const GUILD: &str = "~0~255~128~1~0";        // Teal, bold
    pub const COMBAT: &str = "~255~0~0~0~0";         // Red, normal
    pub const INFO: &str = "~65~190~156~0~0";        // Teal/info
    pub const CLAN_MEMBER: &str = "~36~255~233~0~0";  // Teal (clan member chat)
    pub const CLAN_LEADER: &str = "~255~0~0~0~0";     // Red (clan leader chat)
    pub const GUILD_MSG: &str = "~228~199~27~0~0";    // Yellow-gold (guild messages)
}

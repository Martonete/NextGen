/// Binary Protocol — Packet IDs.
///
/// These are numeric IDs that map 1:1 to our existing text opcodes.
/// The game logic is IDENTICAL — only the wire encoding changes from text to binary.
/// Each packet starts with a 1-byte opcode followed by typed binary fields.

// ── Client → Server ────────────────────────────────────────

/// Client → Server packet IDs.
/// Maps our existing text opcodes to sequential byte IDs.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ClientPacketID {
    // Pre-login (0-9)
    HardwareCheck = 0,   // Hardware serial check
    AccountLogin = 1,    // Account login
    CreateCharacter = 2, // New character creation + enter world
    CharacterLogin = 3,  // Character login (select existing char)
    CharacterSelect = 4, // Character login (primary, with full validation)
    CreateAccount = 5,   // New account creation
    ChangePassword = 6,  // Password change
    AccountRecovery = 7, // Account recovery
    RollDice = 8,        // Roll dice (attributes)
    DeleteCharacter = 9, // Delete character

    // Movement / Position (10-14)
    Walk = 10,          // M<1-4> → Walk with heading byte
    ChangeHeading = 11, // CHEA<1-4> → change heading
    RequestPos = 12,    // RPU → request position update
    SyncPosition = 13,  // ACTUALIZAR → position re-sync

    // Combat (20-24)
    Attack = 20,        // AT → attack
    CastSpell = 21,     // LH → cast spell
    LeftClick = 22,     // LC → left click (target)
    RightClick = 23,    // RC → right click
    WorkLeftClick = 24, // WLC → work left click (skill + target)

    // Chat (30-35)
    Talk = 30,         // ; → area chat
    Yell = 31,         // - → yell
    Whisper = 32,      // \ → private message
    SlashCommand = 33, // / → slash commands (GM, /ONLINE, etc.)

    // Items / Inventory (40-49)
    PickUp = 40,       // AG → grab item
    DropItem = 41,     // TI → drop item
    UseItem = 42,      // USA → use item
    UseItemClick = 43, // QSA → use item via dblclick
    EquipItem = 44,    // EQUI → equip item
    SwapItems = 45,    // SWAP → swap inventory slots
    MouseDrop = 46,    // TR → drop item via mouse drag

    // Skills (50-55)
    UseSkill = 50,    // UK → use skill
    SkillSet = 51,    // SKSE → distribute skill points
    Meditate = 52,    // ME → toggle meditation
    SafeToggle = 53,  // SEG → toggle PvP safety
    MacroDetect = 54, // TENGOMACROS → macro detection
    LevelBonus = 55,  // BOF → level bonus selection

    // Spells (60-63)
    SpellInfo = 60,  // INFS → spell info
    MoveSpell = 61,  // DESPHE → move spell position
    CastByName = 62, // DOWNSI → cast spell by target name

    // Commerce (70-79)
    CommerceBuy = 70,    // COMP → buy from NPC
    CommerceSell = 71,   // VEND → sell to NPC
    CommerceClose = 72,  // FINCOM → close commerce window
    TradeOfferGold = 73, // UOR → offer gold in trade
    TradeOfferItem = 74, // UOC → offer items in trade
    TradeResponse = 75,  // TDR → accept/reject/cancel trade
    TradeCancel = 76,    // TCM → cancel trade
    TradeChat = 77,      // VHC → trade chat message

    // Banking (80-85)
    BankDeposit = 80,  // DEPO → deposit item to bank
    BankWithdraw = 81, // RETI → withdraw item from bank
    BankClose = 82,    // FINBAN → close bank window
    Pong = 88,

    // Crafting (90-94)
    ConstructSmith = 90, // CNS → construct blacksmith item
    ConstructCarp = 91,  // CNC → construct carpentry item
    TrainCreature = 92,  // ENTR → train creature from trainer

    // Guild (100-119)
    GuildInfo = 100,        // GLINFO
    GuildCreate = 101,      // CIG
    GuildUpdateCodex = 102, // DESCOD
    GuildAccept = 103,      // ACEPTARI
    GuildReject = 104,      // RECHAZAR
    GuildExpel = 105,       // ECHARCLA
    GuildNews = 106,        // ACTGNEWS
    GuildApply = 107,       // SOLICITUD
    GuildDetails = 108,     // CLANDETAILS

    // Forum (123)
    ForumPost = 123, // DEMSG — post to forum

    // Player info (140-149)
    PlayerInfo = 140, // DAMINF
    MiniStats = 141,  // FEST
    HeadChange = 142, // CABEZI
    Rankings = 143,   // RANKIN

    // Misc (150-169)
    HouseQuery = 150, // FWO
    HouseBuy = 151,   // CUC
    PetRename = 152,  // CNM
    DragDrop = 160,   // DYDTRA
    Vote = 161,       // NVOT
    Report = 162,     // NEWD
    SosView = 163,    // CONSUL
    SosSend = 164,    // #
    SosRespond = 165, // X
}

// ── Server → Client ────────────────────────────────────────

/// Server → Client packet IDs.
/// Names match the binary_packets.rs builder functions.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ServerPacketID {
    // Auth / Login (0-11)
    Logged = 0,
    Disconnect = 1,
    ErrorMsg = 2,
    ShowMessageBox = 3,
    UserIndexInServer = 4,
    UserCharIndexInServer = 5,
    CommerceEnd = 6,
    BankEnd = 7,
    CommerceInit = 8,
    BankInit = 9,
    UserCommerceInit = 10,
    UserCommerceEnd = 11,
    UserOfferConfirm = 12,
    CommerceChat = 13,
    ShowBlacksmithForm = 14,
    ShowCarpenterForm = 15,
    UpdateSta = 16,
    UpdateMana = 17,
    UpdateHP = 18,
    UpdateGold = 19,
    UpdateExp = 20,
    ChangeMap = 21,
    PosUpdate = 22,
    ChatOverHead = 23,
    ConsoleMsg = 24,
    GuildChat = 25,
    ShowMessageBox2 = 26, // Alias for !! in-game
    UserIndexAlt = 27,
    UserCharIndexAlt = 28,
    CharacterCreate = 29,
    CharacterRemove = 30,
    CharacterMove = 31,
    ForceCharMove = 32,
    CharacterChange = 33,
    ObjectCreate = 34,
    ObjectDelete = 35,
    BlockPosition = 36,
    PlayMusic = 37,
    PlayWave = 38,
    GuildList = 39,
    AreaChanged = 40,
    PauseToggle = 41,
    RainToggle = 42,
    CreateFX = 43,
    UpdateUserStats = 44,
    WorkRequestTarget = 45,
    ChangeInventorySlot = 46,
    ChangeBankSlot = 47,
    ChangeSpellSlot = 48,
    Attributes = 49,
    SendSkills = 50,
    ChangeNPCInventorySlot = 51,
    // 52-53 reserved for craft lists
    RestToggle = 54,
    ErrorShow = 55,
    Blind = 56,
    Silence = 57,
    ShowSignal = 58,
    DiceRoll = 59,
    UpdateHungerAndThirst = 60,
    Fame = 61,
    MiniStats = 62,
    LevelUp = 63,
    AddCharPreview = 64,
    SecurityCode = 65,
    SetInvisible = 66,
    InitAccount = 67,
    DiceRollAlt = 68,
    MeditateToggle = 69,
    BlindNoMore = 70,
    SilenceEnd = 71,
    TrainerCreatureList = 72,
    GuildNews = 73,
    PrivilegeLevel = 74,
    CharacterInfo = 75,
    AccountData = 76,
    FinishOK = 77,
    Dead = 78,
    RemoveDialogs = 79,
    RemoveCharDialog = 80,
    NavigateToggle = 81,
    ParalyzeOK = 82,
    ShowGuildFoundationForm = 83,
    TradeOK = 84,
    BankOK = 85,
    ChangeUserTradeSlot = 86,
    SendNight = 87,
    Pong = 88,
    UpdateTagAndStatus = 89,
    SpawnList = 90,
    ShowSOSForm = 91,
    ShowMOTDEditionForm = 92,
    ShowGMPanelForm = 93,
    UserNameList = 94,
    ShowGuildAlign = 95,
    MapMusic = 96,
    MapName = 97,
    CharData = 98,
    AuraUpdate = 99,
    StopWorking = 100,
    UpdateStrengthAndDexterity = 101,
    UpdateBankGold = 102,
    AddSlots = 103,
    MultiMessage = 104,
    CancelOfferItem = 105,
    ShowPartyForm = 106,
    HeadingChange = 107,
    Arrow = 108,
    NavigateBroadcast = 109,

    // Chat variants (used by text-based handlers still)
    ChatTalk = 110,
    ChatYell = 111,
    ChatWhisper = 112,
    ChatGuild2 = 113,
    ChatClan = 114,
    ConsoleMsgId = 115,
    GmBroadcast = 116,

    // Forum
    AddForumMsg = 117,
    ShowForumForm = 118,

    // Pets
    PetsUpdate = 119,

    // Stat variants
    StatHP = 120,
    StatMana = 121,
    StatSta = 122,
    StatGold = 123,
    StatExp = 124,
    StatLevel = 125,
    StatName = 126,
    StatBulk = 127,
    HungerThirst = 128,
    OnlineCount = 129,

    // Combat messages
    SafeOn = 130,
    SafeOff = 131,
    SafeResuOn = 132,
    SafeResuOff = 133,
    UserSwing = 134,
    UserHit = 135,
    NpcSwing = 136,
    NpcHit = 137,
    PvpDmgRecv = 138,
    PvpDmgDeal = 139,
    UserMiss = 140,
    YouDied = 141,
    UserMount = 142,
    Levitate = 143,

    // Inventory/Spells (legacy IDs)
    InvSlot = 145,
    InvInit = 146,
    SpellSlot = 147,
    SpellInfoResp = 148,

    // Sound (legacy)
    PlaySound = 150,

    // Work/Skills (legacy)
    WorkMode = 155,
    OpenSmith = 156,
    OpenCarp = 157,
    SmithWeapons = 158,
    SmithArmors = 159,
    CarpItems = 160,
    MeditateOK = 161,
    Navigation = 162,
    AmbientColor = 164, // PCR: map ambient RGB color

    // Bank (legacy)
    InitBankLegacy = 165,
    BankSlotLegacy = 166,
    BankGoldLegacy = 167,
    // Commerce (legacy)
    NpcInvReset = 170,
    NpcInvItem = 171,
    NpcInvSlotLegacy = 172,
    InitCommerceLegacy = 173,
    TransactionOK = 174,
    ResponseMsg = 177,
    AuctionBid = 179,

    // Trading (legacy)
    TradeInitLegacy = 180,
    TradeOfferRecv = 181,
    TradeItems = 182,
    TradeChatMsgLegacy = 183,
    TradeOKLegacy = 184,
    // Guild (legacy)
    GuildListLegacy = 190,
    GuildInfoLeader = 191,
    GuildInfoMember = 192,
    GuildShowForm = 193,
    GuildDetailsResp = 194,
    ClanChatResp = 196,
    GuildBankSlotData = 197, // SBG: full guild bank slot data (slot+item+gold)

    // Spell travel beam (cosmetic light beam caster→target; drawn client-side)
    SpellBeam = 198,

    // Character particles
    CharParticleCreate = 211,

    // Misc
    StopDancing = 220,
    MenuData = 221,
    SelectData = 222,
    ImageData = 224,
    AnimData = 225,
    BkwData = 226,
    FestData = 227,
    GinfData = 230,
    IcoData = 231,
    ZsosData = 232,
    RptData = 233,
    SbrData = 234,
    CosmeticSurgery = 238,
    CosmeticPcgn = 240,
    CosmeticPcss = 241,
    CosmeticPccc = 242,
    ParticleCreate = 243,
    LightCreate = 244,
    FullCharInfo = 245,
    TimerInfo = 246,
    GuildBankInitResp = 247,
    GuildBankSlotResp = 248,
    GuildBankGoldResp = 249,

    // Ping
    Ping = 250,
    TravelsOpen = 251,

    // Zones
    ZoneChange = 252,

    // Generic text fallback (for any remaining text-based packets)
    GenericText = 255,
}

impl ClientPacketID {
    /// Convert byte to ClientPacketID, returns None for unmapped IDs.
    pub fn from_byte(b: u8) -> Option<Self> {
        match b {
            0 => Some(Self::HardwareCheck),
            1 => Some(Self::AccountLogin),
            2 => Some(Self::CreateCharacter),
            3 => Some(Self::CharacterLogin),
            4 => Some(Self::CharacterSelect),
            5 => Some(Self::CreateAccount),
            6 => Some(Self::ChangePassword),
            7 => Some(Self::AccountRecovery),
            8 => Some(Self::RollDice),
            9 => Some(Self::DeleteCharacter),
            10 => Some(Self::Walk),
            11 => Some(Self::ChangeHeading),
            12 => Some(Self::RequestPos),
            13 => Some(Self::SyncPosition),
            20 => Some(Self::Attack),
            21 => Some(Self::CastSpell),
            22 => Some(Self::LeftClick),
            23 => Some(Self::RightClick),
            24 => Some(Self::WorkLeftClick),
            30 => Some(Self::Talk),
            31 => Some(Self::Yell),
            32 => Some(Self::Whisper),
            33 => Some(Self::SlashCommand),
            40 => Some(Self::PickUp),
            41 => Some(Self::DropItem),
            42 => Some(Self::UseItem),
            43 => Some(Self::UseItemClick),
            44 => Some(Self::EquipItem),
            45 => Some(Self::SwapItems),
            46 => Some(Self::MouseDrop),
            50 => Some(Self::UseSkill),
            51 => Some(Self::SkillSet),
            52 => Some(Self::Meditate),
            53 => Some(Self::SafeToggle),
            54 => Some(Self::MacroDetect),
            55 => Some(Self::LevelBonus),
            60 => Some(Self::SpellInfo),
            61 => Some(Self::MoveSpell),
            62 => Some(Self::CastByName),
            70 => Some(Self::CommerceBuy),
            71 => Some(Self::CommerceSell),
            72 => Some(Self::CommerceClose),
            73 => Some(Self::TradeOfferGold),
            74 => Some(Self::TradeOfferItem),
            75 => Some(Self::TradeResponse),
            76 => Some(Self::TradeCancel),
            77 => Some(Self::TradeChat),
            80 => Some(Self::BankDeposit),
            81 => Some(Self::BankWithdraw),
            82 => Some(Self::BankClose),
            88 => Some(Self::Pong),
            90 => Some(Self::ConstructSmith),
            91 => Some(Self::ConstructCarp),
            92 => Some(Self::TrainCreature),
            100 => Some(Self::GuildInfo),
            101 => Some(Self::GuildCreate),
            102 => Some(Self::GuildUpdateCodex),
            103 => Some(Self::GuildAccept),
            104 => Some(Self::GuildReject),
            105 => Some(Self::GuildExpel),
            106 => Some(Self::GuildNews),
            107 => Some(Self::GuildApply),
            108 => Some(Self::GuildDetails),
            123 => Some(Self::ForumPost),
            140 => Some(Self::PlayerInfo),
            141 => Some(Self::MiniStats),
            142 => Some(Self::HeadChange),
            143 => Some(Self::Rankings),
            150 => Some(Self::HouseQuery),
            151 => Some(Self::HouseBuy),
            152 => Some(Self::PetRename),
            160 => Some(Self::DragDrop),
            161 => Some(Self::Vote),
            162 => Some(Self::Report),
            163 => Some(Self::SosView),
            164 => Some(Self::SosSend),
            165 => Some(Self::SosRespond),
            _ => None,
        }
    }

    pub fn to_byte(self) -> u8 {
        self as u8
    }
}

impl ServerPacketID {
    pub fn to_byte(self) -> u8 {
        self as u8
    }
}

// ── MultiMessage sub-types ─────────────────────────────────

/// Sub-type IDs for the MultiMessage packet (ServerPacketID::MultiMessage = 104).
/// Used for combat notifications and other compound messages.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum MultiMessageID {
    NPCSwing = 0,
    NPCKillUser = 1,
    BlockedWithShieldUser = 2,
    BlockedWithShieldOther = 3,
    UserSwing = 4,
    SafeModeOn = 5,
    SafeModeOff = 6,
    ResuscitationSafeOff = 7,
    ResuscitationSafeOn = 8,
    NobilityLost = 9,
    CantUseWhileMeditating = 10,
    NPCHitUser = 12,
    UserHitNPC = 13,
    UserAttackedSwing = 14,
    UserHittedByUser = 15,
    UserHittedUser = 16,
    WorkRequestTarget = 17,
    HaveKilledUser = 18,
    UserKill = 19,
    EarnExp = 20,
    GoHome = 21,
    CancelGoHome = 22,
    FinishHome = 23,
}

impl MultiMessageID {
    pub fn to_byte(self) -> u8 {
        self as u8
    }

    pub fn from_byte(b: u8) -> Option<Self> {
        match b {
            0 => Some(Self::NPCSwing),
            1 => Some(Self::NPCKillUser),
            2 => Some(Self::BlockedWithShieldUser),
            3 => Some(Self::BlockedWithShieldOther),
            4 => Some(Self::UserSwing),
            5 => Some(Self::SafeModeOn),
            6 => Some(Self::SafeModeOff),
            7 => Some(Self::ResuscitationSafeOff),
            8 => Some(Self::ResuscitationSafeOn),
            9 => Some(Self::NobilityLost),
            10 => Some(Self::CantUseWhileMeditating),
            12 => Some(Self::NPCHitUser),
            13 => Some(Self::UserHitNPC),
            14 => Some(Self::UserAttackedSwing),
            15 => Some(Self::UserHittedByUser),
            16 => Some(Self::UserHittedUser),
            17 => Some(Self::WorkRequestTarget),
            18 => Some(Self::HaveKilledUser),
            19 => Some(Self::UserKill),
            20 => Some(Self::EarnExp),
            21 => Some(Self::GoHome),
            22 => Some(Self::CancelGoHome),
            23 => Some(Self::FinishHome),
            _ => None,
        }
    }
}


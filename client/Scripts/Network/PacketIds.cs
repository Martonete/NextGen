namespace ArgentumNextgen.Network;

/// <summary>
/// Client → Server packet IDs (13.3 binary protocol).
/// Must match the Rust server's ClientPacketID enum exactly.
/// </summary>
public static class ClientPacketId
{
    // Pre-login (0-9)
    public const byte HardwareCheck = 0;
    public const byte AccountLogin = 1;
    public const byte CreateCharacter = 2;
    public const byte CharacterLogin = 3;
    public const byte CharacterSelect = 4;
    public const byte CreateAccount = 5;
    public const byte ChangePassword = 6;
    public const byte AccountRecovery = 7;
    public const byte RollDice = 8;
    public const byte DeleteCharacter = 9;

    // Movement (10-14)
    public const byte Walk = 10;
    public const byte ChangeHeading = 11;
    public const byte RequestPos = 12;
    public const byte SyncPosition = 13;

    // Combat (20-24)
    public const byte Attack = 20;
    public const byte CastSpell = 21;
    public const byte LeftClick = 22;
    public const byte RightClick = 23;
    public const byte WorkLeftClick = 24;

    // Chat (30-35)
    public const byte Talk = 30;
    public const byte Yell = 31;
    public const byte Whisper = 32;
    public const byte SlashCommand = 33;

    // Items (40-49)
    public const byte PickUp = 40;
    public const byte DropItem = 41;
    public const byte UseItem = 42;
    public const byte UseItemClick = 43;
    public const byte EquipItem = 44;
    public const byte SwapItems = 45;
    public const byte MouseDrop = 46;

    // Skills (50-55)
    public const byte UseSkill = 50;
    public const byte SkillSet = 51;
    public const byte Meditate = 52;
    public const byte SafeToggle = 53;
    public const byte MacroDetect = 54;
    public const byte LevelBonus = 55;

    // Spells (60-63)
    public const byte SpellInfo = 60;
    public const byte MoveSpell = 61;
    public const byte CastByName = 62;

    // Commerce (70-79)
    public const byte CommerceBuy = 70;
    public const byte CommerceSell = 71;
    public const byte CommerceClose = 72;
    public const byte TradeOfferGold = 73;
    public const byte TradeOfferItem = 74;
    public const byte TradeResponse = 75;
    public const byte TradeCancel = 76;
    public const byte TradeChat = 77;

    // Banking (80-85)
    public const byte BankDeposit = 80;
    public const byte BankWithdraw = 81;
    public const byte BankClose = 82;

    // Crafting (90-94)
    public const byte ConstructSmith = 90;
    public const byte ConstructCarp = 91;
    public const byte TrainCreature = 92;

    // Guild (100-119)
    public const byte GuildInfo = 100;
    public const byte GuildCreate = 101;
    public const byte GuildUpdateCodex = 102;
    public const byte GuildAccept = 103;
    public const byte GuildReject = 104;
    public const byte GuildExpel = 105;
    public const byte GuildNewsReq = 106;
    public const byte GuildApply = 107;
    public const byte GuildDetails = 108;
    public const byte GuildBankPermsQuery = 109;
    public const byte GuildBankPermsSet = 110;
    public const byte GuildBankOpen = 111;
    public const byte GuildBankSave = 112;
    public const byte GuildBankDeposit = 113;
    public const byte GuildBankWithdraw = 114;
    public const byte ClanBankWithdrawItem = 115;
    public const byte ClanBankDepositItem = 116;
    public const byte CloseGuildBank = 117;
    public const byte GuildDonatePts = 118;
    public const byte ClanValidName = 119;

    // Quest (120-122)
    public const byte QuestList = 120;
    public const byte QuestInfo = 121;
    public const byte QuestAccept = 122;

    // Forum (123)
    public const byte ForumPost = 123;

    // Chat rooms (132-133)
    public const byte InitChat = 132;
    public const byte ChatMsg = 133;

    // Player info (140-149)
    public const byte PlayerInfo = 140;
    public const byte MiniStatsReq = 141;
    public const byte HeadChange = 142;
    public const byte Rankings = 143;
    public const byte SendPoints = 144;
    public const byte DuelArenaInfo = 145;
    public const byte ToInfo = 146;

    // Misc (150+)
    public const byte HouseQuery = 150;
    public const byte HouseBuy = 151;
    public const byte PetRename = 152;
    public const byte GemExchange = 153;
    public const byte MedalExchange = 154;
    public const byte DivineOffer = 155;
    public const byte TsShop = 156;
    public const byte UpgradeQuery = 157;
    public const byte UpgradeDo = 158;
    public const byte ArenaSpectate = 159;
    public const byte DragDrop = 160;
    public const byte Vote = 161;
    public const byte Report = 162;
    public const byte SosView = 163;
    public const byte SosSend = 164;
    public const byte SosRespond = 165;
    public const byte DonationMenu = 166;
    public const byte DonationPreview = 167;
    public const byte DonationRedeem = 168;
    public const byte TournamentMenu = 169;
    public const byte PrizeInfo = 170;
    public const byte PrizeBuy = 171;
    public const byte FpzReport = 172;
    public const byte ClanInvalidName = 173;
    public const byte PCGF = 174;
    public const byte PCWC = 175;
    public const byte PCCC = 176;
}

/// <summary>
/// Server → Client packet IDs (13.3 binary protocol).
/// Must match the Rust server's ServerPacketID enum exactly.
/// </summary>
public static class ServerPacketId
{
    public const byte Logged = 0;
    public const byte Disconnect = 1;
    public const byte ErrorMsg = 2;
    public const byte ShowMessageBox = 3;
    public const byte UserIndexInServer = 4;
    public const byte UserCharIndexInServer = 5;
    public const byte CommerceEnd = 6;
    public const byte BankEnd = 7;
    public const byte CommerceInit = 8;
    public const byte BankInit = 9;
    public const byte UserCommerceInit = 10;
    public const byte UserCommerceEnd = 11;
    public const byte UserOfferConfirm = 12;
    public const byte CommerceChat = 13;
    public const byte ShowBlacksmithForm = 14;
    public const byte ShowCarpenterForm = 15;
    public const byte UpdateSta = 16;
    public const byte UpdateMana = 17;
    public const byte UpdateHP = 18;
    public const byte UpdateGold = 19;
    public const byte UpdateExp = 20;
    public const byte ChangeMap = 21;
    public const byte PosUpdate = 22;
    public const byte ChatOverHead = 23;
    public const byte ConsoleMsg = 24;
    public const byte GuildChat = 25;
    public const byte CharacterCreate = 29;
    public const byte CharacterRemove = 30;
    public const byte CharacterMove = 31;
    public const byte ForceCharMove = 32;
    public const byte CharacterChange = 33;
    public const byte ObjectCreate = 34;
    public const byte ObjectDelete = 35;
    public const byte BlockPosition = 36;
    public const byte PlayMusic = 37;
    public const byte PlayWave = 38;
    public const byte GuildList = 39;
    public const byte AreaChanged = 40;
    public const byte PauseToggle = 41;
    public const byte RainToggle = 42;
    public const byte CreateFX = 43;
    public const byte UpdateUserStats = 44;
    public const byte WorkRequestTarget = 45;
    public const byte ChangeInventorySlot = 46;
    public const byte ChangeBankSlot = 47;
    public const byte ChangeSpellSlot = 48;
    public const byte Attributes = 49;
    public const byte SendSkills = 50;
    public const byte ChangeNPCInventorySlot = 51;
    public const byte RestToggle = 54;
    public const byte ErrorShow = 55;
    public const byte Blind = 56;
    public const byte Silence = 57;
    public const byte ShowSignal = 58;
    public const byte DiceRoll = 59;
    public const byte UpdateHungerAndThirst = 60;
    public const byte Fame = 61;
    public const byte MiniStats = 62;
    public const byte LevelUp = 63;
    public const byte AddCharPreview = 64;
    public const byte SecurityCode = 65;
    public const byte SetInvisible = 66;
    public const byte InitAccount = 67;
    public const byte MeditateToggle = 69;
    public const byte BlindNoMore = 70;
    public const byte SilenceEnd = 71;
    public const byte TrainerCreatureList = 72;
    public const byte GuildNews = 73;
    public const byte PrivilegeLevel = 74;
    public const byte CharacterInfo = 75;
    public const byte AccountData = 76;
    public const byte FinishOK = 77;
    public const byte Dead = 78;
    public const byte RemoveDialogs = 79;
    public const byte RemoveCharDialog = 80;
    public const byte NavigateToggle = 81;
    public const byte ParalyzeOK = 82;
    public const byte ShowGuildFoundationForm = 83;
    public const byte TradeOK = 84;
    public const byte BankOK = 85;
    public const byte ChangeUserTradeSlot = 86;
    public const byte SendNight = 87;
    public const byte Pong = 88;
    public const byte UpdateTagAndStatus = 89;
    public const byte SpawnList = 90;
    public const byte ShowSOSForm = 91;
    public const byte ShowMOTDEditionForm = 92;
    public const byte ShowGMPanelForm = 93;
    public const byte UserNameList = 94;
    public const byte ShowGuildAlign = 95;
    public const byte MapMusic = 96;
    public const byte MapName = 97;
    public const byte CharData = 98;
    public const byte AuraUpdate = 99;
    public const byte StopWorking = 100;
    public const byte UpdateStrengthAndDexterity = 101;
    public const byte UpdateBankGold = 102;
    public const byte AddSlots = 103;
    public const byte MultiMessage = 104;
    public const byte CancelOfferItem = 105;
    public const byte ShowPartyForm = 106;

    // Chat variants
    public const byte ChatTalk = 110;
    public const byte ChatYell = 111;
    public const byte ChatWhisper = 112;
    public const byte ChatClan = 114;
    public const byte ConsoleMsgId = 115;
    public const byte GmBroadcast = 116;

    // Forum
    public const byte AddForumMsg = 117;
    public const byte ShowForumForm = 118;

    // Stat variants
    public const byte OnlineCount = 129;

    // ── Extended opcodes (added during binary migration) ──────────────

    /// HeadingChange — broadcast char heading to area (|H text opcode). ID 107.
    public const byte HeadingChange = 107;

    /// UserMount — mount/dismount state for a char (USM text opcode). ID 142.
    public const byte UserMount = 142;

    /// Levitate — levitation state for a char (MVOL text opcode). ID 143.
    public const byte Levitate = 143;

    /// ClassOptions — class bonus options at levels 53/56/60 (99 text opcode). ID 144.
    public const byte ClassOptions = 144;

    /// InvInit — inventory initialisation signal (INVI0 text opcode). ID 146.
    public const byte InvInit = 146;

    /// StatLevel — current level update ([L] text opcode). ID 125.
    public const byte StatLevel = 125;

    /// StopDancing — stop/unfreeze movement flag (STOPD text opcode). ID 220.
    public const byte StopDancing = 220;

    /// AnimData — equipment hitbox stats (ANM text opcode). ID 225.
    public const byte AnimData = 225;

    /// RptData — reputation value (RPT text opcode). ID 233.
    public const byte RptData = 233;

    /// TimerInfo — scroll/timer slot data (TIS text opcode). ID 246.
    public const byte TimerInfo = 246;

    /// CharParticleCreate — character particle stream (CFF/PCB). ID 211.
    public const byte CharParticleCreate = 211;

    /// ParticleCreate — map particle effect (PCF text opcode). ID 243.
    public const byte ParticleCreate = 243;

    /// LightCreate — map light effect (PCL text opcode). ID 244.
    public const byte LightCreate = 244;

    // ── Auth / Login (continued) ──────────────────────────────

    /// ShowMessageBox2 — alias for ShowMessageBox used by !! in-game. ID 26.
    public const byte ShowMessageBox2 = 26;

    /// UserIndexAlt — alternate UserIndexInServer (also ID 27 on some code paths). ID 27.
    public const byte UserIndexAlt = 27;

    /// UserCharIndexAlt — alternate UserCharIndex. ID 28.
    public const byte UserCharIndexAlt = 28;

    /// DiceRollAlt — alternative dice roll result (same fields as DiceRoll). ID 68.
    public const byte DiceRollAlt = 68;

    // ── Movement / Projectiles ───────────────────────────────

    /// Arrow — projectile arrow (FLECHI). ID 108.
    public const byte Arrow = 108;

    /// NavigateBroadcast — broadcast navigation state for a char (NVG). ID 109.
    public const byte NavigateBroadcast = 109;

    // ── Chat variants (continued) ────────────────────────────

    /// ChatGuild2 — second guild chat channel. ID 113.
    public const byte ChatGuild2 = 113;

    // ── Stat variants ────────────────────────────────────────

    /// StatHP — individual HP update ([H] stat). ID 120.
    public const byte StatHP = 120;

    /// StatMana — individual mana update ([M] stat). ID 121.
    public const byte StatMana = 121;

    /// StatSta — individual stamina update ([S] stat). ID 122.
    public const byte StatSta = 122;

    /// StatGold — individual gold update ([G] stat). ID 123.
    public const byte StatGold = 123;

    /// StatExp — individual exp update ([E] stat). ID 124.
    public const byte StatExp = 124;

    /// StatName — character name update. ID 126.
    public const byte StatName = 126;

    /// StatBulk — bulk / carry weight. ID 127.
    public const byte StatBulk = 127;

    /// HungerThirst — hunger/thirst update. ID 128.
    public const byte HungerThirst = 128;

    // ── Safe / Combat state ──────────────────────────────────

    /// SafeOn — safe mode enabled. ID 130.
    public const byte SafeOn = 130;

    /// SafeOff — safe mode disabled. ID 131.
    public const byte SafeOff = 131;

    /// SafeResuOn — resurrection safe enabled. ID 132.
    public const byte SafeResuOn = 132;

    /// SafeResuOff — resurrection safe disabled. ID 133.
    public const byte SafeResuOff = 133;

    /// UserSwing — player missed attack (swing). ID 134.
    public const byte UserSwing = 134;

    /// UserHit — player landed a hit. ID 135.
    public const byte UserHit = 135;

    /// NpcSwing — NPC missed attack. ID 136.
    public const byte NpcSwing = 136;

    /// NpcHit — NPC landed a hit. ID 137.
    public const byte NpcHit = 137;

    /// PvpDmgRecv — PvP damage received. ID 138.
    public const byte PvpDmgRecv = 138;

    /// PvpDmgDeal — PvP damage dealt. ID 139.
    public const byte PvpDmgDeal = 139;

    /// UserMiss — user attack missed. ID 140.
    public const byte UserMiss = 140;

    /// YouDied — player death notification. ID 141.
    public const byte YouDied = 141;

    // ── Inventory / Spells (legacy IDs) ─────────────────────

    /// InvSlot — individual inventory slot update. ID 145.
    public const byte InvSlot = 145;

    /// SpellSlot — individual spell slot update. ID 147.
    public const byte SpellSlot = 147;

    /// SpellInfoResp — spell info response. ID 148.
    public const byte SpellInfoResp = 148;

    // ── Sound ────────────────────────────────────────────────

    /// PlaySound — play sound effect. ID 150.
    public const byte PlaySound = 150;

    // ── Work / Crafting ──────────────────────────────────────

    /// WorkMode — work mode toggle (T01). ID 155.
    public const byte WorkMode = 155;

    /// OpenSmith — open smithing UI. ID 156.
    public const byte OpenSmith = 156;

    /// OpenCarp — open carpentry UI. ID 157.
    public const byte OpenCarp = 157;

    /// SmithWeapons — smith weapon list (LAH). ID 158.
    public const byte SmithWeapons = 158;

    /// SmithArmors — smith armor list (LAR). ID 159.
    public const byte SmithArmors = 159;

    /// CarpItems — carpentry item list. ID 160.
    public const byte CarpItems = 160;

    /// MeditateOK — meditation OK. ID 161.
    public const byte MeditateOK = 161;

    /// Navigation — navigation mode data. ID 162.
    public const byte Navigation = 162;

    /// BattleTeamScores — BatallaMistica event team scores. ID 163.
    public const byte BattleTeamScores = 163;

    /// AmbientColor — map ambient RGB color (PCR). ID 164.
    public const byte AmbientColor = 164;

    // ── Bank (legacy) ────────────────────────────────────────

    /// InitBankLegacy — legacy bank init. ID 165.
    public const byte InitBankLegacy = 165;

    /// BankSlotLegacy — legacy bank slot data. ID 166.
    public const byte BankSlotLegacy = 166;

    /// BankGoldLegacy — legacy bank gold. ID 167.
    public const byte BankGoldLegacy = 167;

    /// BankCloseOK — bank close confirmation. ID 168.
    public const byte BankCloseOK = 168;

    // ── Commerce (legacy) ────────────────────────────────────

    /// NpcInvReset — reset NPC inventory display. ID 170.
    public const byte NpcInvReset = 170;

    /// NpcInvItem — NPC inventory item. ID 171.
    public const byte NpcInvItem = 171;

    /// NpcInvSlotLegacy — legacy NPC inv slot. ID 172.
    public const byte NpcInvSlotLegacy = 172;

    /// InitCommerceLegacy — legacy commerce init. ID 173.
    public const byte InitCommerceLegacy = 173;

    /// TransactionOK — NPC commerce transaction OK. ID 174.
    public const byte TransactionOK = 174;

    /// CommerceCloseOK — commerce close confirmation. ID 175.
    public const byte CommerceCloseOK = 175;

    // ── Tournament / Response / Auction ──────────────────────

    /// TournamentPoints — tournament point total. ID 176.
    public const byte TournamentPoints = 176;

    /// ResponseMsg — server response text (RESPUES). ID 177.
    public const byte ResponseMsg = 177;

    /// AuctionInit — auction window open. ID 178.
    public const byte AuctionInit = 178;

    /// AuctionBid — auction bid info (GVN). ID 179.
    public const byte AuctionBid = 179;

    // ── Trading (legacy) ────────────────────────────────────

    /// TradeInitLegacy — legacy trade init. ID 180.
    public const byte TradeInitLegacy = 180;

    /// TradeOfferRecv — trade gold offer received. ID 181.
    public const byte TradeOfferRecv = 181;

    /// TradeItems — trade item info. ID 182.
    public const byte TradeItems = 182;

    /// TradeChatMsgLegacy — legacy trade chat. ID 183.
    public const byte TradeChatMsgLegacy = 183;

    /// TradeOKLegacy — legacy trade OK. ID 184.
    public const byte TradeOKLegacy = 184;

    /// TradeCancelOK — trade cancel confirmation. ID 185.
    public const byte TradeCancelOK = 185;

    // ── Guild (legacy) ───────────────────────────────────────

    /// GuildListLegacy — legacy guild list. ID 190.
    public const byte GuildListLegacy = 190;

    /// GuildInfoLeader — guild info for leader. ID 191.
    public const byte GuildInfoLeader = 191;

    /// GuildInfoMember — guild info for member. ID 192.
    public const byte GuildInfoMember = 192;

    /// GuildShowForm — show guild management form. ID 193.
    public const byte GuildShowForm = 193;

    /// GuildDetailsResp — guild details response. ID 194.
    public const byte GuildDetailsResp = 194;

    /// GuildBankPermsResp — guild bank permissions response. ID 195.
    public const byte GuildBankPermsResp = 195;

    /// ClanChatResp — clan chat message. ID 196.
    public const byte ClanChatResp = 196;

    /// GuildBankSlotData — full guild bank slot data (SBG). ID 197.
    public const byte GuildBankSlotData = 197;

    // ── Quest ────────────────────────────────────────────────

    /// QuestListResp — quest list data. ID 200.
    public const byte QuestListResp = 200;

    /// QuestCurrent — current quest data. ID 201.
    public const byte QuestCurrent = 201;

    /// QuestSelected — selected quest data. ID 202.
    public const byte QuestSelected = 202;

    /// QuestNpcList — quest NPC list trigger. ID 203.
    public const byte QuestNpcList = 203;

    // ── Misc data packets ────────────────────────────────────

    /// MenuData — right-click context menu (MENU). ID 221.
    public const byte MenuData = 221;

    /// SelectData — selection data (SELE). ID 222.
    public const byte SelectData = 222;

    /// MiniTopData — mini ranking data (MTOP). ID 223.
    public const byte MiniTopData = 223;

    /// ImageData — image/graphic data. ID 224.
    public const byte ImageData = 224;

    /// BkwData — BKW data packet. ID 226.
    public const byte BkwData = 226;

    /// FestData — festival data (FEST). ID 227.
    public const byte FestData = 227;

    /// EnchatData — enter chat room (ENCHAT). ID 228.
    public const byte EnchatData = 228;

    /// IrchatData — IRC-style chat message (IRCHAT). ID 229.
    public const byte IrchatData = 229;

    /// GinfData — guild info display. ID 230.
    public const byte GinfData = 230;

    /// IcoData — ICO data packet. ID 231.
    public const byte IcoData = 231;

    /// ZsosData — SOS data (ZSOS). ID 232.
    public const byte ZsosData = 232;

    /// SbrData — SBR data packet. ID 234.
    public const byte SbrData = 234;

    /// AuctionList — auction item list (APT). ID 237.
    public const byte AuctionList = 237;

    // ── Cosmetics ────────────────────────────────────────────

    /// CosmeticSurgery — cosmetic surgery options. ID 238.
    public const byte CosmeticSurgery = 238;

    /// CosmeticImage — cosmetic image data. ID 239.
    public const byte CosmeticImage = 239;

    /// CosmeticPcgn — cosmetic PCGN data. ID 240.
    public const byte CosmeticPcgn = 240;

    /// CosmeticPcss — cosmetic PCSS data. ID 241.
    public const byte CosmeticPcss = 241;

    /// CosmeticPccc — cosmetic PCCC data. ID 242.
    public const byte CosmeticPccc = 242;

    // ── Guild bank / Full char info ──────────────────────────

    /// FullCharInfo — full character info string (FINI). ID 245.
    public const byte FullCharInfo = 245;

    /// GuildBankInitResp — guild bank init (INITCBANK). ID 247.
    public const byte GuildBankInitResp = 247;

    /// GuildBankSlotResp — guild bank slot (BANCOBK). ID 248.
    public const byte GuildBankSlotResp = 248;

    /// GuildBankGoldResp — guild bank gold (SBG). ID 249.
    public const byte GuildBankGoldResp = 249;

    // ── Ping / Triggers ──────────────────────────────────────

    /// Ping — server ping request. ID 250.
    public const byte Ping = 250;

    /// TravelsOpen — open travels/teleport panel. ID 251.
    public const byte TravelsOpen = 251;

    /// ArenaData — arena duel list (MAR). ID 254.
    public const byte ArenaData = 254;

    /// <summary>
    /// GenericText packet (opcode 255): wraps a legacy text-format packet.
    /// Format: [255][len:u16 LE][text_bytes]
    /// Used by the server's bridge layer during protocol migration.
    /// The client should parse the text content using the old text dispatch logic.
    /// </summary>
    public const byte GenericText = 255;
}

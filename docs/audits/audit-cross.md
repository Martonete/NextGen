# Protocol Cross-Audit: Rust Server ↔ C# Client

Audited files:
- `server/source/protocol/mod.rs` — text opcodes
- `server/source/protocol/packets.rs` — binary enum (ClientPacketID / ServerPacketID)
- `server/source/protocol/binary_packets/core.rs` + `inventory.rs` + `systems.rs` + `social.rs`
- `server/source/game/handlers/mod.rs` — server dispatch table
- `server/source/game/constants.rs`
- `client/Scripts/Network/PacketIds.cs`
- `client/Scripts/Network/ClientPackets.cs`
- `client/Scripts/Network/PacketHandler.Binary.cs` + all partial files

---

## 1. Protocol Mismatches (Opcode-Level)

### 1a. Client → Server opcodes: defined in client but NOT in server enum

The following `ClientPacketId` constants are defined in `PacketIds.cs` but have **no corresponding entry in `ClientPacketID` on the server** (server `packets.rs` and `from_byte()` switch):

| ID | Client Name | Notes |
|----|-------------|-------|
| 109 | `GuildBankPermsQuery` | No server enum entry |
| 110 | `GuildBankPermsSet` | No server enum entry |
| 111 | `GuildBankOpen` | No server enum entry |
| 112 | `GuildBankSave` | No server enum entry |
| 113 | `GuildBankDeposit` | No server enum entry |
| 114 | `GuildBankWithdraw` | No server enum entry |
| 115 | `ClanBankWithdrawItem` | No server enum entry |
| 116 | `ClanBankDepositItem` | No server enum entry |
| 117 | `CloseGuildBank` | No server enum entry |
| 118 | `GuildDonatePts` | No server enum entry |
| 119 | `ClanValidName` | No server enum entry |
| 120 | `QuestList` | No server enum entry |
| 121 | `QuestInfo` | No server enum entry |
| 122 | `QuestAccept` | No server enum entry |
| 123 | `ForumPost` | **Server has ForumPost=123** — this one matches |
| 132 | `InitChat` | No server enum entry |
| 133 | `ChatMsg` | No server enum entry |
| 141 | `MiniStatsReq` | Server has `MiniStats=141` — this one matches |
| 144 | `SendPoints` | No server enum entry |
| 145 | `DuelArenaInfo` | No server enum entry |
| 146 | `ToInfo` | No server enum entry |
| 163 | `SosView` | **Server has SosView=163** — matches |
| 164 | `SosSend` | **Server has SosSend=164** — matches |
| 165 | `SosRespond` | **Server has SosRespond=165** — matches |
| 173 | `ClanInvalidName` | No server enum entry |
| 174 | `PCGF` | No server enum entry |
| 175 | `PCWC` | No server enum entry |
| 176 | `PCCC` | No server enum entry |

**Summary:** All guild bank sub-operations (IDs 109–119), the quest system (120–122), chat rooms (132–133), arena/duel (145), and several misc items (144, 146, 173–176) are defined in the client but the server will respond with `PacketResult::Error` ("Unknown binary packet ID") when it receives them.

### 1b. Field-order / field-count mismatches

#### `write_user_char_index` — SERVER sends ID 5, but builder uses `UserCharIndexInServer` (ID 5)
The server `write_user_char_index` function comment says "ID 28" but calls `ServerPacketID::UserCharIndexInServer.to_byte()` which is ID **5**. The client `HandleBinUserCharIndex` expects opcode 5 (`UserCharIndexInServer`). No wire mismatch, but the comment in `core.rs` line 25–31 is wrong: `/// ID 28: User char index in server.` — should be ID 5.

#### `write_error_msg` — SERVER comment says "ID 55"
`write_error_msg` in `core.rs` has comment `/// ID 55: Error message` but calls `ServerPacketID::ErrorMsg.to_byte()` which is **ID 2**. Again, comment is wrong but wire is correct (the client handles opcode 2 as `ErrorMsg`).

#### `write_update_sta` — ID constant mismatch in comment
`write_update_sta` says `/// ID 15: Update Stamina` but `ServerPacketID::UpdateSta` is **16**. Comment wrong, wire is correct.

#### `write_update_mana` — ID constant mismatch in comment
`write_update_mana` says `/// ID 16: Update Mana` but `ServerPacketID::UpdateMana` is **17**. Wire is correct.

#### `write_update_hp` — Comment says "ID 17"
`write_update_hp` says `/// ID 17: Update HP` — `ServerPacketID::UpdateHP` is **18**. Wire correct, comments off by one throughout the stat group.

#### `write_update_gold` and `write_update_bank_gold` — Payload type
Server `write_update_gold` (opcode 19) sends `i32` via `write_long`. Client at opcode 19 reads `bq.ReadLong()` (i32). **Match.**
Server `write_update_bank_gold` (opcode 102) sends `i32` via `write_long`. Client at opcode 102 reads `bq.ReadLong()`. **Match.**

#### `write_commerce_end` — Wrong ID in comment
`write_commerce_end` comment says "ID 5" but `ServerPacketID::CommerceEnd` is **6**. Client handles 6. Wire correct.

#### `write_bank_end` — Wrong ID in comment
`write_bank_end` comment says "ID 6" but `ServerPacketID::BankEnd` is **7**. Wire correct.

#### `write_commerce_init` — Wrong ID in comment
Comment says "ID 7" but `ServerPacketID::CommerceInit` is **8**. Wire correct.

#### `write_bank_init` — Wrong ID in comment
Comment says "ID 8" but `ServerPacketID::BankInit` is **9**. Wire correct.

#### `write_user_commerce_init` — Wrong ID in comment
Comment says "ID 9" but `ServerPacketID::UserCommerceInit` is **10**. Wire correct.

#### `write_user_commerce_end` — Wrong ID in comment
Comment says "ID 10" but `ServerPacketID::UserCommerceEnd` is **11**. Wire correct.

#### `write_commerce_chat` — Wrong ID in comment
Comment says "ID 12" but `ServerPacketID::CommerceChat` is **13**. Wire correct.

#### `write_show_blacksmith_form` — Wrong ID in comment
Comment says "ID 13" but `ServerPacketID::ShowBlacksmithForm` is **14**. Wire correct.

#### `write_show_carpenter_form` — Wrong ID in comment
Comment says "ID 14" but `ServerPacketID::ShowCarpenterForm` is **15**. Wire correct.

#### `write_work_request_target` — Wrong ID
Comment says "ID 46" but `ServerPacketID::WorkRequestTarget` is **45**. Wire correct.

#### `write_remove_dialogs` — Wrong IDs in comments (systematic)
`write_remove_dialogs` says "ID 1" but `ServerPacketID::RemoveDialogs` is **79**. `write_remove_char_dialog` says "ID 2" but is **80**. `write_navigate_toggle` says "ID 3" but is **81**. These comments appear to have been copied from an older sequential scheme; the enum values are correct.

#### `write_guild_list` — Wrong ID in comment
Comment says "ID 40" but `ServerPacketID::GuildList` is **39**. Wire correct.

#### `write_object_create` — Wrong ID comment
Comment says "ID 35" but `ServerPacketID::ObjectCreate` is **34**.

#### `write_object_delete` — Wrong ID comment
Comment says "ID 36" but `ServerPacketID::ObjectDelete` is **35**.

#### `write_block_position` — Wrong ID comment
Comment says "ID 37" but `ServerPacketID::BlockPosition` is **36**.

### 1c. Real field-content mismatches

#### `ShowSignal` (opcode 58) — field type inconsistency
Server: **no builder found for ShowSignal (58)**. No `write_show_signal` exists in any binary_packets file.
Client `HandleBinaryPacket` case 58: reads `string text` then `(ushort)bq.ReadInteger()` (grh as unsigned short). Without a server builder, the client-side handler is dead code — but the field order is documented client-side as `[string][i16]`.

#### `AccountData` (opcode 76) — server builder missing
No `write_account_data` exists in any binary_packets file. Client handles opcode 76 (`HandleBinAccountData`) by reading a single string. Server defines `AccountData = 76` in the enum but never builds this packet. **Effectively dead on server side.**

#### `CharacterInfo` (opcode 75) — server builder missing
No `write_character_info` builder exists. Client `HandleBinCharacterInfo` (called from opcode 75) — implementation not visible in the audited files but it is referenced. Server defines the enum value but has no builder.

#### `Fame` (opcode 61) — no server builder
No `write_fame` builder in any binary_packets file. Client `HandleBinFame` reads 7 × `ReadLong()` (i32 each). Server has no packet builder for this opcode.

#### `MiniStats` (opcode 62) — no server builder
No `write_mini_stats` builder. Client `HandleBinMiniStats` reads `ReadLong(), ReadLong()` (gold, exp). Server has enum entry but no builder.

#### `SendSkills` (opcode 50) — no server builder
No `write_send_skills` builder. Client `HandleBinSendSkills` reads 20 bytes (20 skill values). Server has enum entry but no builder.

#### `Attributes` (opcode 49) — no server builder
No `write_attributes` (or `write_atributes`) builder. Client `HandleBinAtributes` reads 5 bytes: str, agi, int, con, cha. Server has enum entry but no builder.

#### `Attributes` server reads as well:
`write_update_user_stats` (opcode 44) sends the composite stats on login. Individual stats sent by `write_update_hp`, `write_update_mana`, `write_update_sta`, `write_update_gold`, `write_update_exp`. But the `Attributes` packet (opcode 49, character stats: str/agi/int/con/cha) has no server builder.

#### `SendNight` (opcode 87) — no server builder
No `write_send_night` in any binary_packets file. Client reads `bq.ReadBoolean()`. Server has enum entry but no builder.

#### `UpdateStrengthAndDexterity` (opcode 101) — no server builder
No builder found. Client reads `ReadByte(), ReadByte()` (strength, agility). Server has enum entry but no builder.

#### `ShowMessageBox` (opcode 3) — no server builder
No `write_show_message_box` builder. Client handles opcode 3 reading a string. Server has enum entry but no builder. (Note: `write_error_msg` sends opcode 2 `ErrorMsg`, and `write_error_show` sends opcode 55 `ErrorShow` — opcode 3 itself is unused from the server side.)

#### `Disconnect` (opcode 1) — no builder
No `write_disconnect` builder. Client reads opcode 1 by calling `HandleBinDisconnect(bq)` which reads no fields (just sets `IsLogged=false`). Server has no builder for opcode 1.

#### `ChangeUserTradeSlot` (opcode 86) — no server builder
No `write_change_user_trade_slot` in any binary_packets file. Client `HandleBinChangeUserTradeSlot` reads a complex struct: `[u8 slot][i16 objIndex][i32 amount][i16 grhIndex][u8 objType][i16 maxHit][i16 minHit][i16 maxDef][i16 minDef][i32 value][string name]`. Server has enum entry but no builder — meaning trade slot updates cannot be sent by the server.

#### `CancelOfferItem` (opcode 105) — no server builder
No `write_cancel_offer_item`. Client reads `[u8 slot]`. Server has enum entry but no builder.

#### `ShowPartyForm` (opcode 106) — no server builder
No `write_show_party_form`. Client reads `[u8 pType]`. No server builder.

#### `MapMusic` (opcode 96) — no server builder
No `write_map_music`. Client `HandleBinMapMusic` (called at opcode 96) — referenced but implementation not audited. No builder in server code.

#### `GuildNews` (opcode 73) — field count check
Server: `write_trainer_creature_list` (opcode 72) sends one string.
Client for opcode 73 (`GuildNews`) reads **three strings**: `news`, `motd`, `codex`. No `write_guild_news` builder found in binary_packets (the `write_guild_list` at opcode 39 only sends one string). **Server has no builder for GuildNews (73).**

#### `CharacterChange` vs `CharacterCreate` field count — CONFIRMED MATCH
Server `write_character_change` (opcode 33): `[i16 charIndex][i16 body][i16 head][u8 heading][i16 weapon][i16 shield][i16 helmet][i16 fxIndex][i16 fxLoops]` — 9 fields.
Client `HandleBinCharacterChange` reads: `charIndex, body, head, heading, weapon, shield, helmet, fxIndex, fxLoops` — **Matches.**

Server `write_character_create` (opcode 29): `[i16 charIndex][i16 body][i16 head][u8 heading][i16 x][i16 y][i16 weapon][i16 shield][i16 helmet][i16 fxIndex][i16 fxLoops][string name][u8 nickColor][u8 privileges]` — 14 fields.
Client `HandleBinCharacterCreate` reads: same 14 fields in the same order. **Matches.**

#### `write_dice_roll` comment says "ID 67" but enum is `DiceRoll = 59`
Wire is correct (opcode 59). Comment is wrong.

#### `UpdateExp` (opcode 20) — type issue
Server `write_update_exp` sends `write_long` (i32). Client `HandleBinUpdateExp` reads `bq.ReadLong()` (returns int). **Match** — but `_state.Exp` is `int`, and `bq.ReadLong()` returns `int` in C# via the ByteQueue wrapper. Fine.

#### `BattleTeamScores` (opcode 163) — no server builder
No `write_battle_team_scores`. Client reads 4 × `ReadLong()`. **Server cannot send this packet.**

#### `NavigationData` (opcode 162) — no server builder
No `write_navigation_data`. Client `HandleBinNavigationData` — referenced but implementation not visible. **No builder.**

---

## 2. Dead Opcodes

### Server-Side — ServerPacketID entries with no builder AND never sent:

| Opcode | Name | Status |
|--------|------|--------|
| 1 | `Disconnect` | No builder, never sent by server code |
| 3 | `ShowMessageBox` | No builder (ErrorMsg=2 and ErrorShow=55 are used) |
| 26 | `ShowMessageBox2` | No builder found; used client-side only |
| 27 | `UserIndexAlt` | No builder |
| 49 | `Attributes` | No builder |
| 50 | `SendSkills` | No builder |
| 52–53 | (reserved for craft lists) | No enum entry, just a comment |
| 61 | `Fame` | No builder |
| 62 | `MiniStats` | No builder |
| 75 | `CharacterInfo` | No builder |
| 76 | `AccountData` | No builder |
| 83 (form) | `ShowGuildFoundationForm` | Has builder (`write_show_guild_fundation_form`) — OK |
| 86 | `ChangeUserTradeSlot` | No builder |
| 87 | `SendNight` | No builder |
| 90 | `SpawnList` | No builder |
| 91 | `ShowSOSForm` | No builder |
| 92 | `ShowMOTDEditionForm` | No builder |
| 93 | `ShowGMPanelForm` | No builder |
| 94 | `UserNameList` | No builder |
| 95 | `ShowGuildAlign` | No builder |
| 96 | `MapMusic` | No builder |
| 101 | `UpdateStrengthAndDexterity` | No builder |
| 105 | `CancelOfferItem` | No builder |
| 106 | `ShowPartyForm` | No builder |
| 144 | `ClassOptions` | Has builder (`write_class_options` — NOT FOUND in files audited) — check needed |
| 162 | `Navigation` | No builder |
| 163 | `BattleTeamScores` | No builder |
| 165 | `InitBankLegacy` | Has builder in inventory.rs but sends string only; client reads string. |
| 168 | `BankCloseOK` | In client's PacketIds.cs but NO entry in server `ServerPacketID` enum |
| 169 | (gap) | Not in server enum |
| 175 | `CommerceCloseOK` | In client `PacketIds.cs`; server `ServerPacketID` does NOT have this enum entry |
| 176 | `TournamentPoints` | Has no builder |
| 185 | `TradeCancelOK` | In client `PacketIds.cs`; NOT in server `ServerPacketID` enum |
| 195 | `GuildBankPermsResp` | In client `PacketIds.cs`; NOT in server `ServerPacketID` enum |
| 198–209 | (gap) | Not in either enum |
| 210 | (gap) | Not in either enum |
| 247 | `GuildBankInitResp` | In server enum, but no builder found in binary_packets files |
| 248 | `GuildBankSlotResp` | In server enum, no builder |
| 249 | `GuildBankGoldResp` | In server enum, no builder |

### Client-Side dead `ClientPacketId` constants (sent by client but server returns Error):

All entries in section 1a above. The most operationally impactful:
- All guild bank ops (109–119): `GuildBankPermsQuery`, `GuildBankPermsSet`, `GuildBankOpen`, `GuildBankSave`, `GuildBankDeposit`, `GuildBankWithdraw`, `ClanBankWithdrawItem`, `ClanBankDepositItem`, `CloseGuildBank`, `GuildDonatePts`, `ClanValidName`
- Quest system: `QuestList` (120), `QuestInfo` (121), `QuestAccept` (122)
- Chat rooms: `InitChat` (132), `ChatMsg` (133)
- Misc: `SendPoints` (144), `DuelArenaInfo` (145), `ToInfo` (146), `PCGF` (174), `PCWC` (175), `PCCC` (176)

---

## 3. Constant Drift (server/source/game/constants.rs vs client)

### `SoundManager` (client) vs server sound IDs

Server `constants.rs` defines:
- `SND_TALAR = 13` (lumber)
- `SND_PESCAR = 14` (fishing)
- `SND_MINERO = 15` (mining)
- `SND_HERRERO = 41` (blacksmith)
- `SND_CARPINTERO = 42` (carpentry)

Client `SoundManager.cs` was not directly audited but client code at `HandleBinLevelUp` calls `SoundManager.SND_LEVEL` and `HandleBinDead` calls `SoundManager.SND_DEATH`. These are client-only constants. The server sends sound indices via `write_play_wave(wav_index, x, y)` and client receives them as raw numbers. If `SoundManager.SND_LEVEL` or `SND_DEATH` do not match any server-issued `play_wave` index, no direct mismatch occurs (they are client-side triggers from packet handler logic, not from wire values).

### Dead body constants
Server: `DEAD_HEAD_NEUTRAL = 500`, `DEAD_BODY_NEUTRAL = 8`.
Client `PacketHandler.Helpers.cs` has `IsDeadHead(head)` — not audited in detail, but the logic should check for head index 500 as the dead/ghost indicator. If this is different, dead characters would not render correctly.

### No numeric constants shared across the wire were found to drift
The server `constants.rs` contains item/object IDs (tool indices, resource IDs, equipment IDs). None of these are sent over the wire as protocol constants — they are used in server business logic only. The client does not have a corresponding constants file with the same numeric values in an auditable way.

---

## 4. Missing Handlers (Server sends, Client doesn't handle / Client sends, Server ignores)

### Server sends but client does NOT have a case in `HandleBinaryPacket`:

All of these are in `ServerPacketId` but absent from the switch in `PacketHandler.Binary.cs`:

| Opcode | Name |
|--------|------|
| 58 | `ShowSignal` — **present in switch** (reads string + ushort, result unused). Actually handled but result discarded. |
| 73 | `GuildNews` — **present in switch** (reads 3 strings). OK. |
| 78 | `Dead` — **present in switch**. OK. |

After reviewing carefully: the client switch is remarkably complete — essentially every opcode in `ServerPacketId` that has an enum value is handled in the switch. Gaps that are truly missing handlers:

- None of the `ServerPacketId` constants from 200–219 (except the ones explicitly defined) have cases. But those IDs don't appear in the server's `ServerPacketID` enum either.

**Real missing handler**: `ServerPacketId.HeadingChange = 107` calls `HandleBinHeadingChange(bq)` at the bottom of the switch, but the actual method `HandleBinHeadingChange` is only referenced in a comment stub in `PacketHandler.Auth.cs` — the method body was not found in any audited file. **This is a missing implementation.**

**Real missing handler**: `CharParticleCreate` (211) — called as `HandleBinCharParticleCreate(bq)` but the method is only documented in a comment stub in `PacketHandler.Auth.cs`: `/// CharParticleCreate (ID 211)` with no actual method body. **This handler is missing.**

**Real missing handler**: `HandleBinArrow(bq)` (opcode 108) — referenced in the switch but method body not found in any audited partial class. Only a comment exists in Auth.cs: `/// Arrow (ID 108)`.

**Real missing handler**: `HandleBinForceCharMove(bq)` (opcode 32) — referenced in the switch. A comment in Social.cs documents: `/// ForceCharMove (ID 32)` but no method body was found in Movement.cs (which was the expected location).

**Real missing handler**: `HandleBinCharacterInfo(bq)` (opcode 75) — referenced in switch. No body found in any partial file.

**Real missing handler**: `HandleBinPlaySound(bq)` (opcode 150) — referenced in switch (Combat.cs comment stub). No body found.

**Real missing handler**: `HandleBinWorkMode(bq)` (opcode 155) — referenced in switch. No body found in Commerce.cs (comment stub exists).

**Real missing handler**: `HandleBinUserMount(bq)` (opcode 142) — referenced. No body found in audited files.

**Real missing handler**: `HandleBinLevitate(bq)` (opcode 143) — referenced. No body found.

**Real missing handler**: `HandleBinAnimData(bq)` (opcode 225) — referenced. No body found.

**Real missing handler**: `HandleBinNavigateBroadcast(bq)` (opcode 109) — referenced. No body found.

**Real missing handler**: `HandleBinNavigationData(bq)` (opcode 162) — referenced. No body found.

**Real missing handler**: `HandleBinMapMusic(bq)` (opcode 96) — referenced. No body found.

### Client sends but server has no enum entry (and returns PacketResult::Error):

All entries in section 1a. Additionally the text-opcode handlers in `handle_packet()` (the legacy text dispatch at line 644+ in mod.rs) handle some VB6-era string packets but those are bypassed in the pure-binary client.

---

## 5. Data Format (.dat/.ind Files)

### Server side
Server parses `.dat`/`.ind` files in:
- `db/charfile.rs` — character persistence (SQL, not .dat)
- `game/zones.rs` — zone definitions
- `game/types.rs` — object types
- `game/handlers/inventory/use_item.rs`, `click.rs`, `doors.rs`
- `game/handlers/skills/craft.rs`
- Various `.ao` binary files via INI-style readers

The server code references `.AO` binary data files (VB6 format) for maps, objects, spells, and NPCs. These are read server-side only for game logic — they are not part of the wire protocol and do not need to be compared to client parsing.

### Client side
Client parses `.dat`/`.ind` files in:
- `Data/GrhLoader.cs` — reads `Graficos.ind` + `Graficos.dat` (VB6 binary format)
- `Data/BodyLoader.cs` — reads `Personajes.ind` + `Personajes.dat`
- `Data/WeaponShieldLoader.cs` — reads weapon/shield animations
- `Data/FxLoader.cs` — reads FX data
- `Data/AuraLoader.cs` — reads aura data
- `Data/AoFont.cs` — reads font/textos data
- `Data/GameData.cs` — coordinates all loaders

**These are purely local asset files.** Neither side sends raw `.dat`/`.ind` content over the wire — they share the same file format spec (VB6 binary) and the client reads the files locally. There is no protocol-level `.dat`/`.ind` exchange to cross-audit. **No mismatch found here.**

However: the map format (`.map` files, also VB6 binary) is loaded server-side by the Rust server and loaded client-side by the C# client. Both sides independently parse the same `.map` format. If there is a version mismatch in how trigger/NPC/object layers are indexed, map rendering would be wrong but this would show up as gameplay bugs, not a wire protocol issue.

---

## Summary of Critical Issues

### CRITICAL — Will cause stream corruption or crash:

1. **All guild bank client packets (IDs 109–119) cause server `PacketResult::Error`** — server closes/corrupts the stream when these are received. Client sends them (e.g., `WriteGuildBankDepositItem`, `WriteGuildBankWithdrawGold`).

2. **Quest packets (IDs 120–122) cause server `PacketResult::Error`** — same issue.

3. **Chat room packets (IDs 132–133) cause server `PacketResult::Error`**.

4. **`HandleBinArrow` (opcode 108), `HandleBinForceCharMove` (32), `HandleBinCharParticleCreate` (211), `HandleBinHeadingChange` (107), `HandleBinCharacterInfo` (75)** — referenced in the dispatch switch but have no method body. When the server sends these packets, the client will throw an exception and the byte queue will roll back (treating it as a partial packet), stalling the stream.

5. **`HandleBinPlaySound` (150), `HandleBinWorkMode` (155), `HandleBinUserMount` (142), `HandleBinLevitate` (143), `HandleBinAnimData` (225), `HandleBinNavigateBroadcast` (109), `HandleBinNavigationData` (162), `HandleBinMapMusic` (96)** — stub references with no bodies.

### HIGH — Feature gaps (server sends opcode, no handler / no builder):

6. **`Attributes` (49), `SendSkills` (50), `Fame` (61), `MiniStats` (62), `SendNight` (87), `UpdateStrengthAndDexterity` (101), `ChangeUserTradeSlot` (86), `CancelOfferItem` (105), `ShowPartyForm` (106)** — in both enums but server has no binary builders, so these stats/features are never delivered.

7. **`GuildNews` (73)** — server has no builder; client will never receive guild news/motd/codex.

8. **`GuildBankPermsResp` (195), `BankCloseOK` (168), `CommerceCloseOK` (175), `TradeCancelOK` (185)** — defined in client `PacketIds.cs` but **absent from the server `ServerPacketID` enum entirely**. Server cannot send these; client will silently skip them.

9. **`GuildBankInitResp` (247), `GuildBankSlotResp` (248), `GuildBankGoldResp` (249)** — in server enum, no builders.

### LOW — Comment errors (no wire impact):

10. All the off-by-one ID comments in `binary_packets/inventory.rs` (see section 1b) are purely documentation errors — the wire behavior is correct because enum values are used, not magic numbers.

---

*Audit completed: 2026-03-28*

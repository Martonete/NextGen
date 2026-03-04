# Argentum Online 13.3 — Protocol Reference

Complete analysis of the TCP networking and packet protocol used by Argentum Online version 13.3 (Comunidad Winter / Alkon branch). This is the **final community version** of the VB6 codebase, designed by Juan Martin Sotuyo Dodero (Maraxus).

> **Source**: [`Comunidad-Winter/Argentum-Online`](https://github.com/Comunidad-Winter/Argentum-Online) — analyzed from `client/CODIGO/` and `server/Codigo/`.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [TCP Connection Setup](#2-tcp-connection-setup)
3. [Packet Format](#3-packet-format)
4. [Data Types (clsByteQueue)](#4-data-types-clsbytequeue)
5. [Encryption](#5-encryption)
6. [Connection Lifecycle](#6-connection-lifecycle)
7. [Packet Dispatch](#7-packet-dispatch)
8. [Server → Client Packets (ServerPacketID)](#8-server--client-packets-serverpacketid)
9. [Client → Server Packets (ClientPacketID)](#9-client--server-packets-clientpacketid)
10. [MultiMessage System](#10-multimessage-system)
11. [GM Commands Sub-Protocol](#11-gm-commands-sub-protocol)
12. [Broadcast Routing (SendTarget)](#12-broadcast-routing-sendtarget)
13. [Key Packet Formats (Detailed)](#13-key-packet-formats-detailed)
14. [Security Features](#14-security-features)
15. [Comparison: 13.3 vs TSAO (11.5)](#15-comparison-133-vs-tsao-115)

---

## 1. Architecture Overview

```
┌──────────────┐         TCP (raw binary)         ┌──────────────┐
│   Client     │ ◄──────────────────────────────► │   Server     │
│  (VB6 EXE)   │        Port (from Server.ini)    │  (VB6 EXE)   │
│              │                                   │              │
│ frmMain      │                                   │ wskapiAO.bas │
│  └ Winsock1  │                                   │  └ WndProc() │
│              │                                   │              │
│ Protocol.bas │                                   │ Protocol.bas │
│  ├ Write*()  │ ──── outgoingData ──────────────► │  ├ Handle*() │
│  └ Handle*() │ ◄──── incomingData ────────────── │  └ Write*()  │
│              │                                   │              │
│ clsByteQueue │         (2 per connection)         │ clsByteQueue │
│  ├ incoming  │                                   │  ├ incoming  │
│  └ outgoing  │                                   │  └ outgoing  │
└──────────────┘                                   └──────────────┘
```

### Key Files

| File | Location | Purpose |
|------|----------|---------|
| `Protocol.bas` | Client + Server | All packet read/write functions and dispatch |
| `clsByteQueue.cls` | Client + Server | FIFO byte buffer for serialization |
| `wskapiAO.bas` | Server | Raw Winsock API wrapper, event dispatch |
| `wsksock.bas` | Server | Win32 Winsock2 API declarations (`ws2_32.dll`) |
| `TCP.bas` | Client + Server | Socket lifecycle, login calls, send/close |
| `SecurityIp.bas` | Server | IP rate limiting and connection limits |
| `Declares.bas` | Client + Server | Global types, enums, constants |
| `modSendData.bas` | Server | Broadcast routing (`SendData()`, `SendTarget`) |
| `frmMain.frm` | Client | `Winsock1` control, `DataArrival` handler |

---

## 2. TCP Connection Setup

### Server Side (Winsock2 API)

The server supports 4 socket backends via `#If UsarQueSocket`. The **primary** is raw Winsock2 API (`UsarQueSocket = 1`):

1. **`SocketConfig()`** (General.bas): Initializes IP tables, Winsock, starts listening
2. **`IniciaWsApi()`** (wskapiAO.bas): Creates a hidden Win32 `STATIC` window, subclasses it with `WndProc` to receive socket messages
3. **`ListenForConnect()`** (wsksock.bas):
   - `socket(PF_INET, SOCK_STREAM, 0)` — TCP socket
   - `bind(INADDR_ANY, Puerto)` — bind to configured port
   - `WSAAsyncSelect(sock, hWnd, 1025, FD_READ | FD_CLOSE | FD_ACCEPT)` — async notification
   - `listen(sock, SOMAXCONN)` — start accepting
4. **Port**: Read from `Server.ini → [INIT] StartPort`

### Connection Accept Flow

`WndProc()` handles Windows message `1025`:

| Event | Action |
|-------|--------|
| `FD_ACCEPT` | `accept()` → disable linger → IP rate check → IP connection limit → set 8KB buffers → assign user slot |
| `FD_READ` | `recv()` into 8KB buffer → `EventoSockRead()` → append to `incomingData` → `HandleIncomingData()` |
| `FD_CLOSE` | `EventoSockClose()` → save user → cleanup slot |

### Client Side (Winsock Control)

The client uses the **MSWinsockLib.Winsock** ActiveX control (`Winsock1` on `frmMain`):

```vb6
frmMain.Winsock1.Connect CurServerIp, CurServerPort
```

Server IP/port come from `ServersLst()` array or `frmConnect` text fields, with defaults from `Inicio.con` binary config.

### Socket-to-User Mapping (Server)

A VB6 `Collection` (`WSAPISock2Usr`) maps socket handles to user indices for O(1) lookup:
- `AgregaSlotSock(Sock, Slot)` — add mapping
- `BuscaSlotSock(Sock)` — lookup user index
- `BorraSlotSock(Sock)` — remove mapping

---

## 3. Packet Format

### Protocol Type: Binary, Little-Endian, Unencrypted

Every packet starts with a **1-byte opcode** (packet ID), followed by fields in a fixed, known order:

```
[PacketID: 1 byte] [Field₁] [Field₂] ... [Fieldₙ]
```

There are:
- **No length prefixes** on the overall packet
- **No delimiters** between packets
- **No encryption** (standard build)
- **No compression**

The protocol relies entirely on knowing the exact field sizes from the packet ID to determine where one packet ends and the next begins.

### Partial Packet Handling

Since TCP is a stream protocol with no framing, packets may arrive split across multiple `recv()` calls. The `clsByteQueue` handles this:

1. Each handler checks if enough bytes are available before reading
2. If not enough data → raises `NotEnoughDataErrCode` → buffer remains unchanged → wait for more data
3. For packets with variable-length strings: the buffer state is saved before reading, and restored on failure

---

## 4. Data Types (clsByteQueue)

The `clsByteQueue` is a FIFO byte buffer (default 10,240 bytes) using `RtlMoveMemory` for fast operations. **Identical implementation on client and server.**

| Type | Wire Size | VB6 Type | Read/Write Methods |
|------|-----------|----------|--------------------|
| Byte | 1 byte | `Byte` | `WriteByte` / `ReadByte` |
| Boolean | 1 byte (0 or 1) | `Boolean` | `WriteBoolean` / `ReadBoolean` |
| Integer | 2 bytes LE | `Integer` (signed 16-bit) | `WriteInteger` / `ReadInteger` |
| Long | 4 bytes LE | `Long` (signed 32-bit) | `WriteLong` / `ReadLong` |
| Single | 4 bytes IEEE 754 | `Single` | `WriteSingle` / `ReadSingle` |
| Double | 8 bytes IEEE 754 | `Double` | `WriteDouble` / `ReadDouble` |
| String (variable) | 2-byte LE length + N bytes ASCII | `String` | `WriteASCIIString` / `ReadASCIIString` |
| String (fixed) | N bytes ASCII (no prefix) | `String` | `WriteASCIIStringFixed` / `ReadASCIIStringFixed` |

### String Encoding

Variable-length strings use a **2-byte little-endian length prefix** followed by ASCII bytes:

```
[Length: 2 bytes LE] [ASCII data: Length bytes]
```

String lists (e.g., guild codex) use `vbNullChar` (0x00) as separator between entries.

### Peek Methods

All types have `Peek*` variants that read without advancing the buffer position. Used for lookahead (e.g., peeking at the packet ID before committing to read the full packet).

### Buffer Overflow

- `NOT_ENOUGH_DATA` (vbObjectError + 9): Raised when reading more bytes than available
- `NOT_ENOUGH_SPACE` (vbObjectError + 10): Raised when buffer is full

---

## 5. Encryption

### Standard Build: **None**

The 13.3 protocol has **no encryption on the wire**. Data flows as raw binary over TCP. This was confirmed by searching both client and server codebases for: `Encrypt`, `Decrypt`, `Cifrar`, `Descifrar`, `AoDefEncode`, `AoDefDecode`, `Semilla`, `Numero2Letra`, XOR-based crypto — **zero matches**.

### Optional Anti-Cheat (`#If SeguridadAlkon`)

A compile-time flag `SeguridadAlkon` enables optional security features:
- **MD5 password hashing**: Password sent as 32-byte fixed MD5 hash instead of plaintext
- **Client integrity check**: MD5 hash of client executable sent during login, validated by server
- **`DataSent()` / `DataReceived()` hooks**: Byte array transforms via `clsManagerInvisibles` — anti-cheat layer, NOT encryption
- **`ConnectionStablished()` init**: Anti-cheat initialization on connect

These features are disabled by default in the community build.

### Comparison with TSAO (11.5)

TSAO uses a **4-layer encryption** pipeline:
1. `AoDefServEncrypt` (hex encoding)
2. `AoDefEncode` (base64)
3. `DeCodificar` (XOR with rotating key)
4. `Numero2Letra` → `Semilla` (key rotation, counter 0→999999)

**The 13.3 codebase eliminated all encryption.** This is a fundamental architectural difference.

---

## 6. Connection Lifecycle

### Login Handshake

```
Client                              Server
  │                                    │
  │──── TCP Connect ──────────────────►│ EventoSockAccept()
  │                                    │  ├ IP rate check (5000ms min)
  │                                    │  ├ Connection limit check (max 10/IP)
  │                                    │  └ Assign UserIndex slot
  │                                    │
  │──── LoginExistingChar (ID 0) ─────►│ HandleLoginExistingChar()
  │     or LoginNewChar (ID 2)         │  ├ Validate version
  │     or ThrowDices (ID 1)           │  ├ Load/create character
  │                                    │  └ Send initial state:
  │                                    │
  │◄──── Logged (ID 0) ───────────────│  class byte
  │◄──── UserIndexInServer (ID 27) ───│  user index
  │◄──── UserCharIndexInServer (ID 28)│  char index
  │◄──── ChangeMap (ID 21) ───────────│  map number
  │◄──── PosUpdate (ID 22) ───────────│  x, y position
  │◄──── UpdateUserStats (ID 45) ─────│  HP/Mana/Sta/Gold/Level/Exp
  │◄──── ChangeInventorySlot ×N ──────│  inventory contents
  │◄──── ChangeSpellSlot ×N ──────────│  spell list
  │◄──── Atributes (ID 50) ───────────│  STR/AGI/INT/CON/CHA
  │◄──── SendSkills (ID 71) ──────────│  20 skill values
  │◄──── CharacterCreate ×N ──────────│  visible characters in area
  │◄──── ObjectCreate ×N ─────────────│  visible items on ground
  │                                    │
  │──── Walk / Attack / etc. ─────────►│  Normal gameplay
  │                                    │
```

### Pre-Login Allowed Packets

Only **3 packet IDs** are accepted before authentication:

| ID | Packet | Purpose |
|----|--------|---------|
| 0 | `LoginExistingChar` | Login with existing character |
| 1 | `ThrowDices` | Roll dice for character creation stats |
| 2 | `LoginNewChar` | Create new character and login |

Any other packet from a non-authenticated user results in **immediate socket closure**.

### Ping / Keep-Alive

| Direction | Packet | ID | Payload |
|-----------|--------|----|---------|
| Client → Server | `Ping` | 119 | (none) |
| Server → Client | `Pong` | 88 | (none) |

This is a latency check, not a required heartbeat. The server does not disconnect for missing pings.

### Idle Detection

`UserList(UserIndex).Counters.IdleCount` is reset to 0 on every valid incoming packet. A server-side timer presumably increments this counter to detect idle users.

### Anti-SpeedHack (Walk)

The `Walk` handler includes speed detection: if **30 walk packets arrive in less than 5800ms**, the user is kicked.

### Disconnect

| Type | Mechanism |
|------|-----------|
| Client-initiated (clean) | Client sends `Quit` (ID 71), server saves and closes |
| Client-initiated (TCP) | `FD_CLOSE` event → `Cerrar_Usuario()` |
| Server-initiated | Server writes `Disconnect` (ID 4) to outgoing buffer |
| Socket cleanup | `CloseSocket()`: decrement IP count, clear centinela, cancel commerce, save character, reset slot |

---

## 7. Packet Dispatch

### Client Side

`HandleIncomingData()` in `Protocol.bas` (line 464):
1. Peek first byte (packet ID) without removing
2. `Select Case` dispatch to `Handle*()` function
3. Each handler reads its fields; if not enough data → error → wait for more
4. After successful handling, if more data remains → **recursive call** to process next packet

### Server Side

`HandleIncomingData(UserIndex)` in `Protocol.bas` (line 355):
1. Peek first byte
2. Authentication gate: if not `LoginExistingChar`, `ThrowDices`, or `LoginNewChar` → user must be logged in
3. `Select Case` dispatch to handler
4. After handling → recursive call for remaining data
5. Unknown packet IDs → close socket

### Send Pipeline

```
Write*() functions
    └─► outgoingData (clsByteQueue)
            └─► FlushBuffer()
                    └─► ReadASCIIStringFixed(length) → raw bytes
                            └─► send() / Winsock1.SendData()
```

### Broadcast Pattern (Server)

For packets sent to multiple users, the server uses `PrepareMessage*()` functions:
1. Serialize packet to `auxiliarBuffer` (shared `clsByteQueue`)
2. Extract as VB6 String (raw bytes)
3. Write that string to each target user's `outgoingData` via `WriteASCIIStringFixed`

---

## 8. Server → Client Packets (ServerPacketID)

107 packet types. VB6 enum auto-increments from 0.

| ID | Name | Payload |
|----|------|---------|
| 0 | `logged` | Byte: class |
| 1 | `RemoveDialogs` | — |
| 2 | `RemoveCharDialog` | Integer: charIndex |
| 3 | `NavigateToggle` | — |
| 4 | `Disconnect` | — |
| 5 | `CommerceEnd` | — |
| 6 | `BankEnd` | — |
| 7 | `CommerceInit` | — |
| 8 | `BankInit` | Long: bankGold |
| 9 | `UserCommerceInit` | String: otherName |
| 10 | `UserCommerceEnd` | — |
| 11 | `UserOfferConfirm` | — |
| 12 | `CommerceChat` | String: chat |
| 13 | `ShowBlacksmithForm` | — |
| 14 | `ShowCarpenterForm` | — |
| 15 | `UpdateSta` | Integer: minSta |
| 16 | `UpdateMana` | Integer: minMana |
| 17 | `UpdateHP` | Integer: minHP |
| 18 | `UpdateGold` | Long: gold |
| 19 | `UpdateBankGold` | Long: bankGold |
| 20 | `UpdateExp` | Long: exp |
| 21 | `ChangeMap` | Integer: mapNum, Integer: mapVersion |
| 22 | `PosUpdate` | Byte: x, Byte: y |
| 23 | `ChatOverHead` | String: chat, Integer: charIndex, Long: color |
| 24 | `ConsoleMsg` | String: chat, Byte: fontIndex |
| 25 | `GuildChat` | String: chat |
| 26 | `ShowMessageBox` | String: message |
| 27 | `UserIndexInServer` | Integer: userIndex |
| 28 | `UserCharIndexInServer` | Integer: charIndex |
| 29 | `CharacterCreate` | Integer: charIndex, Integer: body, Integer: head, Byte: heading, Byte: x, Byte: y, Integer: weapon, Integer: shield, Integer: helmet, Integer: fxIndex, Integer: fxLoops, String: name, Byte: nickColor, Byte: privileges |
| 30 | `CharacterRemove` | Integer: charIndex |
| 31 | `CharacterChangeNick` | Integer: charIndex, String: newNick |
| 32 | `CharacterMove` | Integer: charIndex, Byte: x, Byte: y |
| 33 | `ForceCharMove` | Byte: direction |
| 34 | `CharacterChange` | Integer: charIndex, Integer: body, Integer: head, Byte: heading, Integer: weapon, Integer: shield, Integer: helmet, Integer: fxIndex, Integer: fxLoops |
| 35 | `ObjectCreate` | Byte: x, Byte: y, Integer: grhIndex |
| 36 | `ObjectDelete` | Byte: x, Byte: y |
| 37 | `BlockPosition` | Byte: x, Byte: y, Boolean: blocked |
| 38 | `PlayMIDI` | Byte: midiIndex |
| 39 | `PlayWave` | Byte: waveIndex, Byte: x, Byte: y |
| 40 | `guildList` | String: guildNames (null-separated) |
| 41 | `AreaChanged` | Byte: x, Byte: y |
| 42 | `PauseToggle` | — |
| 43 | `RainToggle` | — |
| 44 | `CreateFX` | Integer: charIndex, Integer: fxIndex, Integer: fxLoops |
| 45 | `UpdateUserStats` | Integer: maxHP, Integer: minHP, Integer: maxMana, Integer: minMana, Integer: maxSta, Integer: minSta, Long: gold, Byte: level, Long: expToNextLevel, Long: currentExp |
| 46 | `WorkRequestTarget` | Byte: skillType |
| 47 | `ChangeInventorySlot` | Byte: slot, Integer: objIndex, String: name, Integer: amount, Boolean: equipped, Integer: grhIndex, Byte: objType, Integer: maxHit, Integer: minHit, Integer: def, Single: value |
| 48 | `ChangeBankSlot` | Byte: slot, Integer: objIndex, String: name, Integer: amount, Boolean: equipped, Integer: grhIndex, Byte: objType, Integer: maxHit, Integer: minHit, Integer: def, Single: value |
| 49 | `ChangeSpellSlot` | Byte: slot, Integer: spellIndex, String: name |
| 50 | `Atributes` | Byte×5: STR, AGI, INT, CON, CHA |
| 51 | `BlacksmithWeapons` | Integer: count, then per item: String: name, Integer: grhIndex, ... |
| 52 | `BlacksmithArmors` | (same format as weapons) |
| 53 | `CarpenterObjects` | (same format as weapons) |
| 54 | `RestOK` | — |
| 55 | `ErrorMsg` | String: message |
| 56 | `Blind` | — |
| 57 | `Dumb` | — |
| 58 | `ShowSignal` | String: text, Integer: grhIndex |
| 59 | `ChangeNPCInventorySlot` | Byte: slot, String: name, Integer: amount, Single: value, Integer: grhIndex, Integer: objIndex, Byte: objType, Integer: maxHit, Integer: minHit, Integer: def |
| 60 | `UpdateHungerAndThirst` | Byte: maxAgua, Byte: minAgua, Byte: maxHam, Byte: minHam |
| 61 | `Fame` | Long×7: fame values (Asesino, Bandido, Burgues, Ladron, Noble, Plebe, Promedio) |
| 62 | `MiniStats` | Long: gold, Long: exp, ... (level, class, etc.) |
| 63 | `LevelUp` | Integer: skillPoints |
| 64 | `AddForumMsg` | Byte: forumType, String: title, String: author, String: body |
| 65 | `ShowForumForm` | Byte: privilege, Boolean: canPost |
| 66 | `SetInvisible` | Integer: charIndex, Boolean: invisible |
| 67 | `DiceRoll` | Byte×5: STR, AGI, INT, CON, CHA |
| 68 | `MeditateToggle` | — |
| 69 | `BlindNoMore` | — |
| 70 | `DumbNoMore` | — |
| 71 | `SendSkills` | Byte×20: skill values |
| 72 | `TrainerCreatureList` | String: creatures (null-separated) |
| 73 | `guildNews` | String: news, String: motd, String: codeOfPurpose |
| 74 | `OfferDetails` | String: details |
| 75 | `AlianceProposalsList` | String: guilds (null-separated) |
| 76 | `PeaceProposalsList` | String: guilds (null-separated) |
| 77 | `CharacterInfo` | String: name, Byte: race, Byte: class, Byte: gender, Byte: level, Long: gold, Long: bankGold, Long: reputation, String: description, String: guildName, String: title, ... |
| 78 | `GuildLeaderInfo` | String: memberList, String: joinRequestList, ... |
| 79 | `GuildMemberInfo` | String: memberName, Boolean: online, ... |
| 80 | `GuildDetails` | String: guildName, String: founder, ... |
| 81 | `ShowGuildFundationForm` | — |
| 82 | `ParalizeOK` | — |
| 83 | `ShowUserRequest` | String: details |
| 84 | `TradeOK` | — |
| 85 | `BankOK` | — |
| 86 | `ChangeUserTradeSlot` | Byte: offerSlot, Integer: objIndex, Long: amount, Integer: grhIndex, Byte: objType, Integer: maxHit, Integer: minHit, Integer: def, Long: value, String: name |
| 87 | `SendNight` | Boolean: isNight |
| 88 | `Pong` | — |
| 89 | `UpdateTagAndStatus` | Integer: charIndex, Byte: nickColor, String: tag |
| 90 | `SpawnList` | String: creatures (null-separated) |
| 91 | `ShowSOSForm` | String: sosList (null-separated) |
| 92 | `ShowMOTDEditionForm` | String: motd |
| 93 | `ShowGMPanelForm` | — |
| 94 | `UserNameList` | String: names (null-separated) |
| 95 | `ShowDenounces` | String: denounces (null-separated) |
| 96 | `RecordList` | Byte: recordType, ... |
| 97 | `RecordDetails` | ... |
| 98 | `ShowGuildAlign` | — |
| 99 | `ShowPartyForm` | Byte: type, ... |
| 100 | `UpdateStrenghtAndDexterity` | Byte: STR, Byte: AGI |
| 101 | `UpdateStrenght` | Byte: STR |
| 102 | `UpdateDexterity` | Byte: AGI |
| 103 | `AddSlots` | Byte: maxSlots |
| 104 | `MultiMessage` | Byte: subType (eMessages), variable payload |
| 105 | `StopWorking` | — |
| 106 | `CancelOfferItem` | Byte: slot |

---

## 9. Client → Server Packets (ClientPacketID)

130 packet types. VB6 enum auto-increments from 0.

| ID | Name | Payload |
|----|------|---------|
| 0 | `LoginExistingChar` | String: name, String: password, Byte×3: version |
| 1 | `ThrowDices` | — |
| 2 | `LoginNewChar` | String: name, String: password, Byte×3: version, Byte: race, Byte: gender, Byte: class, Integer: head, String: email, Byte: homeCity |
| 3 | `Talk` | String: chat |
| 4 | `Yell` | String: chat |
| 5 | `Whisper` | String: targetName, String: chat |
| 6 | `Walk` | Byte: heading (1=N, 2=E, 3=S, 4=W) |
| 7 | `RequestPositionUpdate` | — |
| 8 | `Attack` | — |
| 9 | `PickUp` | — |
| 10 | `SafeToggle` | — |
| 11 | `ResuscitationSafeToggle` | — |
| 12 | `RequestGuildLeaderInfo` | — |
| 13 | `RequestAtributes` | — |
| 14 | `RequestFame` | — |
| 15 | `RequestSkills` | — |
| 16 | `RequestMiniStats` | — |
| 17 | `CommerceEnd` | — |
| 18 | `UserCommerceEnd` | — |
| 19 | `UserCommerceConfirm` | — |
| 20 | `CommerceChat` | String: chat |
| 21 | `BankEnd` | — |
| 22 | `UserCommerceOk` | — |
| 23 | `UserCommerceReject` | — |
| 24 | `Drop` | Byte: slot, Integer: amount |
| 25 | `CastSpell` | Byte: slot |
| 26 | `LeftClick` | Byte: x, Byte: y |
| 27 | `DoubleClick` | Byte: x, Byte: y |
| 28 | `Work` | Byte: skill |
| 29 | `UseSpellMacro` | — |
| 30 | `UseItem` | Byte: slot |
| 31 | `CraftBlacksmith` | Integer: itemIndex |
| 32 | `CraftCarpenter` | Integer: itemIndex |
| 33 | `WorkLeftClick` | Byte: x, Byte: y, Byte: skill |
| 34 | `CreateNewGuild` | String: desc, String: name, String: site, String: codex (null-separated) |
| 35 | `SpellInfo` | Byte: slot |
| 36 | `EquipItem` | Byte: slot |
| 37 | `ChangeHeading` | Byte: heading |
| 38 | `ModifySkills` | Byte×20: skills array |
| 39 | `Train` | Byte: petIndex |
| 40 | `CommerceBuy` | Byte: slot, Integer: amount |
| 41 | `BankExtractItem` | Byte: slot, Integer: amount |
| 42 | `CommerceSell` | Byte: slot, Integer: amount |
| 43 | `BankDeposit` | Byte: slot, Integer: amount |
| 44 | `ForumPost` | Byte: forumMsgType, String: title, String: body |
| 45 | `MoveSpell` | Boolean: moveUp, Byte: slot |
| 46 | `MoveBank` | Byte: origin, Byte: dest |
| 47 | `ClanCodexUpdate` | String: desc, String: codex (null-separated) |
| 48 | `UserCommerceOffer` | Byte: slot, Long: amount, Byte: offerSlot |
| 49 | `GuildAcceptPeace` | String: guildName |
| 50 | `GuildRejectAlliance` | String: guildName |
| 51 | `GuildRejectPeace` | String: guildName |
| 52 | `GuildAcceptAlliance` | String: guildName |
| 53 | `GuildOfferPeace` | String: guildName, String: proposal |
| 54 | `GuildOfferAlliance` | String: guildName, String: proposal |
| 55 | `GuildAllianceDetails` | String: guildName |
| 56 | `GuildPeaceDetails` | String: guildName |
| 57 | `GuildRequestJoinerInfo` | String: userName |
| 58 | `GuildAlliancePropList` | — |
| 59 | `GuildPeacePropList` | — |
| 60 | `GuildDeclareWar` | String: guildName |
| 61 | `GuildNewWebsite` | String: url |
| 62 | `GuildAcceptNewMember` | String: userName |
| 63 | `GuildRejectNewMember` | String: userName, String: reason |
| 64 | `GuildKickMember` | String: userName |
| 65 | `GuildUpdateNews` | String: news |
| 66 | `GuildMemberInfo` | String: userName |
| 67 | `GuildOpenElections` | — |
| 68 | `GuildRequestMembership` | String: guildName, String: application |
| 69 | `GuildRequestDetails` | String: guildName |
| 70 | `Online` | — |
| 71 | `Quit` | — |
| 72 | `GuildLeave` | — |
| 73 | `RequestAccountState` | — |
| 74 | `PetStand` | — |
| 75 | `PetFollow` | — |
| 76 | `ReleasePet` | — |
| 77 | `TrainList` | — |
| 78 | `Rest` | — |
| 79 | `Meditate` | — |
| 80 | `Resucitate` | — |
| 81 | `Heal` | — |
| 82 | `Help` | — |
| 83 | `RequestStats` | — |
| 84 | `CommerceStart` | — |
| 85 | `BankStart` | — |
| 86 | `Enlist` | — |
| 87 | `Information` | — |
| 88 | `Reward` | — |
| 89 | `RequestMOTD` | — |
| 90 | `Uptime` | — |
| 91 | `PartyLeave` | — |
| 92 | `PartyCreate` | — |
| 93 | `PartyJoin` | — |
| 94 | `Inquiry` | — |
| 95 | `GuildMessage` | String: chat |
| 96 | `PartyMessage` | String: chat |
| 97 | `CentinelReport` | Integer: code |
| 98 | `GuildOnline` | — |
| 99 | `PartyOnline` | — |
| 100 | `CouncilMessage` | String: chat |
| 101 | `RoleMasterRequest` | String: request |
| 102 | `GMRequest` | — |
| 103 | `bugReport` | String: report |
| 104 | `ChangeDescription` | String: desc |
| 105 | `GuildVote` | String: userName |
| 106 | `Punishments` | String: userName |
| 107 | `ChangePassword` | String: oldPass, String: newPass |
| 108 | `Gamble` | Integer: gold |
| 109 | `InquiryVote` | Byte: option |
| 110 | `LeaveFaction` | — |
| 111 | `BankExtractGold` | Long: amount |
| 112 | `BankDepositGold` | Long: amount |
| 113 | `Denounce` | String: text |
| 114 | `GuildFundate` | — |
| 115 | `GuildFundation` | Byte: clanType |
| 116 | `PartyKick` | Integer: userIndex |
| 117 | `PartySetLeader` | Integer: userIndex |
| 118 | `PartyAcceptMember` | String: userName |
| 119 | `Ping` | — |
| 120 | `RequestPartyForm` | — |
| 121 | `ItemUpgrade` | Integer: itemIndex |
| 122 | `GMCommands` | Byte: subCommand (eGMCommands), variable payload |
| 123 | `InitCrafting` | — |
| 124 | `Home` | — |
| 125 | `ShowGuildNews` | — |
| 126 | `ShareNpc` | — |
| 127 | `StopSharingNpc` | — |
| 128 | `Consultation` | — |
| 129 | `MoveItem` | Byte: originalSlot, Byte: newSlot, Byte: moveType |

---

## 10. MultiMessage System

`MultiMessage` (ServerPacketID 104) is a **bandwidth optimization**. Instead of using a unique 1-byte packet ID for each common combat/status message, the server multiplexes them under a single packet ID with a sub-type byte.

### Wire Format

```
[Byte: 104] [Byte: eMessages subType] [variable payload per subType]
```

### eMessages Sub-Types

| SubID | Name | Extra Payload | Description |
|-------|------|---------------|-------------|
| 0 | `DontSeeAnything` | — | "You don't see anything interesting" |
| 1 | `NPCSwing` | — | NPC missed attack |
| 2 | `NPCKillUser` | — | NPC killed user |
| 3 | `BlockedWithShieldUser` | — | User blocked with shield |
| 4 | `BlockedWithShieldOther` | — | Other user blocked with shield |
| 5 | `UserSwing` | — | User missed attack |
| 6 | `SafeModeOn` | — | Safe mode enabled |
| 7 | `SafeModeOff` | — | Safe mode disabled |
| 8 | `ResuscitationSafeOff` | — | Resuscitation safe off |
| 9 | `ResuscitationSafeOn` | — | Resuscitation safe on |
| 10 | `NobilityLost` | — | Lost nobility status |
| 11 | `CantUseWhileMeditating` | — | Action blocked during meditation |
| 12 | `NPCHitUser` | Byte: bodyPart, Integer: damage | NPC hit user |
| 13 | `UserHitNPC` | Long: damage | User hit NPC |
| 14 | `UserAttackedSwing` | Integer: attackerCharIndex | Attacked but missed |
| 15 | `UserHittedByUser` | Integer: attackerCharIndex, Byte: bodyPart, Integer: damage | Hit by another user |
| 16 | `UserHittedUser` | Integer: victimCharIndex, Byte: bodyPart, Integer: damage | User hit another user |
| 17 | `WorkRequestTarget` | Byte: skillType | Server requests work target |
| 18 | `HaveKilledUser` | Integer: killedCharIndex, Long: expGained | Killed another user |
| 19 | `UserKill` | Integer: killerCharIndex | Killed by another user |
| 20 | `EarnExp` | *(commented out)* | — |
| 21 | `GoHome` | Byte: distance, Integer: time, String: homeCity | Going home |
| 22 | `CancelGoHome` | — | Home travel cancelled |
| 23 | `FinishHome` | — | Arrived home |

---

## 11. GM Commands Sub-Protocol

GM commands are multiplexed under `ClientPacketID.GMCommands` (ID 122):

```
[Byte: 122] [Byte: eGMCommands subCommand] [variable payload]
```

Over **100 GM sub-commands** exist, including:

| SubCmd | Name | Payload |
|--------|------|---------|
| 0 | `QueryGMState` | — |
| 1 | `TurnOffServer` | — |
| 2 | `TurnCriminal` | String: userName |
| 3 | `ResetFactions` | String: userName |
| 4 | `RemoveCharFromGuild` | String: userName |
| 5 | `RequestCharMail` | String: userName |
| 6 | `AlterPassword` | String: userName, String: newPass |
| 7 | `AlterMail` | String: userName, String: newMail |
| 8 | `AlterName` | String: userName, String: newName |
| 9 | `ToggleCentinelActivated` | — |
| 10 | `DoBackUp` | — |
| 11 | `ShowGuildMessages` | String: guildName |
| 12 | `SaveMap` | — |
| 13 | `ChangeMapInfoPK` | Boolean: isPK |
| ... | ... | ... |
| 51 | `WarpChar` | String: userName, Integer: map, Byte: x, Byte: y |
| 52 | `Silence` | String: userName |
| ... | ... | ... |
| 97 | `AlterGuildName` | String: guildName, String: newName |
| 98 | `HigherAdminsMessage` | String: message |

*(Full list: ~100 sub-commands covering moderation, world editing, user management, server control)*

---

## 12. Broadcast Routing (SendTarget)

The server's `SendData()` function (modSendData.bas) routes packets to different audiences:

| Target | Description |
|--------|-------------|
| `ToAll` | All logged-in users |
| `ToAllButIndex` | All except sender |
| `ToPCArea` | All users in sender's visibility area |
| `ToPCAreaButIndex` | Area except sender |
| `ToMapButIndex` | Same map except sender |
| `ToGuildMembers` | Online guild members |
| `ToAdmins` | Admin/Dios/SemiDios/Consejero |
| `ToDeadArea` | Dead users in area + admins |
| `ToPCAreaButGMs` | Area excluding GMs |
| `ToPartyArea` | Party members in area |
| `ToClanArea` | Clan members in area |
| ... | ~25 total routing targets |

Area-based routing uses a **bitmask system** (`AreaPerteneceX/Y`, `AreaReciveX/Y`) for efficient visibility checks.

---

## 13. Key Packet Formats (Detailed)

### Login — Existing Character (Client → Server, ID 0)

```
Offset  Size   Field
0       1      PacketID (0x00)
1       2      Username length (LE)
3       N      Username (ASCII)
3+N     2      Password length (LE)
5+N     M      Password (ASCII)
5+N+M   1      Version Major
6+N+M   1      Version Minor
7+N+M   1      Version Revision
```

With `SeguridadAlkon`:
```
Offset  Size   Field
...     32     Password (fixed MD5 hash, no length prefix)
...     1      Version Major
...     1      Version Minor
...     1      Version Revision
...     16     Client MD5 hash (fixed, no length prefix)
```

### Login — New Character (Client → Server, ID 2)

```
Offset  Size   Field
0       1      PacketID (0x02)
1       2+N    Username (String)
...     2+M    Password (String)
...     1      Version Major
...     1      Version Minor
...     1      Version Revision
...     1      Race (1=Human, 2=Elf, 3=DarkElf, 4=Dwarf, 5=Gnome)
...     1      Gender (1=Male, 2=Female)
...     1      Class (1-12)
...     2      Head graphic index (Integer)
...     2+K    Email (String)
...     1      Home City (1-5)
```

### CharacterCreate (Server → Client, ID 29)

```
Offset  Size   Field
0       1      PacketID (29)
1       2      CharIndex (Integer)
3       2      Body (Integer) — body animation index
5       2      Head (Integer) — head graphic index
7       1      Heading (Byte) — 1=N, 2=E, 3=S, 4=W
8       1      X (Byte) — map X position
9       1      Y (Byte) — map Y position
10      2      Weapon (Integer) — weapon animation index
12      2      Shield (Integer) — shield animation index
14      2      Helmet (Integer) — helmet animation index
16      2      FX Index (Integer) — visual effect
18      2      FX Loops (Integer) — FX loop count
20      2+N    Name (String)
...     1      NickColor (Byte) — color/criminal status
...     1      Privileges (Byte) — user role bitmask
```

### CharacterChange (Server → Client, ID 34)

```
Offset  Size   Field
0       1      PacketID (34)
1       2      CharIndex (Integer)
3       2      Body (Integer)
5       2      Head (Integer)
7       1      Heading (Byte)
8       2      Weapon (Integer)
10      2      Shield (Integer)
12      2      Helmet (Integer)
14      2      FX Index (Integer)
16      2      FX Loops (Integer)
```

Total: 18 bytes fixed.

### CharacterMove (Server → Client, ID 32)

```
Offset  Size   Field
0       1      PacketID (32)
1       2      CharIndex (Integer)
3       1      X (Byte)
4       1      Y (Byte)
```

Total: 5 bytes fixed.

### UpdateUserStats (Server → Client, ID 45)

```
Offset  Size   Field
0       1      PacketID (45)
1       2      MaxHP (Integer)
3       2      MinHP (Integer)
5       2      MaxMana (Integer)
7       2      MinMana (Integer)
9       2      MaxSta (Integer)
11      2      MinSta (Integer)
13      4      Gold (Long)
17      1      Level (Byte)
18      4      ExpToNextLevel (Long)
22      4      CurrentExp (Long)
```

Total: 26 bytes fixed.

### ChangeInventorySlot (Server → Client, ID 47)

```
Offset  Size   Field
0       1      PacketID (47)
1       1      Slot (Byte)
2       2      ObjIndex (Integer)
4       2+N    Name (String)
...     2      Amount (Integer)
...     1      Equipped (Boolean)
...     2      GrhIndex (Integer)
...     1      ObjType (Byte)
...     2      MaxHit (Integer)
...     2      MinHit (Integer)
...     2      Def (Integer)
...     4      Value (Single)
```

### Walk (Client → Server, ID 6)

```
Offset  Size   Field
0       1      PacketID (0x06)
1       1      Heading (Byte) — 1=N, 2=E, 3=S, 4=W
```

Total: 2 bytes.

---

## 14. Security Features

| Feature | Description |
|---------|-------------|
| **IP Rate Limiting** | Minimum 5000ms between connections from same IP |
| **Connection Limit** | Maximum 10 concurrent connections per IP |
| **Anti-SpeedHack** | 30 walk packets in <5800ms → kick |
| **Pre-Auth Gate** | Only 3 packet IDs accepted before login |
| **Unknown Packets** | Unknown packet ID → immediate socket close |
| **Version Check** | Client version validated during login |
| **`SeguridadAlkon`** | Optional: MD5 password hashing + client integrity check |

### What's NOT Present

- No wire encryption (no XOR, no AES, no TLS)
- No packet signing or HMAC
- No replay protection
- No sequence numbers
- No compression
- No rate limiting per packet type (except walk)

---

## 15. Comparison: 13.3 vs TSAO (11.5)

| Feature | TSAO (v11.5) | AO 13.3 |
|---------|-------------|---------|
| **Wire format** | Text-based (ASCII strings, comma-separated) | **Binary** (typed fields, no delimiters) |
| **Packet ID** | Variable-length text opcodes (`CC`, `M1`, `PT`, `+`, `[CD`) | **1-byte numeric opcode** (0-129) |
| **Encryption** | 4-layer: XOR + base64 + hex + key rotation | **None** |
| **Key rotation** | `Numero2Letra` → `Semilla`, counter 0→999999 | N/A |
| **String encoding** | Inline, comma-separated | 2-byte LE length prefix + ASCII |
| **Buffer** | VB6 String concatenation | `clsByteQueue` (10KB FIFO, RtlMoveMemory) |
| **Framing** | Implicit (opcodes of known length + delimiters) | Implicit (1-byte opcode + known field sizes) |
| **Socket impl** | Winsock control | Raw Winsock2 API (ws2_32.dll) |
| **Login** | Text: `OLOGIN\nuser\npass\nver` | Binary: `[0x00][String:user][String:pass][Byte×3:ver]` |
| **Char create** | `CC{body},{head},{heading},{idx},{x},{y},...` (comma-separated text) | Binary: `[29][Int:charIdx][Int:body][Int:head]...` |
| **Movement** | `M1`-`M4` (text direction) | `[0x06][Byte:heading]` (binary) |
| **Broadcast** | Per-packet routing | `SendTarget` enum + `PrepareMessage` pattern |
| **MultiMessage** | N/A | 24 sub-types for combat/status optimization |
| **GM commands** | Individual text commands | Nested binary dispatch (100+ sub-commands under ID 122) |
| **Server packets** | ~228 text opcodes | **107 binary opcodes** (+ 24 MultiMessage sub-types) |
| **Client packets** | ~120 text opcodes | **130 binary opcodes** (+ 100 GM sub-commands) |
| **Anti-cheat** | Encryption as obfuscation | Optional MD5 integrity + IP rate limiting |
| **Performance** | String parsing overhead | Zero-copy memory operations |

### Key Implications for argentum-nextgen

1. **Binary protocol is strictly better**: No parsing overhead, smaller wire size, type-safe
2. **13.3's approach should be the target**: Adopt binary protocol with `ByteQueue`-style buffers
3. **Drop encryption entirely**: It provided no real security in TSAO (obfuscation only). Use TLS if real security is needed.
4. **MultiMessage is worth keeping**: Reduces bandwidth for frequent combat messages
5. **GM sub-protocol is cleaner**: Single dispatch point for all GM commands
6. **IP security should be preserved**: Rate limiting and connection limits are essential
7. **Consider adding**: Packet length prefixes (simplifies framing), sequence numbers (prevents replay), optional TLS

---

*Document generated from source analysis of [`Comunidad-Winter/Argentum-Online`](https://github.com/Comunidad-Winter/Argentum-Online) (v13.3).*
*Reference for the argentum-nextgen project.*

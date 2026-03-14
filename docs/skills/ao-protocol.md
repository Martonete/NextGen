---
name: ao-protocol
description: Use when adding new network packets, modifying ByteQueue serialization, or debugging client-server communication in the binary protocol layer.
---

# AO Network Protocol Reference

Source: `client/Scripts/Network/ByteQueue.cs`, `server/source/protocol/`

Source: `client/Scripts/Network/ByteQueue.cs`, `client/Scripts/Network/PacketIds.cs`,
`client/Scripts/Network/PacketHandler.cs`, `client/Scripts/Network/PacketHandler.Binary.cs`,
`client/Scripts/Network/PacketHandler.Movement.cs`

---

## 1. ByteQueue — FIFO Byte Buffer

Mirrors VB6 `clsByteQueue.cls` and Rust `byte_queue.rs`. All data is little-endian,
matching VB6 memory layout.

### Data Type Wire Format

| Type | Size | Encoding | C# Read/Write |
|------|------|----------|----------------|
| Byte | 1 | unsigned 0-255 | ReadByte() / WriteByte() |
| Boolean | 1 | 0=false, nonzero=true | ReadBoolean() / WriteBoolean() |
| Integer | 2 | i16 LE (VB6 Integer) | ReadInteger() / WriteInteger() |
| Long | 4 | i32 LE (VB6 Long) | ReadLong() / WriteLong() |
| Single | 4 | IEEE 754 float LE | ReadSingle() / WriteSingle() |
| Double | 8 | IEEE 754 double LE | ReadDouble() / WriteDouble() |
| String | 2+N | i16 LE length prefix + N bytes Latin-1 | ReadString() / WriteString() |

### Important: String Encoding

Strings use **Latin-1** (ISO 8859-1), NOT UTF-8. The length prefix is a 2-byte
signed integer (max string length = 32,767 bytes). VB6's `StrConv` produced
Latin-1 by default.

### Buffer Management

```csharp
// Writing: starts empty with 256-byte buffer, auto-grows (doubling)
var bq = new ByteQueue();
bq.WriteByte(opcode);
bq.WriteString("hello");
byte[] packet = bq.ToArray();

// Reading: wrap existing data
var bq = new ByteQueue(rawData);
byte op = bq.ReadByte();
string name = bq.ReadString();

// Zero-copy wrap (caller must not mutate buffer)
bq.Wrap(buffer, offset, length);
```

### Position Save/Restore (Partial Packet Handling)

```csharp
int saved = bq.ReadPosition;   // save position
try {
    HandlePacket(bq);           // may throw if not enough data
} catch (InvalidOperationException) {
    bq.RestorePosition(saved);  // rollback — wait for more data
}
```

The `Available` property returns `_writePos - _readPos` (bytes remaining).

---

## 2. Binary Protocol Overview

### Server -> Client

- **1-byte opcode** + typed binary fields
- No null terminator, no framing (raw binary stream over TCP)
- Partial packets handled via ByteQueue position save/restore

### Client -> Server

- Same binary format for native packets
- Legacy text packets still exist (wrapped in GenericText opcode 255)

### Receive Buffer

`PacketHandler` maintains a 64KB receive buffer (`_recvBuf`). TCP data is
appended via `RecvAppend()`, then packets are consumed from the front.

### Dispatch Flow

1. `RecvAppend(data)` — append TCP bytes
2. Peek opcode byte
3. If opcode == 255 (GenericText): read `len(u16 LE)` + text, dispatch to text handlers
4. Otherwise: dispatch to `HandleBinaryPacket(bq)` switch table
5. On `InvalidOperationException` (not enough data): restore position, wait

---

## 3. Server -> Client Opcodes (ServerPacketId)

### Core Packets (0-50)

| ID | Name | Fields |
|----|------|--------|
| 0 | Logged | (none) |
| 1 | Disconnect | string reason |
| 2 | ErrorMsg | string msg |
| 3 | ShowMessageBox | string msg |
| 4 | UserIndexInServer | i16 index |
| 5 | UserCharIndexInServer | i16 charIndex |
| 6 | CommerceEnd | (none) |
| 7 | BankEnd | (none) |
| 8 | CommerceInit | (none) |
| 9 | BankInit | i32 gold |
| 16 | UpdateSta | i16 minSta, i16 maxSta |
| 17 | UpdateMana | i16 minMana, i16 maxMana |
| 18 | UpdateHP | i16 minHP, i16 maxHP |
| 19 | UpdateGold | i32 gold |
| 20 | UpdateExp | i32 exp |
| 21 | ChangeMap | i16 mapNum, i16 version, u8 r, u8 g, u8 b |
| 22 | PosUpdate | u8 x, u8 y |
| 23 | ChatOverHead | string text, i16 charIdx, u8 r, u8 g, u8 b |
| 24 | ConsoleMsg | string text, u8 fontIdx, u8 bold, u8 r, u8 g, u8 b |
| 29 | CharacterCreate | (see Section 5) |
| 30 | CharacterRemove | i16 charIndex |
| 31 | CharacterMove | i16 charIdx, u8 heading |
| 33 | CharacterChange | (complex, see source) |
| 34 | ObjectCreate | u8 x, u8 y, i16 grhIdx, string name |
| 35 | ObjectDelete | u8 x, u8 y |
| 37 | PlayMusic | i16 musicId |
| 38 | PlayWave | u8 wav, u8 x, u8 y |
| 40 | AreaChanged | u8 areaX, u8 areaY |
| 41 | PauseToggle | (none) |
| 42 | RainToggle | (none) |
| 43 | CreateFX | i16 charIdx, i16 fxIdx, i16 loops |
| 44 | UpdateUserStats | (bulk stat update) |
| 46 | ChangeInventorySlot | (slot data) |
| 48 | ChangeSpellSlot | (slot data) |

### Extended Opcodes (100+)

| ID | Name | Origin |
|----|------|--------|
| 99 | AuraUpdate | binary |
| 107 | HeadingChange | \|H text |
| 108 | Arrow | FLECHI text |
| 109 | NavigateBroadcast | NVG text |
| 120-128 | Stat* variants | individual stat updates |
| 130-141 | Combat state | safe/hit/swing/pvp/death |
| 142 | UserMount | USM text |
| 143 | Levitate | MVOL text |
| 150 | PlaySound | non-spatial sound |
| 164 | AmbientColor | PCR text |
| 211 | CharParticleCreate | CFF/PCB text |
| 243 | ParticleCreate | PCF text |
| 244 | LightCreate | PCL text |
| 250 | Ping | keepalive |
| 255 | GenericText | legacy text wrapper |

### GenericText Wrapper (Opcode 255)

```
[255][len:u16 LE][text_bytes]
```

Used by the server's bridge layer during VB6->Rust protocol migration.
The client parses the inner text using legacy dispatch (prefix matching).

---

## 4. Client -> Server Opcodes (ClientPacketId)

| Range | Category | Examples |
|-------|----------|---------|
| 0-9 | Pre-login | HardwareCheck(0), AccountLogin(1), CreateCharacter(2), CreateAccount(5) |
| 10-14 | Movement | Walk(10), ChangeHeading(11), RequestPos(12) |
| 20-24 | Combat | Attack(20), CastSpell(21), LeftClick(22) |
| 30-35 | Chat | Talk(30), Yell(31), Whisper(32), SlashCommand(33) |
| 40-49 | Items | PickUp(40), DropItem(41), UseItem(42), EquipItem(44) |
| 50-55 | Skills | UseSkill(50), Meditate(52), SafeToggle(53) |
| 60-63 | Spells | SpellInfo(60), MoveSpell(61), CastByName(62) |
| 70-79 | Commerce | CommerceBuy(70), CommerceSell(71), Trade*(73-77) |
| 80-85 | Banking | BankDeposit(80), BankWithdraw(81), BankClose(82) |
| 90-94 | Crafting | ConstructSmith(90), ConstructCarp(91) |
| 100-119 | Guild | GuildInfo(100), GuildCreate(101), GuildBank*(111-117) |
| 120-122 | Quest | QuestList(120), QuestInfo(121), QuestAccept(122) |
| 125-128 | Mail | MailSend(125), MailOpen(126), MailExtract(127) |
| 140-149 | Player info | PlayerInfo(140), Rankings(143) |
| 150+ | Misc | HouseQuery(150), DragDrop(160), Vote(161) |

---

## 5. Key Packet Byte Layouts

### CharacterCreate (Opcode 29)

```
[29]                    opcode      u8
charIndex               i16         2 bytes
body                    i16         2 bytes
head                    i16         2 bytes
heading                 u8          1 byte
posX                    u8          1 byte
posY                    u8          1 byte
weapon                  i16         2 bytes
shield                  i16         2 bytes
helmet                  i16         2 bytes
fxIndex                 i16         2 bytes
fxLoops                 i16         2 bytes
name                    string      2+N bytes (i16 len + Latin-1)
npcNumber               u8          1 byte
privileges              u8          1 byte
```

Total: 22 + name length bytes.

### PosUpdate (Opcode 22)

```
[22]                    opcode      u8
posX                    u8          1 byte
posY                    u8          1 byte
```

Total: 3 bytes. Warps the player to (x, y) on the current map.

### ChangeMap (Opcode 21)

```
[21]                    opcode      u8
mapNumber               i16         2 bytes
mapVersion              i16         2 bytes
ambientR                u8          1 byte
ambientG                u8          1 byte
ambientB                u8          1 byte
```

Total: 8 bytes. Triggers full map load + character clear.

### PlayWave (Opcode 38)

```
[38]                    opcode      u8
wavId                   u8          1 byte
srcX                    u8          1 byte
srcY                    u8          1 byte
```

Total: 4 bytes. Plays spatial sound at map coordinates (x, y).

### CreateFX (Opcode 43)

```
[43]                    opcode      u8
charIndex               i16         2 bytes
fxIndex                 i16         2 bytes
fxLoops                 i16         2 bytes
```

Total: 7 bytes. Creates a visual effect on a character.

---

## 6. How to Add a New Packet

### Step 1: Define Opcode Constants

**Server** (`server-rust/src/protocol/mod.rs`):
```rust
pub const MY_NEW_PACKET: u8 = <next_free_id>;
```

**Client** (`client/Scripts/Network/PacketIds.cs`):
```csharp
public const byte MyNewPacket = <same_id>;
```

### Step 2: Server — Write Packet Builder

In `server-rust/src/protocol/binary_packets.rs`:
```rust
pub fn build_my_new_packet(value: i16, name: &str) -> Vec<u8> {
    let mut bq = ByteQueue::new();
    bq.write_byte(MY_NEW_PACKET);
    bq.write_integer(value);
    bq.write_string(name);
    bq.to_vec()
}
```

### Step 3: Client — Add Handler

In the appropriate `PacketHandler.*.cs` partial class:
```csharp
private void HandleBinMyNewPacket(ByteQueue bq)
{
    short value = bq.ReadInteger();
    string name = bq.ReadString();
    _state.SomeField = value;
    GD.Print($"[PKT] MyNewPacket: value={value} name={name}");
}
```

### Step 4: Add to Dispatch Map

In `PacketHandler.Binary.cs`, add to the switch:
```csharp
case ServerPacketId.MyNewPacket:
    HandleBinMyNewPacket(bq);
    break;
```

### Step 5: Sending Client -> Server

In `client/Scripts/Network/Connection.cs` or the relevant sender:
```csharp
var bq = new ByteQueue();
bq.WriteByte(ClientPacketId.MyNewPacket);
bq.WriteInteger(someValue);
Send(bq.ToArray());
```

---

## 7. Partial Packet Safety

All handlers may throw `InvalidOperationException` when ByteQueue runs out
of data (TCP delivered a partial packet). The dispatch loop:

1. Saves `bq.ReadPosition`
2. Attempts to read the full packet
3. On exception: restores position, returns (waits for next TCP read)
4. On success: consumes bytes from the front of the receive buffer

This is why ByteQueue methods check `Available` before reading.

---

## 8. Legacy Text Protocol (GenericText Wrapper)

Some packets still arrive as text wrapped in opcode 255:

```
[255][len:u16 LE][ascii_text_with_null]
```

Text format: ASCII/Latin-1, fields separated by commas or specific prefixes.
Dispatch in text handlers uses prefix matching (e.g., `"CC"` for CharacterCreate,
`"|H"` for HeadingChange).

The text protocol had 4-layer encryption for client->server:
1. Hex encode
2. Base64 encode
3. XOR cipher
4. Null-byte termination

This encryption only applies to the legacy client->server text path.
Binary packets (both directions) are **NOT encrypted**.

---

## 9. Packet Handler Architecture

The handler is split across partial class files:

| File | Responsibility |
|------|---------------|
| PacketHandler.cs | Infrastructure: receive buffer, dispatch, fields |
| PacketHandler.Binary.cs | Binary opcode switch table (200+ cases) |
| PacketHandler.Auth.cs | Login, logged, disconnect, error |
| PacketHandler.Movement.cs | ChangeMap, PosUpdate, CharCreate/Move/Remove |
| PacketHandler.Combat.cs | HP/Mana/Sta updates, FX, death, stats |
| PacketHandler.Social.cs | Chat, guild, quest, mail |
| PacketHandler.Commerce.cs | NPC commerce, bank, player trade |
| PacketHandler.Inventory.cs | Inventory slots, spell slots |
| PacketHandler.Helpers.cs | Utilities: ParseInt, IsDeadHead, etc. |
| PacketHandler.Text*.cs | Legacy text protocol handlers |

### Callbacks (Actions)

| Callback | Signature | Trigger |
|----------|-----------|---------|
| OnMapLoad | Action | ChangeMap received |
| OnPlaySound | Action\<int\> | Non-spatial sound |
| OnPlaySoundAt | Action\<int, byte, byte\> | Spatial sound at (x, y) |
| OnPlayMusic | Action\<int\> | Music change |
| OnFloatingText | Action\<int, string, string\> | Damage/heal text |
| OnStopSfx | Action | Position warp (same map) |

---

## 10. Common Debugging Tips

1. **Packet too short**: If handler throws, check that the server writes all
   fields in the exact same order and type.
2. **Endianness**: Everything is little-endian. `ReadInteger()` reads low byte
   first: `val = data[pos] | (data[pos+1] << 8)`.
3. **String length**: If strings are garbled, check the 2-byte length prefix.
   A wrong length causes all subsequent fields to be misaligned.
4. **Opcode mismatch**: Server and client must agree on the exact opcode number.
   Check both `PacketIds.cs` and `server-rust/src/protocol/mod.rs`.
5. **GenericText fallback**: If a binary packet is not yet implemented, the
   server may send it wrapped in GenericText (255). Check the text handlers.

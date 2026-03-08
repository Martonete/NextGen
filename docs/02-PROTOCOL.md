# Communication Protocol

## Overview

Argentum Online uses a text-based TCP protocol. All packets are ASCII/Latin-1 strings terminated by a null byte (`0x00`). The protocol has 4 encryption layers for security.

## Encryption Pipeline

### Outbound (Server → Client)

```
PlainText
  → AoDefServEncrypt: Convert each byte to 2-char hex (e.g., 0x41 → "41")
  → AoDefEncode: Custom base64 encoding (VB6-specific alphabet)
  → Append 0x00 terminator
  → Send over TCP
```

### Inbound (Client → Server)

```
Receive TCP data (null-terminated)
  → AoDefDecode: Custom base64 decoding
  → DeCodificar: XOR stream cipher with rotating key
  → PlainText (opcode + fields)
```

### Key Rotation (XOR Cipher)

The XOR cipher key rotates per packet:
1. Counter starts at 0, increments after each packet (wraps at 999,999)
2. `Numero2Letra(counter)` converts number to Spanish words (e.g., 123 → "ciento veinti tres")
3. `Semilla(words)` derives a numeric seed from the word string
4. The seed is used as the XOR key for `DeCodificar`

**Files**: `crypto/aodef_cipher.rs`, `crypto/aodef_converter.rs`

## Packet Format

### General Structure

Packets are plain strings with an opcode prefix followed by fields:
```
<OPCODE><field1>,<field2>,...
```

Common delimiters:
- `,` (comma) — most fields
- `°` (ASCII 176) — chat messages with char index
- `~` (ASCII 126) — font styling in whispers
- `¦` (BF / ASCII 166) — guild data fields

### Client Opcodes (Inbound)

#### Pre-Login
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `KERD22` | HardwareCheck | `hd_serial` | HD serial for ban check |
| `ALOGIN` | AccountLogin | `account,password,version,hd` | Login to account |
| `NACCNT` | NewAccount | `account,password,email,pin,code` | Create account |
| `OOLOGI` | CharSelect | `char_name,account,password` | Select character |
| `THCJXD` | CharSelectAlt | `char_name,account,password` | Alt char select |
| `NLOGIN` | NewChar | `name,race,gender,class,head,mail,hogar,...,attrs,code` | Create character |
| `TIRDAD` | DiceRoll | *(none)* | Roll attributes |
| `TBRP` | DeleteChar | `char_name,account,password` | Delete character |
| `REPASS` | ChangePass | `account,old_pass,new_pass` | Change password |
| `REECUH` | Recovery | `account,pin` | Account recovery |

#### Movement & Combat
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `M` | Walk | `heading` | Move in direction (1=N, 2=E, 3=S, 4=W) |
| `AT` | Attack | *(none)* | Melee/ranged attack in facing direction |
| `LH` | CastSpell | `spell_slot` | Cast equipped spell |
| `SEG` | SafeToggle | *(none)* | Toggle PvP safety |
| `CHEA` | ChangeHeading | `heading` | Face direction without moving |

#### Inventory & Items
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `USA` | UseItem | `slot` | Use item (potions, boats, scrolls) |
| `QSA` | UseItemClick | `slot` | Use item via double-click |
| `EQUI` | EquipItem | `slot` | Equip/unequip item |
| `AG` | PickUp | *(none)* | Pick up item from ground |
| `TI` | DropItem | `slot,amount` | Drop item |
| `SWAP` | SwapSlots | `slot1,slot2` | Swap inventory positions |

#### Commerce
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `COMP` | Buy | `slot,amount` | Buy from NPC |
| `VEND` | Sell | `slot,amount` | Sell to NPC |
| `FINCOM` | CloseCommerce | *(none)* | Close shop window |
| `DEPO` | BankDeposit | `slot,amount` | Deposit to bank |
| `RETI` | BankWithdraw | `slot,amount` | Withdraw from bank |
| `FINBAN` | CloseBank | *(none)* | Close bank window |

#### Chat
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `;` | Talk | `message` | Area chat |
| `-` | Yell | `message` | Wide-area yell |
| `\` | Whisper | `target message` | Private message |

#### Crafting (Work Left Click)
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `WLC` | WorkLeftClick | `x,y,skill` | Use skill on tile |
| `CNS` | ConstructSmith | `item_index` | Blacksmith crafting |
| `CNC` | ConstructCarp | `item_index` | Carpentry crafting |

#### Guilds
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `GLINFO` | GuildInfo | *(none)* | Open guild panel |
| `CIG` | CreateGuild | `name¦desc¦codex¦...` | Create guild |
| `SOLICITUD` | Apply | `guild_name` | Apply to guild |
| `ACEPTARI` | Accept | `player_name` | Accept applicant |
| `RECHAZAR` | Reject | `player_name` | Reject applicant |
| `ECHARCLA` | Expel | `player_name` | Expel member |

#### Quests
| Opcode | Name | Fields | Description |
|--------|------|--------|-------------|
| `IQUEST` | QuestList | *(none)* | Request available quests |
| `INFD` | QuestInfo | `quest_index` | Get quest details |
| `ACQT` | AcceptQuest | `quest_index` | Accept quest |

### Server Opcodes (Outbound)

#### Login Flow
| Opcode | Format | Description |
|--------|--------|-------------|
| `INIAC` | `INIAC<num_chars>,<notice>` | Account accepted |
| `ADDPJ` | `ADDPJ<name>,<level>,<class>,<map>,<body>,<head>,<weapon>,<shield>,<helmet>` | Character in selection |
| `CODEH` | `CODEH<code>` | Security code |
| `LOGGED` | `LOGGED` | Login complete |
| `ERR` | `ERR<message>` | Error (disconnects) |
| `ERO` | `ERO<message>` | Error (dialog only) |

#### World State
| Opcode | Format | Description |
|--------|--------|-------------|
| `CC` | `CC<body>,<head>,<heading>,<charindex>,<x>,<y>,<weapon>,<shield>,<helmet>,<name>,<status>,<priv>,<aura>` | Create character |
| `BP` | `BP<charindex>` | Remove character |
| `MP` | `MP<charindex>,<x>,<y>` | Move character |
| `CM` | `CM<map>` | Change map |
| `BKW` | `BKW<r>,<g>,<b>` | Map ambient color |
| `CA` | `CA` + raw bytes (x, y) | Clear area (erase entities outside range) |
| `PU` | `PU<x>,<y>` | Player position |

#### Chat & Floating Text
| Opcode | Format | Description |
|--------|--------|-------------|
| `T\|` | `T\|<color>°<text>°<charindex>` | Talk bubble (° = ASCII 176) |
| `N\|` | `N\|<color>°<text>°<charindex>` | Floating combat text (damage numbers, miss text) |
| `P\|` | `P\|<text>~<r>~<g>~<b>~<bold>~<italic>` | Console message (~ = ASCII 126) |
| `\|\|NNN` | `\|\|NNN` or `\|\|NNN@param1@param2` | Client-side text code (from Textos.ao) |
| `!!` | `!!<message>` | Modal message box (in-game GM broadcast) |
| `ERO` | `ERO<message>` | Modal error dialog (stays connected) |

#### Combat
| Opcode | Format | Description |
|--------|--------|-------------|
| `U2` | `U2<user_heading>,<npc_charindex>,<damage>` | User hits NPC |
| `N2` | `N2<heading>,<user_charindex>,<damage>` | NPC hits user |
| `FX` | `FX<charindex>,<grh>,<loops>` | Visual effect on character |
| `TW` | `TW<sound_id>` | Play sound |

#### Stats & UI
| Opcode | Format | Description |
|--------|--------|-------------|
| `[H]` | `[H]<max>,<min>` | HP update |
| `[M]` | `[M]<max>,<min>` | Mana update |
| `[S]` | `[S]<max>,<min>` | Stamina update |
| `[G]` | `[G]<gold>` | Gold update |
| `[E]` | `[E]<exp_next>,<exp_current>` | Experience update |
| `CSI` | `CSI<slot>,<obj>,<name>,<amt>,<equipped>,<grh>,<type>,<maxhit>,<minhit>,<maxdef>,<valor/3>` | Inventory slot |
| `NAVEG` | `NAVEG` | Toggle navigation mode (boat) |
| `PARADOK` | `PARADOK` | Toggle paralysis |
| `MUERT` | `MUERT` | Character died |

## Text Code System (|| Codes)

The client has a pre-defined text database (`Textos.ao`) with 983 entries. Instead of sending raw text, the server sends `||NNN` where NNN is the text code. The client renders the localized text with appropriate font styling.

Parameterized messages use `@` as separator: `||60@PlayerName@150` → "Has matado a PlayerName! Has ganado 150 puntos de experiencia."

This system reduces bandwidth and ensures consistent client-side text rendering.

## Area Visibility (9×9 Zone Grid)

Maps are divided into zones for efficient packet broadcasting:
- Zone size: ~9×9 tiles (configurable via `AREA_SIZE`)
- Each tile belongs to exactly one zone
- Players only receive packets from entities in adjacent zones (3×3 zone area)
- `CA` packet tells the client to erase entities outside the visible range

**SendTarget variants**:
- `ToArea { map, x, y }` — all players in adjacent zones
- `ToAreaButIndex { conn_id, map, x, y }` — area excluding one player
- `ToMap(map)` — all players on a map
- `ToAll` — all connected players
- `ToIndex(conn_id)` — specific player

## Privilege Levels

```
USER = 0, CONSEJERO = 1, SEMIDIOS = 2, EVENT_MASTER = 3,
DIOS = 4, GRAN_DIOS = 8, DIRECTOR = 9, DEVELOPER = 10,
SUB_ADMIN = 11, ADMIN = 12
```

GM commands are prefixed with `/` in the chat handler and require minimum privilege levels.

# VB6 Parity Reference

## Purpose

This document catalogs specific VB6 behaviors, formulas, and edge cases that the Rust server must replicate exactly. These are the "gotchas" discovered during migration — places where VB6 does something unintuitive or undocumented.

## Encryption & Protocol

### Key Rotation
- Counter range: 0–999,999 (wraps to 0)
- `Numero2Letra` uses Spanish words: "cero", "uno", "dos", ..., "novecientos noventa y nueve mil novecientos noventa y nueve"
- `Semilla` derives a numeric seed from the Spanish text
- Each inbound packet uses the NEXT counter value (counter increments AFTER decryption)

### Packet Framing
- Null byte (`0x00`) terminates every packet
- Client sends Latin-1 (Windows-1252), NOT UTF-8
- Server must convert outbound UTF-8 to Latin-1 for special characters (°, ñ, etc.)

### String Artifacts
- VB6 strings may have trailing null bytes or control characters after decryption
- Must `trim()` + `trim_end_matches(|c| c.is_control())` on every inbound packet

## Map System

### PK Flag Inversion
```
File: Pk=0 → MapInfo.Pk = True  (PvP enabled, map is PK)
File: Pk=1 → MapInfo.Pk = False (PvP disabled, safe map)
```
VB6 code: `MapInfo.Pk = Not (CBool(val))` — explicit inversion.

### Water Detection (`HayAgua`)
```vb
' VB6 General.bas
Function HayAgua(Map, X, Y) As Boolean
    If MapData(Map, X, Y).Graphic(1) >= 1505 And _
       MapData(Map, X, Y).Graphic(1) <= 1520 And _
       MapData(Map, X, Y).Graphic(2) = 0 Then
        HayAgua = True
    End If
End Function
```
**Note**: Uses `Graphic(1)` and `Graphic(2)` (1-indexed in VB6 = `graphic[0]` and `graphic[1]` in Rust 0-indexed).

### Movement with Boat (LegalPos)
```vb
' VB6 GameLogic.bas
Function LegalPos(Map, X, Y, Optional PuedeAgua = False) As Boolean
    If PuedeAgua Then
        ' On boat: only water tiles are legal
        tmpBlock = (Not Blocked) And (No User) And (No NPC) And HayAgua(Map, X, Y)
    Else
        ' On foot: water tiles are impassable
        tmpBlock = (Not Blocked) And (No User) And (No NPC) And (Not HayAgua(Map, X, Y))
    End If
End Function
```

### Special Maps
Maps 158, 159, 160 always allow water traversal (`PuedeAgua = True` regardless of boat).

## Combat Formulas

### Critical Hit
- Multiplier: **1.8×** (NOT 2×)
- VB6: `daño = daño * 1.8`

### Backstab (Apuñalar)
- Player damage: **1.5×**
- NPC damage: **2×**
- Auto-success for Assassin class when attacker faces same direction as target

### Weapon Penetration (Refuerzo)
```
effective_armor = armor_absorption - weapon.refuerzo
if effective_armor < 0 then effective_armor = 0
```

### Weapon Poison
- 60% chance to poison on hit when weapon has `Envenena=1`
- VB6: `If RandomNumber(1, 100) <= 60 Then`

### NPC Poison
- NPCs with `Veneno=True` poison on every hit (100% chance)

### Experience Distribution
- Proportional to damage dealt when multiple players attack same NPC
- Each attacker gets: `(their_damage / total_damage) * base_exp`

### Kill Deduplication
- `LastCrimMatado` / `LastCiudMatado` — stores last killed player name
- Same name cannot give reputation points twice in a row
- Prevents reputation farming by repeatedly killing the same person

## Resurrection

### Resurrect Potion / Reviver NPC
- HP set to **35** (not full HP)
- VB6: `MinHP = 35`

### Spell Resurrection
- HP set to 35 (same as potion)
- Target must be dead and on same map

## Potion System

### Remo Potion (Remove Paralysis)
- Costs **60 HP** to use
- **3-round cooldown** for non-Warrior/non-Hunter classes
- VB6: `If Not EsGuerrero And Not EsCazador Then interval = 3`

## Inventory & Equipment

### Scroll Duplicate Check
- Cannot learn a spell you already know
- VB6 checks all spell slots before adding

### Boat Equip Prevention
- Boats are ONLY used via `USA` opcode (double-click)
- NOT equippable via `EQUI` opcode (right-click / E key)
- VB6 equip handler only matches: Weapon, Armor, Shield, Helmet, Arrow, Instrument, Tool

### Boat Mount/Dismount (DoNavega)
Mount:
```vb
UserList(ui).Char.Head = 0           ' Hide head
UserList(ui).Char.Body = Barco.Ropaje ' Set boat body
UserList(ui).Char.WeaponAnim = 0     ' Clear weapon
UserList(ui).Char.ShieldAnim = 0     ' Clear shield
UserList(ui).Char.CascoAnim = 0      ' Clear helmet
UserList(ui).flags.Navegando = 1
```

Dismount:
```vb
UserList(ui).Char.Head = OrigChar.Head           ' Restore head
UserList(ui).Char.Body = equiparRopaje(ui)       ' Restore armor or naked
UserList(ui).Char.WeaponAnim = equipped weapon   ' Restore weapon
UserList(ui).Char.ShieldAnim = equipped shield   ' Restore shield
UserList(ui).Char.CascoAnim = equipped helmet    ' Restore helmet
UserList(ui).flags.Navegando = 0
```

Login with boat:
```vb
If flags.Navegando = 1 Then
    Char.Body = ObjData(BarcoObjIndex).Ropaje
    Char.Head = 0
    Char.WeaponAnim = 0
    Char.ShieldAnim = 0
    Char.CascoAnim = 0
End If
```

## NPC Field Mapping

**Important**: NPC runtime state uses `map`, `x`, `y` directly — NOT `pos_map`, `pos_x`, `pos_y` like players.

```rust
// Correct NPC position access
npc.map, npc.x, npc.y

// Correct Player position access
user.pos_map, user.pos_x, user.pos_y
```

## Level Bonuses

Class-specific bonuses applied at levels 53, 56, 60 from `ClassBonus.dat`.

## Safe Zones

- Trigger=1: General safe zone (no PvP, no stealing, no NPC aggro)
- Trigger=4: PvP prevention on specific tiles (e.g., arena entrance)
- Trigger=6: Combat zone (ZONAPELEA) — forced PvP, special rules

## Character Save State

Fields saved to charfile on disconnect:
```
head, body, heading, weapon, shield, helmet, map, x, y,
level, exp, HP, mana, stamina, hit range, hunger, thirst,
gold, bank_gold, attributes[5], skills[20],
dead, poisoned, paralyzed, criminal, hidden, navigating, privileges,
spells[35], inventory[25] (with equipped flag), bank[40],
equipment slots, reputation[7], guild_index,
criminales_matados, ciudadanos_matados, ejercito_real, ejercito_caos,
skill_pts_libres, puntos_donacion, puntos_torneo, ts_points
```

## Text Code System (|| Migration)

The VB6 server sends `||NNN` codes for most system messages. The client looks up the text in `Textos.tsao` and renders it with the appropriate font.

Parameterized messages: `||NNN@param1@param2` — the client substitutes `@ARG1`, `@ARG2` in the text template.

### Codes Used in Rust Server
Combat: 3, 50, 56, 60, 163, 170, 207
Items: 5, 107, 108, 115
Spells: 181, 182, 832
Navigation: 103, 106
Skills: 113, 813, 814, 816, 818, 819, 825, 826, 827, 828
Commerce: 10, 17, 185, 186, 191, 652, 660, 661, 663
Level: 67, 68
Status: 117, 119, 196, 205, 393, 394, 714, 829
Death: 3
GM: 773, 778
Events: 250, 252, 255, 256, 257, 258, 259, 263, 288
Bank: 223

### Messages Still Using P| Format
- Player whispers (`P|text~r~g~b~bold~italic`) — by design, whispers use P| for custom styling
- NPC dialog and custom server messages — no || equivalent exists
- Some TS-specific messages with no standard text code

## Floating Combat Text (N| Packet)

VB6 sends `N|` area-broadcast packets during combat to display damage/miss text above characters. These are rendered as floating numbers that rise and fade (identical animation to chat dialog bubbles).

### Format
```
N|<vbColor>°<text>°<charIndex>
```
- ° = ASCII 176 separator
- `vbColor`: VB6 color integer (e.g., 65535 = yellow for damage, 255 = red for miss)
- `text`: Display text (e.g., "-150" for damage, "¡Fallo!" for miss)
- `charIndex`: Target character to display text above

### Combat Scenarios Sending N|
| Scenario | Color | Text | Target |
|----------|-------|------|--------|
| PvP melee hit | 65535 (yellow) | `-{damage}` | Victim |
| PvP melee miss | 255 (red) | `¡Fallo!` | Victim |
| PvP ranged hit | 65535 | `-{damage}` | Victim |
| PvP ranged miss | 255 | `¡Fallo!` | Victim |
| User hits NPC | 65535 | `-{damage}` | NPC |
| User misses NPC | 255 | `¡Fallo!` | User |
| NPC hits user | 65535 | `-{damage}` | User |
| NPC misses user | 255 | `¡Fallo!` | User |
| Spell damage | 65535 | `-{damage}` | Target |

## Error/Message Dialog System (ERR/ERO/!!)

VB6 has three modal message packet types. The Godot client renders these as a centered modal dialog with "Aceptar" button (matching VB6's `frmMensaje` form).

| Packet | Behavior | Connection |
|--------|----------|------------|
| `ERR"message"` | Shows error dialog + sets login error | Disconnects after |
| `ERO"message"` | Shows message dialog | Stays connected |
| `!!"message"` | Shows GM broadcast dialog | Stays connected |

### Client Behavior
- Dialog blocks all input until "Aceptar" is pressed (or Enter key)
- ERR: Also sets `LoginError` for screen-specific handling (Login, CharSelect, CharCreate)
- ERO: Used for non-fatal validations (friends list errors, in-game warnings)
- !!: Used for GM broadcasts to all players

## FPS-Independent Dialog Animation

VB6 ran at ~60fps with per-frame animation increments. The Godot client uses delta-time scaling:

```csharp
float dtFactor = deltaMs / 16.667f; // 16.667ms = target 60fps frame time
ch.DialogRiseCounter -= dtFactor;     // Rise phase
ch.DialogAlpha += 12f * dtFactor;     // Fade in
ch.DialogAlpha -= 10f * dtFactor;     // Fade out
```

This ensures dialog bubbles and floating text animate at the same speed regardless of FPS (works correctly from 30fps to 1400+ fps).

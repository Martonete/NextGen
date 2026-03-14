# Tierras Sagradas AO — Faction System Research

## Overview

The faction system is a dual-alignment (Angel/Demon) progression system with 5 hierarchical ranks. Players enlist in either the Royal Army (Armada Real) or the Chaos Forces (Fuerzas Caos) and progress through ranks by killing the opposing faction's players.

---

## Core Data Structures

### tFacciones Type (Declares.bas, line 1550)

```vb
Public Type tFacciones
    ArmadaReal As Byte                  ' 0=not member, 1=member of Royal Army
    FuerzasCaos As Byte                 ' 0=not member, 1=member of Chaos Forces
    CriminalesMatados As Double         ' Kill count: criminals killed (Royal Army progress)
    CiudadanosMatados As Double         ' Kill count: citizens killed (Chaos progress)
    NeutralesMatados As Double          ' Kill count: neutral kills (secondary tracker)
    RecompensasReal As Long             ' Hierarchy level (0-5): Royal Army
    RecompensasCaos As Long             ' Hierarchy level (0-5): Chaos Forces
    RecibioExpInicialReal As Byte       ' Flag: received initial enlistment XP (Real)
    RecibioExpInicialCaos As Byte       ' Flag: received initial enlistment XP (Chaos)
    RecibioArmaduraReal As Byte         ' Flag: received armor reward (Real)
    RecibioArmaduraCaos As Byte         ' Flag: received armor reward (Chaos)
    Reenlistadas As Byte                ' Flag: can only enlist once (permanent)
End Type
```

### Hierarchy Flags (Declares.bas, line 1402-1406)

Players track hierarchy position with individual flag bits:
- `PJerarquia` — 1st Rank (Servidor del Rey / Servidor del Demonio)
- `SJerarquia` — 2nd Rank (Soldado Imperial / Mercenario de la Oscuridad)
- `TJerarquia` — 3rd Rank (Protector del Imperio / General de los Infiernos)
- `CJerarquia` — 4th Rank (Maestro de la Luz / Maestro de la Oscuridad)
- `CJerarquiaC` — 5th Rank (Caballero de la Luz / Caballero de la Oscuridad)

### Hierarchy Kill Thresholds (Facciones.ini)

```ini
[Jerarquias]
Primera=50      ; 1st rank: 50 kills
Segunda=100     ; 2nd rank: 100 kills
Tercera=200     ; 3rd rank: 200 kills
Cuarta=350      ; 4th rank: 350 kills
```

---

## Faction Alignment States

### StatusMith.EsStatus Codes

Kill counter tracking depends on player status (Modulo_UsUaRiOs.bas, line 2077-2105):

| Status | Meaning | Counted As |
|--------|---------|-----------|
| 1 | Citizen (Ciudadano) | Target for Chaos kills (CiudadanosMatados++) |
| 2 | Criminal (Criminal) | Target for Royal kills (CriminalesMatados++) |
| 3 | Royal Army (Armada Real) | Target for Chaos kills (CiudadanosMatados++) |
| 4 | Chaos Forces (Fuerzas Caos) | Target for Royal kills (CriminalesMatados++) |
| 5 | Alliance (Alianza) | Target for Chaos kills |
| 6 | Horde (Horda) | Target for Royal kills |

### Helper Functions (FileIO.bas)

```vb
Function Ciudadano(ByVal userindex As Integer) As Boolean
    Ciudadano = UserList(userindex).StatusMith.EsStatus = 1 Or _
                UserList(userindex).StatusMith.EsStatus = 3 Or _
                UserList(userindex).StatusMith.EsStatus = 5
End Function

Function Criminal(ByVal userindex As Integer) As Boolean
    Criminal = UserList(userindex).StatusMith.EsStatus = 2 Or _
               UserList(userindex).StatusMith.EsStatus = 4 Or _
               UserList(userindex).StatusMith.EsStatus = 6
End Function
```

---

## Enlistment System

### Sub EnlistarArmadaReal (ModFacciones.bas, line 41-89)

**Preconditions:**
- Player NOT already in Royal Army (ArmadaReal = 0)
- Player NOT in Chaos Forces (FuerzasCaos = 0)
- Player IS a Ciudadano (status 1, 3, or 5)
- Player HAS killed >= FragsJerarquia(1) (50) criminals
- Player NOT already re-enlisted (Reenlistadas = 0)

**On Success:**
1. Set `ArmadaReal = 1`, `Reenlistadas = 1`
2. Reset `RecompensasReal = 0`
3. Set `StatusMith.EsStatus = 3` (Royal Army)
4. Initialize hierarchy: `PJerarquia = 1`, others = 0
5. Send opcode `||177` (enlistment success)
6. **If first time:**
   - Award `ExpAlUnirse` (50,000 exp)
   - Send opcode `||170@50000`
   - Set `RecibioExpInicialReal = 1`

**Error Messages:**
- `||173` — Already in Royal Army
- `||174` — Already in Chaos Forces
- `||175` — Not a Ciudadano
- Custom NPC text — Need X more criminals killed

### Sub EnlistarCaos (ModFacciones.bas, line 388-442)

**Preconditions:**
- Player NOT already in Chaos Forces (FuerzasCaos = 0)
- Player NOT in Royal Army (ArmadaReal = 0)
- Player NEVER received Royal initial XP (RecibioExpInicialReal = 0)
- Player IS a Criminal (status 2, 4, or 6)
- Player HAS killed >= FragsJerarquia(1) (50) citizens
- Player NOT already re-enlisted (Reenlistadas = 0)

**On Success:**
1. Set `FuerzasCaos = 1`, `Reenlistadas = 1`
2. Set `RecompensasCaos = 0`
3. Set `StatusMith.EsStatus = 4` (Chaos Forces)
4. Initialize hierarchy: `PJerarquia = 1`, others = 0
5. Send NPC text: "Bienvenido a la horda infernal!!!, para recibir tu armadura escribe /recompensa!"
6. **If first time:**
   - Award `ExpAlUnirse` (50,000 exp)
   - Send opcode `||170@50000`
   - Set `RecibioExpInicialCaos = 1`

**Key Difference:** Chaos can only recruit players who were never in the Royal Army.

---

## Reward System

### Sub RecompensaArmadaReal (ModFacciones.bas, line 90-341)

Players can receive rewards at each hierarchy level. Progression requires meeting kill thresholds:

| Rank | RecompensasReal | Kill Requirement | Reward |
|------|-----------------|------------------|--------|
| 0→1 | 0→1 | ≥50 criminals | Armor + 5,000 exp |
| 1→2 | 1→2 | ≥100 criminals | Armor + 5,000 exp |
| 2→3 | 2→3 | ≥200 criminals | Armor + 5,000 exp |
| 3→4 | 3→4 | ≥350 criminals | Spell + 5,000 exp |
| 4→5 | 4→5 | Hold 5x items 1220-1224 (20 each) | Spell 54 + 5,000 exp |

**Armor Rewards by Race & Class:**

Hierarchy 1-3 armor is distributed by Raza + clase:

```
Enano/Gnomo + Mago/Druida/Bardo:     950, 952, 954
Enano/Gnomo + Other:                 956, 958, 960
Other + Mago/Druida/Bardo:           951, 953, 955
Other + Other:                       957, 959, 961
```

**Spell Rewards (Hierarchy 4):**
- Mago → Spell 60
- Druida/Bardo → Spell 63
- Asesino → Spell 64
- Paladin → Spell 61
- Clérigo → Spell 62

**5th Rank Quest:**
- Collect 20x each of items: 1220, 1221, 1222, 1223, 1224
- Upon collection: Award Spell 54 + remove items

### Sub RecompensaCaos (ModFacciones.bas, line 443-699)

Mirror system for Chaos Forces. Kill threshold for citizens (no "criminal" equivalent).

**Armor Rewards by Race & Class:**

```
Enano/Gnomo + Mago/Druida/Bardo:     981, 983, 985
Enano/Gnomo + Other:                 987, 989, 991
Other + Mago/Druida/Bardo:           980, 982, 984
Other + Other:                       986, 988, 990
```

**Spell Rewards (Hierarchy 4):**
- Mago → Spell 60
- Druida/Bardo → Spell 63
- Asesino → Spell 64
- Paladin → Spell 61
- Clérigo → Spell 62

**5th Rank Quest:** Identical to Royal Army (collect items 1220-1224)

**Broadcast Messages:**
- Rank 4 promotion (1st time only): `||851@<PlayerName>` (internal), `||852@<PlayerName>` (5th rank)

---

## Kill Tracking

### Sub ContarMuerte (Modulo_UsUaRiOs.bas, line 2077-2105)

Increments faction kill counters when a player kills another player.

**Exclusions (no counter increment):**
- Victim is a newbie (EsNewbie)
- Victim in PvP zone allowing it (TriggerZonaPelea = TRIGGER6_PERMITE)
- Attacker in special map
- Either player in maps 184 or 185

**Kill Counting Logic:**

```vb
If (Victim.Status = Criminal OR EsHorda) Then
    If Attacker.LastCrimMatado <> Victim.Name Then
        Attacker.LastCrimMatado = Victim.Name
        If Attacker.CriminalesMatados < 65000 Then
            Attacker.CriminalesMatados += 1
        End If
    End If
End If

If (Victim.Status = Ciudadano OR EsAlianza) Then
    If Attacker.LastCiudMatado <> Victim.Name Then
        Attacker.LastCiudMatado = Victim.Name
        If Attacker.CiudadanosMatados < 65000 Then
            Attacker.CiudadanosMatados += 1
        End If
    End If
End If

If Victim.Status = Neutral Then
    If Attacker.LastNeutrMatado <> Victim.Name Then
        Attacker.LastNeutrMatado = Victim.Name
        If Attacker.NeutralesMatados < 65000 Then
            Attacker.NeutralesMatados += 1
        End If
    End If
End If
```

**Prevention of Farming:** Uses `LastCrimMatado`, `LastCiudMatado`, `LastNeutrMatado` to track unique kills per victim per session.

---

## Chat Commands (TCP_HandleData2.bas)

### /ENLISTAR

Initiates faction enlistment. Uses target NPC to determine faction:

```vb
If Npclist(TargetNPC).flags.Faccion = 0 Then
    Call EnlistarArmadaReal(userindex)
Else
    Call EnlistarCaos(userindex)
End If
```

**Errors:**
- `||9` — No NPC targeted
- `||158` — Distance too far

### /INFORMACION

Requests faction status from NPC:

```vb
If Npclist(TargetNPC).flags.Faccion = 0 Then
    ' Royal Army NPC
    If UserList(userindex).Faccion.ArmadaReal = 0 Then
        "No perteneces a las tropas reales!!!"
    Else
        "Tu deber es combatir criminales, cada 100 criminales que derrotes te dare una recompensa."
    End If
Else
    ' Chaos NPC
    If UserList(userindex).Faccion.FuerzasCaos = 0 Then
        "No perteneces a la legión oscura!!!"
    Else
        "Tu deber es sembrar el caos y la desesperanza, cada 100 ciudadanos que derrotes te dare una recompensa."
    End If
End If
```

### /RECOMPENSA

Claims available rewards:

```vb
If Npclist(TargetNPC).flags.Faccion = 0 Then
    If UserList(userindex).Faccion.ArmadaReal = 0 Then
        "No perteneces a las tropas reales!!!"
    Else
        Call RecompensaArmadaReal(userindex)
    End If
Else
    If UserList(userindex).Faccion.FuerzasCaos = 0 Then
        "No perteneces a la legión oscura!!!"
    Else
        Call RecompensaCaos(userindex)
    End If
End If
```

---

## Client Opcodes

### Server → Client Packets

| Opcode | Format | Purpose |
|--------|--------|---------|
| `\|\|170@<EXP>` | int | Enlistment bonus XP |
| `\|\|173` | — | Already in Royal Army |
| `\|\|174` | — | Already in Chaos Forces |
| `\|\|175` | — | Not a Ciudadano |
| `\|\|176` | — | Already re-enlisted |
| `\|\|177` | — | Enlistment success (Royal) |
| `\|\|178@<Name>` | string | Announce rank 4 promotion (Royal) |
| `\|\|179` | — | Missing items for 5th rank |
| `\|\|180@<Name>` | string | Announce rank 5 promotion (Royal) |
| `\|\|181` | — | Spell slot full |
| `\|\|182` | — | Already have spell |
| `\|\|183` | — | Expulsion from Royal Army |
| `\|\|184` | — | Expulsion from Chaos Forces |
| `\|\|851@<Name>` | string | Announce rank 4 promotion (Chaos) |
| `\|\|852@<Name>` | string | Announce rank 5 promotion (Chaos) |
| `\|\|869@<C>@<Cr>@<N>` | long,long,long | Faction kill counts (Citizens, Criminals, Neutrals) |
| `N\|<Text>\|<Icon>\|<NPC>` | string | NPC dialogue |

---

## Character File Format (INI)

### [FACCIONES] Section (FileIO.bas)

```ini
[FACCIONES]
EjercitoReal=<0|1>              ; Byte: In Royal Army?
EjercitoCaos=<0|1>              ; Byte: In Chaos Forces?
CiudMatados=<0-65000>           ; Double: Citizens killed
CrimMatados=<0-65000>           ; Double: Criminals killed
NeutrMatados=<0-65000>          ; Double: Neutrals killed
rArCaos=<0|1>                   ; Byte: Received Chaos armor
rArReal=<0|1>                   ; Byte: Received Royal armor
rExCaos=<0|1>                   ; Byte: Received Chaos initial XP
rExReal=<0|1>                   ; Byte: Received Royal initial XP
recCaos=<0-5>                   ; Long: Chaos hierarchy level
recReal=<0-5>                   ; Long: Royal hierarchy level
Reenlistadas=<0|1>              ; Byte: Ever enlisted?
```

---

## Expulsion System

### Sub ExpulsarFaccionReal (ModFacciones.bas, line 343-354)

Removes player from Royal Army:
- Reset `ArmadaReal = 0`
- Clear all hierarchy flags (`PJerarquia`, `SJerarquia`, `TJerarquia`, `CJerarquia` = 0)
- Unequip Royal armor if equipped
- Send opcode `||183`

### Sub ExpulsarFaccionCaos (ModFacciones.bas, line 356-367)

Removes player from Chaos Forces:
- Reset `FuerzasCaos = 0`
- Clear all hierarchy flags
- Unequip Chaos armor if equipped
- Send opcode `||184`

**Trigger:** When player dies with `EsStatus = 3|4` (faction member), faction is reset (TCP_HandleData2.bas, line 972-976).

---

## Faction Titles

### TituloReal (ModFacciones.bas, line 369-386)

| RecompensasReal | Title |
|-----------------|-------|
| 0-1 | "Servidor del Rey" |
| 2 | "Soldado Imperial" |
| 3 | "Protector del Imperio" |
| 4 | "Maestro de la Luz" |
| 5 | "Caballero de la Luz" |

### TituloCaos (ModFacciones.bas, line 701-718)

| RecompensasCaos | Title |
|-----------------|-------|
| 0-1 | "Servidor del Demonio" |
| 2 | "Mercenario de la Oscuridad" |
| 3 | "General de los Infiernos" |
| 4 | "Maestro de la Oscuridad" |
| 5 | "Caballero de la Oscuridad" |

---

## GM Commands (TCP_HandleData3.bas)

### /CRIMMATADOS

Set criminal kill count (GM only):

```vb
UserList(tIndex).Faccion.CriminalesMatados = val(Arg2)
```

### /CIUDMATADOS

Set citizen kill count (GM only):

```vb
UserList(tIndex).Faccion.CiudadanosMatados = val(Arg2)
```

---

## Summary: Key Implementation Points

1. **Dual faction system**: Royal Army (kill criminals) vs. Chaos (kill citizens)
2. **Progressive ranks**: 5 levels based on kill thresholds (50, 100, 200, 350)
3. **Unique restrictions**:
   - Can't join Chaos if ever joined Royal
   - Must pre-qualify with kills before enlistment
   - One-time enlistment (Reenlistadas flag)
4. **Rewards**: Armor (levels 1-3), Spells (level 4), Spell + Item Quest (level 5)
5. **Kill tracking**: Per-victim anti-farming with `LastCrimMatado`, `LastCiudMatado`, `LastNeutrMatado`
6. **Data persistence**: All faction data in character .chr file under [FACCIONES] section
7. **Status dependency**: Faction activities gated by `StatusMith.EsStatus` codes

---

## File References

- **Main faction module**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/ModFacciones.bas`
- **Type definitions**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/Declares.bas` (lines 1550-1562)
- **File I/O**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/FileIO.bas` (lines 994-1005, 1715-1726, 2031-2042)
- **Kill tracking**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/Modulo_UsUaRiOs.bas` (lines 2077-2105)
- **Chat commands**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/TCP_HandleData2.bas` (lines 1820-1885)
- **Configuration**: `/workspace/Tierras-Sagradas-AO/Servidor/Facciones.ini`

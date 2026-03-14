# Tierras Sagradas AO — Quest System Research

## Overview
The quest system in VB6 has two components:
1. **Regular Quests** (QUESTS.dat): 31 fixed quests defined in config
2. **Daily Quests/Misiones Diarias** (MisionesDiarias.dat): 12 daily mission types with varied objectives

---

## 1. Quest Data File Format

### File: `Servidor/Dat/QUESTS.DAT`
**Format**: INI-style text file with binary wrapper

```ini
[INIT]
Num=31

[Quest1]
Name=Matar 10 usuarios
Tipo=2
Usuarios=10
Creditos=0
ptsTS=0
ptsTorneo=0
Oro=200000

[Quest6]
Name=Mata 30 Lobos!
Tipo=1
MataNPC=501
Cant=30
Creditos=0
ptsTS=0
ptsTorneo=0
Oro=90000
```

### Quest Data Structure (`modQuests.bas` - `tQuests` type):
```vb
Public Type tQuests
    Name As String           ' Quest display name
    Tipo As Byte             ' 1=Kill NPC, 2=Kill Players
    Usuarios As Integer      ' (Tipo=2) Number of players to kill
    ptsTorneo As Integer     ' Tournament points reward
    ptsTS As Integer         ' TS Points (seasonal) reward
    Creditos As Integer      ' Donation credits reward
    Oro As Long              ' Gold reward
    numNPC As Integer        ' (Tipo=1) NPC type ID to kill
    CantNPC As Integer       ' (Tipo=1) Number of NPCs to kill
End Type
```

### Quest Types:
- **Tipo = 1**: Kill X NPCs of a specific type (tracked: `MuereQuest` counter, target: `CantNPC`)
- **Tipo = 2**: Kill X players (tracked: `MuereQuest` counter, target: `Usuarios`)

### All 31 Quests in QUESTS.DAT:
1. Matar 10 usuarios (Tipo=2, Usuarios=10, Oro=200000)
2. Matar 25 usuarios (Tipo=2, Usuarios=25, Oro=500000)
3. Matar 50 usuarios (Tipo=2, Usuarios=50, Oro=1200000)
4. Matar 75 usuarios (Tipo=2, Usuarios=75, Oro=2000000)
5. Matar 100 usuarios (Tipo=2, Usuarios=100, Oro=5000000)
6. Mata 30 Lobos (Tipo=1, MataNPC=501, Cant=30, Oro=90000)
7. Mata 15 Ara (Tipo=1, MataNPC=521, Cant=15, Oro=100000)
8. Mata 15 Trolles Frenéticos (Tipo=1, MataNPC=946, Cant=15, Oro=250000)
9. Mata 20 Liches (Tipo=1, MataNPC=554, Cant=20, Oro=300000)
10. Mata 15 Beholders Helados (Tipo=1, MataNPC=537, Cant=15, Oro=200000)
11. Mata 5 Yetis (Tipo=1, MataNPC=943, Cant=5, Oro=300000)
12. Mata 4 Golems de Oro (Tipo=1, MataNPC=609, Cant=4, Oro=400000)
13. Mata 10 Apariciones (Tipo=1, MataNPC=941, Cant=10, Oro=300000)
14. Mata 5 Gran Dragones de Plata (Tipo=1, MataNPC=564, Cant=5, Creditos=1, Oro=700000)
15. Mata 4 Dragones Rojos (Tipo=1, MataNPC=542, Cant=4, Creditos=1, Oro=700000)
16. Mata 4 Bestias Infernales (Tipo=1, MataNPC=949, Cant=4, Creditos=1, Oro=400000)
17. Mata 5 Medusas (Tipo=1, MataNPC=553, Cant=5, Oro=200000)
18. Matar 7 Gran Dragones de la Oscuridad (Tipo=1, MataNPC=566, Cant=7, Creditos=1, Oro=3000000)
19. Mata 5 Abominaciones de las minas (Tipo=1, MataNPC=911, Cant=5, Creditos=1, Oro=600000)
20. Mata 10 Viudas Negras (Tipo=1, MataNPC=944, Cant=10, Oro=300000)
21. Mata 3 Golem (Tipo=1, MataNPC=638, Cant=3, Oro=600000)
22. Matar 3 Medusas Legendarias (Tipo=1, MataNPC=950, Cant=3, Oro=500000)
23. Asesina al dios Tarraske (Tipo=1, MataNPC=623, Cant=1, Creditos=3, Oro=500000)
24. Asesina al dios Mifrit (Tipo=1, MataNPC=624, Cant=1, Creditos=3, Oro=500000)
25. Asesina al dios Erebros (Tipo=1, MataNPC=625, Cant=1, Creditos=3, Oro=500000)
26. Asesina al dios Poseidon (Tipo=1, MataNPC=626, Cant=1, Creditos=3, Oro=500000)
27. Asesina 5 Golems de Tierra (Tipo=1, MataNPC=627, Cant=5, Creditos=1, Oro=600000)
28. Asesina 5 Almas en Pena (Tipo=1, MataNPC=628, Cant=5, Creditos=1, Oro=600000)
29. Asesina 10 Galeones Perdidos (Tipo=1, MataNPC=634, Cant=10, Oro=650000)
30. Asesina 5 Golems de Fuego (Tipo=1, MataNPC=622, Cant=5, Creditos=1, Oro=600000)
31. Asesina 3 Gran Leviatanes (Tipo=1, MataNPC=633, Cant=3, Oro=450000)

---

## 2. Daily Quests/Misiones Diarias

### File: `Servidor/Dat/MisionesDiarias.dat`
**Format**: INI-style text

```ini
[INIT]
NumMisiones=12

[MISION1]
Nombre=Matar 15 apariciones
Info=¡Deberas encontrar 15 apariciones y acabar con ellas!
Tipo=1
NumNPC=941
Cantidad=15

[MISION6]
Tipo=6
QuestNumber=9
Cantidad=2
```

### Daily Mission Types (12 total):
| Tipo | Objective | Variable | Notes |
|------|-----------|----------|-------|
| 1 | Kill X NPCs | NumNPC, Cantidad | - |
| 2 | Kill X players | Cantidad | - |
| 3 | Win X duels | Cantidad | - |
| 4 | Complete X challenge rounds | Cantidad | - |
| 5 | Complete X 2vs2 challenge rounds | Cantidad | - |
| 6 | Complete X quests | QuestNumber, Cantidad | Links to regular quest ID |
| 7 | Win X team matches | Cantidad | - |
| 8 | Win X events/tournaments | Cantidad | - |
| 9 | Spend X gold on NPCs today | Cantidad | Amount in gold |
| 10 | Get X tournament points today | Cantidad | Points needed |
| 11 | Redeem X tournament items | Puntos, Cantidad | Puntos=item cost, Cantidad=how many items |
| 12 | Win X Hunger Games | Cantidad | - |

---

## 3. Character File Quest Data Format

### File: `charfile/{CharName}.chr`
**Format**: INI text file, section [FLAGS]

```ini
[FLAGS]
Questeando=1              ' Boolean: actively on a quest
UserNumQuest=5            ' Byte: current quest ID (1-31, 0=none)
MuereQuest=12             ' Long: kill counter for current quest progress
QuestCompletadas=42       ' Integer: total quests completed lifetime
```

### Quest State Fields:
- **Questeando** (Byte):
  - 0 = No quest active
  - 1 = Quest active

- **UserNumQuest** (Byte):
  - 0 = No quest selected
  - 1-31 = Quest ID from QUESTS.dat

- **MuereQuest** (Long):
  - Counter for kills/progress toward quest completion
  - Reset to 0 when quest accepted
  - Incremented each kill matching quest type
  - Compared against CantNPC or Usuarios to detect completion

- **QuestCompletadas** (Integer):
  - Cumulative count of all quests ever completed
  - Incremented when quest is successfully finished

---

## 4. Quest Completion Logic

### Code Flow (modQuests.bas):

#### Trigger: NPC Kill (`RestarNPC()`)
```vb
Sub RestarNPC(ByVal userindex As Integer, ByVal KillNPC As Integer)
    NroQuest = UserList(userindex).flags.UserNumQuest

    If QuestsList(NroQuest).Tipo = 1 Then
        If KillNPC = QuestsList(NroQuest).numNPC Then
            UserList(userindex).flags.MuereQuest += 1
        End If

        If UserList(userindex).flags.MuereQuest >= QuestsList(NroQuest).CantNPC Then
            ' QUEST COMPLETE - Awards below
        End If
    End If
End Sub
```

#### Trigger: Player Kill (`RestarUser()`)
```vb
Sub RestarUser(ByVal userindex As Integer, ByVal VictimIndex As Integer)
    NroQuest = UserList(userindex).flags.UserNumQuest

    If QuestsList(NroQuest).Tipo = 2 Then
        If UserList(userindex).flags.Questeando = 1 And TriggerZonaPelea(...) <> TRIGGER6_PERMITE Then
            UserList(userindex).flags.MuereQuest += 1
        End If

        If UserList(userindex).flags.MuereQuest = QuestsList(NroQuest).Usuarios Then
            ' QUEST COMPLETE - Awards below
        End If
    End If
End Sub
```

#### On Completion:
```
1. Send ||66 to client (completion signal)
2. Award GOLD:
   - If non-premium/no estado: oro * 1.0
   - If premium or estado: oro * 2.0
   - Send ||63@{gold_formatted}
3. Award TOURNAMENT POINTS:
   - Send ||57@{pts}
   - Call AgregarPuntos(userindex)
4. Award TS POINTS:
   - Send ||900@{pts}
5. Award DONATION CREDITS:
   - Send ||930@{credits}
6. Award REPUTATION:
   - tmpReward = ptsTorneo value (same 2x logic as gold)
7. Call ResetQuest() to clear state
8. Increment QuestCompletadas counter
9. Call SendUserGLD() to update client display
```

---

## 5. Quest-Related Opcodes

### Server → Client (Downstream)

| Opcode | Format | Meaning |
|--------|--------|---------|
| `QTL` | `QTL{num},{name1},{name2},...` | Quest list (sent on IQUEST request) |
| `MQC` | `MQC{cantNPC},{muerequests},{name},{oro},{pts},{credits},{tspts}` | My quest current progress |
| `MQS` | `MQS{name},{oro},{pts},{credits},{tspts}` | My quest selected (after INFD accept) |
| `DAMEQUEST` | `DAMEQUEST` | Request quest list from NPC |
| `\|\|66` | - | Quest completed! |
| `\|\|63@` | `\|\|63@{gold}` | Gold reward received |
| `\|\|57@` | `\|\|57@{pts}` | Tournament points reward |
| `\|\|900@` | `\|\|900@{pts}` | TS Points reward |
| `\|\|930@` | `\|\|930@{credits}` | Donation credits reward |
| `\|\|279` | - | Already on a quest, can't accept another |
| `\|\|280` | - | Quest accepted successfully |
| `\|\|304` | - | No quest to abandon (error) |
| `\|\|305` | - | Quest abandoned successfully |

### Client → Server (Upstream)

| Opcode | Format | Meaning |
|--------|--------|---------|
| `IQUEST` | - | Get quest list and current progress |
| `INFD` | `INFD,{questID}` | Get quest details for selection |
| `ACQT` | `ACQT,{questID}` | Accept quest |
| `/QUEST` | - | Request quest info from NPC (deprecated/in quest) |
| `/NOQUEST` | - | Abandon current quest |

---

## 6. NPC Quest Interaction

### Request Flow:
1. Client sends `/QUEST` while talking to quest NPC
2. Server replies with `DAMEQUEST` (request quest list)
3. Client shows quest selection dialog
4. Client sends `INFD,{questID}` to get quest details
5. Server replies with `MQS{data}`
6. Client displays quest info and accept/cancel buttons
7. If accept: Client sends `ACQT,{questID}`
8. Server validates and sends `||280` (success) or error code

### Validation Rules:
- Cannot accept quest if already on a quest (`||279`)
- Cannot accept quest in PK zone (`||291`)
- On success: Set `Questeando=1`, `UserNumQuest={id}`, `MuereQuest=0`

---

## 7. Quest Persistence

### Load (FileIO.bas - CargarPersonaje):
```vb
UserList(userindex).flags.Questeando = CByte(UserFile.GetValue("FLAGS", "Questeando"))
UserList(userindex).flags.MuereQuest = CByte(UserFile.GetValue("FLAGS", "MuereQuest"))
UserList(userindex).flags.UserNumQuest = CByte(UserFile.GetValue("FLAGS", "UserNumQuest"))
UserList(userindex).flags.QuestCompletadas = CLng(UserFile.GetValue("FLAGS", "QuestCompletadas"))
```

### Save (FileIO.bas - SaveUserOpcional):
```vb
Call WriteVar(UserFile, "FLAGS", "Questeando", CStr(UserList(userindex).flags.Questeando))
Call WriteVar(UserFile, "FLAGS", "UserNumQuest", CStr(UserList(userindex).flags.UserNumQuest))
Call WriteVar(UserFile, "FLAGS", "MuereQuest", CStr(UserList(userindex).flags.MuereQuest))
Call WriteVar(UserFile, "FLAGS", "QuestCompletadas", CStr(UserList(userindex).flags.QuestCompletadas))
```

---

## 8. NPC Kill Trigger

### Code: SistemaCombate.bas or similar
```vb
' When NPC is killed:
If UserList(userindex).flags.UserNumQuest > 0 Then
    Call modQuests.RestarNPC(userindex, NPCTypeID)
End If
```

---

## 9. Player Kill Trigger

### Code: Modulo_UsUaRiOs.bas
```vb
' On player death in valid zone:
If UserList(AttackerIndex).flags.UserNumQuest <> 0 Then
    Call modQuests.RestarUser(AttackerIndex, VictimIndex)
End If
```

---

## 10. Implementation Priority for Rust

### Phase Priority:
1. **Load QUESTS.dat** (parse INI format)
2. **Character quest state persistence** (read/write .chr FLAGS)
3. **Opcode handlers**:
   - IQUEST (list + current progress)
   - INFD (get quest details)
   - ACQT (accept quest)
   - /QUEST + /NOQUEST
4. **Kill triggers**:
   - NPC kill → check quest match → increment counter
   - Player kill → check quest type 2 → increment counter
5. **Completion logic** (awards + state reset)
6. **Daily Quests** (optional, lower priority)

---

## 11. Test Cases

### Scenario 1: NPC Kill Quest
1. Player accepts Quest 6 (Mata 30 Lobos)
2. User state: Questeando=1, UserNumQuest=6, MuereQuest=0
3. Player kills wolf #1 (NPC 501): MuereQuest → 1
4. Player kills wolf #30: MuereQuest → 30
5. Server detects MuereQuest >= CantNPC (30)
6. Send ||66, gold award, reset state

### Scenario 2: Player Kill Quest
1. Player accepts Quest 1 (Matar 10 usuarios)
2. User state: Questeando=1, UserNumQuest=1, MuereQuest=0
3. Player kills 5 enemies: MuereQuest → 5
4. Player kills 10th enemy: MuereQuest → 10
5. Server detects MuereQuest >= Usuarios (10)
6. Send ||66, gold + tournament points award, reset state

### Scenario 3: Abandon Quest
1. Player on Quest 5, MuereQuest=5
2. Send /NOQUEST
3. Server validates Questeando=1
4. Call ResetQuest(): Questeando=0, UserNumQuest=0, MuereQuest=0
5. Send ||305 (success)

---

## File References
- **Main Logic**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/modQuests.bas`
- **Persistence**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/FileIO.bas`
- **Opcodes**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/TCP_HandleData1.bas` (IQUEST, INFD, ACQT)
- **Opcodes**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/TCP_HandleData2.bas` (/QUEST, /NOQUEST)
- **NPC/Player Kills**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/Modulo_UsUaRiOs.bas`
- **Data**: `/workspace/Tierras-Sagradas-AO/Servidor/Dat/QUESTS.DAT`
- **Data**: `/workspace/Tierras-Sagradas-AO/Servidor/Dat/MisionesDiarias.dat`

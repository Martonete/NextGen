# Missing VB6 Handlers Not Yet Implemented in Rust

This document contains ALL VB6 packet handlers from TCP_HandleData1-4.bas that are NOT yet implemented in the Rust server.
Organized by source file and category.

---

## FROM HandleData1 (TCP_HandleData1.bas)

### 1. `X` — Consultation Response (admin responds to user query)
**Lines 70-81**
```vb
Case "X"        ' >>> Sistema Consultas
    rData = Right$(rData, Len(rData) - 1)
    Dim Usuario As Integer
    Dim texto As String
    Usuario = NameIndex(ReadField(1, rData, Asc("*")))
    texto = ReadField(2, rData, Asc("*"))
    If Usuario <= 0 Then Exit Sub
    UserList(Usuario).flags.ConsultaEnviada = False
    UserList(Usuario).flags.NumeroConsulta = 0
    SendData SendTarget.toindex, Usuario, 0, "||190"
    Call SendData(SendTarget.toindex, Usuario, 0, "RESPUES" & texto & "*" & UserList(userindex).Name)
Exit Sub
```
**What it does**: Admin responds to a user's SOS/consultation. Clears the user's consultation flag and sends the response text.

### 2. `#` — Send SOS/Consultation (user submits help request)
**Lines 82-107**
```vb
Case "#"       ' >>> Sistema Consultas
    Debug.Print "Me llego SOS"
    rData = Right$(rData, Len(rData) - 1)
    Dim TipoConsulta As Byte
    Dim rDatax As String
    TipoConsulta = ReadField(1, rData, Asc("|"))
    rDatax = ReadField(2, rData, Asc("|"))

    If UserList(userindex).flags.Silenciado = 1 And UserList(userindex).Counters.timeSilenciado > 0 Then
            Call SendData(SendTarget.toindex, userindex, 0, "||945@" & UserList(userindex).Counters.timeSilenciado)
        Exit Sub
    End If

    If UserList(userindex).flags.ConsultaEnviada = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||192")
        Exit Sub
    End If

        Call SendData(SendTarget.ToAdmins, 0, 0, "||193")
        MensajesNumber = MensajesNumber + 1
        MensajesSOS(MensajesNumber).Tipo = "Consulta"
        MensajesSOS(MensajesNumber).Autor = UserList(userindex).Name
        MensajesSOS(MensajesNumber).Contenido = rDatax
        UserList(userindex).flags.ConsultaEnviada = True
        UserList(userindex).flags.NumeroConsulta = MensajesNumber
Exit Sub
```
**What it does**: User sends a help request (SOS) to admins. Checks if silenced, if already has pending consultation. Notifies all admins.

### 3. `ACTPT` — Send Points
**Lines 403-405**
```vb
Case "ACTPT"
    Call EnviarPuntos(userindex)
Exit Sub
```
**What it does**: Sends tournament/donation points to the user client.

### 4. `ACTUALIZAR` — Position Update Request
**Lines 454-456**
```vb
Case "ACTUALIZAR"
    Call SendData(SendTarget.toindex, userindex, 0, "PU" & UserList(userindex).Pos.X & "," & UserList(userindex).Pos.Y)
    Exit Sub
```
**What it does**: Client requests position re-sync. Same as RPU but triggered by a different opcode.

### 5. `TOINFO` — Tournament Info
**Lines 457-460**
```vb
Case "TOINFO"
    tStr = SendTorneoList(userindex)
    Call SendData(SendTarget.toindex, userindex, 0, "LTR" & SendTorneoList(userindex))
Exit Sub
```
**What it does**: Sends list of active tournament participants.

### 6. `IDUELOS` — Duel Arena Info
**Lines 471-473**
```vb
Case "IDUELOS"
    Call SendData(SendTarget.toindex, userindex, 0, "MAR" & NombreDueleando(1) & "," & NombreDueleando(2) & "," & NombreDueleando(3) & "," & NombreDueleando(4) & "," & NombreDueleando(5) & "," & NombreDueleando(6) & "," & NombreDueleando(7) & "," & NombreDueleando(8))
Exit Sub
```
**What it does**: Sends names of players currently dueling in all 4 arenas.

### 7. `TENGOMACROS` — Macro Detection
**Lines 475-484**
```vb
Case "TENGOMACROS"
    With UserList(userindex)
        .flags.tieneMacro = .flags.tieneMacro + 1

        If (.flags.tieneMacro = 2) Then
            Call SendData(SendTarget.ToAdmins, 0, 0, "N|Seguridad>> se detecto el uso de macros en el usuario: " & UserList(userindex).Name & ", hay que revisarlo. ~255~255~0")
            .flags.tieneMacro = 0
        End If
    End With
Exit Sub
```
**What it does**: Client reports macro detection. After 2 reports, notifies admins.

### 8. `CCANJE` — Tournament Prize Exchange Menu
**Lines 486-496**
```vb
Case "CCANJE"
    Dim Premios As Integer, SX As String
        SX = "PRM" & UBound(PremiosList) & ","

        For Premios = 1 To UBound(PremiosList)
            SX = SX & PremiosList(Premios).ObjName & ","
        Next Premios

        Call SendData(SendTarget.toindex, userindex, 0, SX & UserList(userindex).Stats.PuntosTorneo & "," & UserList(userindex).Stats.TSPoints)
        Call SendData(SendTarget.toindex, userindex, 0, "INF" & PremiosList(val(rData)).ObjRequiere & "," & PremiosList(val(rData)).ObjMaxAt & "," & PremiosList(val(rData)).ObjMinAt & "," & PremiosList(val(rData)).ObjMaxdef & "," & PremiosList(val(rData)).ObjMindef & "," & PremiosList(val(rData)).ObjMaxAtMag & "," & PremiosList(val(rData)).ObjMinAtMag & "," & PremiosList(val(rData)).ObjMaxDefMag & "," & PremiosList(val(rData)).ObjMinDefMag & "," & PremiosList(val(rData)).ObjDescripcion)
    Exit Sub
```
**What it does**: Opens the tournament prize exchange UI. Sends prize list and player's points.

### 9. `GLINFO` — Guild Info Panel
**Lines 498-524**
```vb
Case "GLINFO"
    Call LoadGuildsClanes
    Dim GI As Integer
        GI = UserList(userindex).GuildIndex

        If GI <= 0 Then
            Call SendData(SendTarget.toindex, userindex, 0, "GL" & SendGuildsList(userindex))
        Exit Sub
        End If

        If UserList(userindex).GuildIndex >= 1 Then
          UserInfo = SendGuildUserInfo(userindex)
          If UserInfo <> vbNullString Then
            Call SendData(SendTarget.toindex, userindex, 0, "IREDAEK" & UserInfo)
          Exit Sub
          End If
        End If

        tStr = SendGuildLeaderInfo(userindex)
        If tStr = vbNullString And UserInfo = vbNullString Then
            Call SendData(SendTarget.toindex, userindex, 0, "GL" & SendGuildsList(userindex))
        Else
            If m_EsGuildLeader(UserList(userindex).Name, GI) Or m_EsGuildSubLeader1(UserList(userindex).Name, GI) Or m_EsGuildSubLeader2(UserList(userindex).Name, GI) Then
                Call SendData(SendTarget.toindex, userindex, 0, "IREDAEL" & tStr)
            End If
        End If
       Exit Sub
```
**What it does**: Opens guild info panel. Shows guild list if not in guild, member info if member, leader panel if leader/sub-leader.

### 10. `FEST` — Mini Statistics
**Lines 525-527**
```vb
Case "FEST"
    Call EnviarMiniEstadisticas(userindex)
    Exit Sub
```
**What it does**: Sends mini-stats to client.

### 11. `FINCBN` — Close Guild Bank
**Lines 545-550**
```vb
Case "FINCBN"
    UserList(userindex).flags.Comerciando = False
    UserList(userindex).flags.CuentaBancaria = ""
    Call SendData(SendTarget.toindex, userindex, 0, "FINCBNOK")
    Exit Sub
```
**What it does**: User closes guild bank interface.

### 12. `DCANJE` — Donation Exchange Menu
**Lines 560-567**
```vb
Case "DCANJE"
    tStr = UserList(userindex).Stats.PuntosDonacion & "," & UBound(DonationList) & ","
    For i = 1 To UBound(DonationList)
        tStr = tStr & DonationList(i).ObjName & ","
    Next
    Call SendData(SendTarget.toindex, userindex, 0, "DRM" & tStr)
Exit Sub
```
**What it does**: Opens donation prize exchange. Sends donation points and item list.

### 13. `CONSUL` — View SOS Messages (Admin)
**Lines 569-581**
```vb
Case "CONSUL"
    rData = Right$(rData, Len(rData) - 6)
    Dim dataSOS As String
    dataSOS = MensajesNumber & "|"
    For loopC = 1 To MensajesNumber
        dataSOS = dataSOS & MensajesSOS(loopC).Tipo & "-" & MensajesSOS(loopC).Autor & "-" & MensajesSOS(loopC).Contenido & "|"
    Next loopC
    Call SendData(SendTarget.toindex, userindex, 0, "ZSOS" & dataSOS)
Exit Sub
```
**What it does**: Admin views all pending SOS/consultation messages.

### 14. `CABEZI` — Head Change (barber)
**Lines 583-676** (94 lines)
**What it does**: User changes head/hairstyle for 500 gold. Validates race/gender head range.

### 15. `DAMINF` — Statistics Form Info
**Lines 678-759** (82 lines)
**What it does**: Sends detailed player statistics (duels, events, kills, etc.) for a target player's info form.

### 16. `ENVFPZ` — Send FPZ Report
**Lines 761-765**
```vb
Case "ENVFPZ"
    rData = Right$(rData, Len(rData) - 6)
    Call SendData(SendTarget.ToAdmins, 0, 0, "||218@" & UserList(userindex).Name & "@" & rData)
Exit Sub
```
**What it does**: User sends an FPZ (anti-hack) report to admins.

### 17. `FTSPTS` — TS Points Exchange
**Lines 767-828** (62 lines)
**What it does**: Exchange TS (Tierras Sagradas) points for specific items. 12 different item options with varying costs.

### 18. `ADDPTS` — Add Points to Guild
**Lines 830-869** (40 lines)
**What it does**: Donate tournament points to guild. Checks guild level, calculates level-ups.

### 19. `DYDTRA` — Drag & Drop Transfer
**Lines 941-997** (57 lines)
**What it does**: Transfer items to another player via drag & drop. Validates position, ownership, transferability.

### 20. `INCHAT` — Init Chat with Friend
**Lines 999-1011**
```vb
Case "INCHAT"
    rData = Right$(rData, Len(rData) - 6)
    Dim Contactito As String
    Contactito = UserList(userindex).flags.NombreAmigo(rData)
    If UCase$(Contactito) = "(NADIE)" Then
        Call SendData(SendTarget.toindex, userindex, 0, "||226")
        Exit Sub
    End If
    Call SendData(SendTarget.toindex, userindex, 0, "ENCHAT" & Contactito)
Exit Sub
```
**What it does**: Opens private chat with a friend from friends list.

### 21. `KKCHAT` — Send Chat Message to Friend
**Lines 1013-1050** (38 lines)
**What it does**: Sends a message through the friends chat system. Validates friendship, logs if admin monitoring.

### 22. `OFDIOZ` — Offer Souls to God
**Lines 1064-1199** (136 lines)
**What it does**: Complex god worship system. Sacrifice souls, receive divine items based on hierarchy level (1-4). Different gods (Mifrit, Poseidon, Tarraske, Erebros) give different rewards.

### 23. `DESPHE` — Move Spell Position
**Lines 1201-1205**
```vb
Case "DESPHE"
    rData = Right(rData, Len(rData) - 6)
    Call DesplazarHechizo(userindex, CInt(ReadField(1, rData, 44)), CInt(ReadField(2, rData, 44)))
Exit Sub
```
**What it does**: Swap/move spell positions in the spell list.

### 24. `TR` — Throw Item by Mouse Click
**Lines 1233-1253** (21 lines)
**What it does**: Drop item at current position via mouse drag. Different from TI (keyboard drop).

### 25. `SPn` (SP + special char) — Upgrade Item with Octarine
**Lines 1345-1374** (30 lines)
**What it does**: Upgrade an item using an Octarine Gem. Reads upgrade data from Mejorados.dat.

### 26. `SPH` — Item Upgrade Info
**Lines 1377-1399** (23 lines)
**What it does**: Shows info about a potential item upgrade (stats, description, graphic).

### 27. `ARE` — Arena Spectator
**Lines 1400-1534** (135 lines)
**What it does**: Enter a duel arena as spectator. Costs 100,000 gold. 4 arenas, max 4 spectators each.

### 28. `CUC` — Buy House
**Lines 1628-1669** (42 lines)
**What it does**: Purchase a house. Checks owner, gold, gives key item.

### 29. `FWO` — House Owner Info
**Lines 1671-1676**
```vb
Case "FWO"
    rData = Right$(rData, Len(rData) - 3)
    NumCasax = ReadField(1, rData, 44)
    Call SendData(SendTarget.toindex, userindex, 0, "GVN" & GetVar(DatPath & "Casas.dat", "Casa" & NumCasax, "Dueno") & "," & GetVar(DatPath & "Casas.dat", "Casa" & NumCasax, "Precio") & "," & GetVar(DatPath & "Casas.dat", "Casa" & NumCasax, "Fecha"))
Exit Sub
```
**What it does**: Query house owner, price and purchase date.

### 30. `CNM` — Change Pet Name
**Lines 1678-1684**
```vb
Case "CNM"
    rData = Right$(rData, Len(rData) - 3)
    Dim NickM As String
    NickM = ReadField(1, rData, 44)
    Call CambiarNickMascota(userindex, NickM)
Exit Sub
```
**What it does**: Rename a pet/mount.

### 31. `IPX` — Prize Info
**Lines 1686-1691** (6 lines)
**What it does**: Shows detailed info about a tournament prize item.

### 32. `SPX` — Buy Tournament Prize
**Lines 1693-1729** (37 lines)
**What it does**: Purchase items with tournament points. Validates points, adds to inventory.

### 33. `BOF` — Level Bonuses
**Lines 1731-1770** (40 lines)
**What it does**: Apply level bonuses at levels 53, 56, 60. HP increases based on selection.

### 34. `NVOT` — Vote in Poll
**Lines 2362-2392** (31 lines)
**What it does**: Cast vote in active poll (5 options). One vote per player.

### 35. `NEWD` — New Report/Denuncia
**Lines 2393-2413** (21 lines)
**What it does**: Submit a report against another player. Logs timestamps, notifies admins.

### 36. `CCBG` — Save Guild Bank
**Lines 2415-2424** (10 lines)
**What it does**: Persist guild bank inventory to file.

### 37. `GEMS` — Gem Exchange (/gemas)
**Lines 2476-2574** (99 lines)
**What it does**: Exchange 7 gems for rewards: tournament points, octarine gem, 30k souls, god renunciation, or fragment.

### 38. `GEPS` — Medal Prize Exchange
**Lines 2576-2681** (106 lines)
**What it does**: Exchange medals (item 1025) for various prizes: items, souls, tournament points, random gem.

### 39. `SWAP` — Swap Inventory Items
**Lines 2713-2724**
```vb
Case "SWAP"
    rData = Right$(rData, Len(rData) - 4)
    ObjSlot1 = ReadField(1, rData, 44)
    ObjSlot2 = ReadField(2, rData, 44)
    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")
        Exit Sub
    End If
    SwapObjects (userindex)
Exit Sub
```
**What it does**: Swap two inventory items between slots.

### 40. `PCGF` — PC Game Forward
**Lines 2725-2732**
**What it does**: Forward PC game data (process name, weight) to target user.

### 41. `PCWC` — PC Window Check
**Lines 2733-2739**
**What it does**: Forward window process info to target user.

### 42. `PCCC` — PC Caption Check
**Lines 2740-2746**
**What it does**: Forward window caption to target user.

### 43. `INFS` — Spell Info
**Lines 2747-2764** (18 lines)
**What it does**: Display detailed spell info (name, description, skill req, mana cost, stamina cost).

### 44. `SKSE` — Modify Skills
**Lines 2785-2816** (32 lines)
**What it does**: Distribute skill points. Anti-cheat: validates sum doesn't exceed available points, checks for negatives.

### 45. `ENTR` — Train Creature
**Lines 2817-2838** (22 lines)
**What it does**: Spawn a creature from an NPC trainer. Checks trainer NPC type, max pets.

### 46. `RETB` — Guild Bank Withdraw Item
**Lines 2883-2898** (16 lines)
**What it does**: Withdraw item from guild bank. Checks permissions.

### 47. `DEPB` — Guild Bank Deposit Item
**Lines 2944-2954** (11 lines)
**What it does**: Deposit item into guild bank.

---

## FROM HandleData2 (TCP_HandleData2.bas)

### 48. `/INFOSUB` — Auction Info
**Lines 262-277**
**What it does**: Shows current auction status: item, amount, highest bidder and bid.

### 49. `/SUBASTAR` — Start Auction
**Lines 279-286**
**What it does**: Opens the auction initialization form.

### 50. `/MSJ` — Toggle Private Messages
**Lines 298-306**
**What it does**: Toggle receiving private messages on/off.

### 51. `/CIUDADANIA` — Set Citizenship
**Lines 308-340**
**What it does**: Register citizenship at NPC based on map location (Inthak, Thir, Ruvendel).

### 52. `/MONTAR` — Mount Pet
**Lines 342-395** (54 lines)
**What it does**: Mount a pet dragon/horse. Changes body, removes visible equipment.

### 53. `/DESMONTAR` — Dismount
**Lines 397-403**
**What it does**: Dismount from current mount.

### 54. `/QUITARMASCOTA` — Remove Pet
**Lines 405-416**
**What it does**: Despawn owned pet/mount NPC.

### 55. `/BOTIX` — Spawn AI Bot (Mage)
**Lines 422-424**
**What it does**: Spawn an AI mage bot.

### 56. `/BOTIX2` — Spawn AI Bot (Cleric)
**Lines 426-428**
**What it does**: Spawn an AI cleric bot.

### 57. `/GUERRA` — Join War
**Lines 430-454** (25 lines)
**What it does**: Join an active war event. Teleports to war zone based on faction.

### 58. `/CIRUJIA` — Surgery (Race Change)
**Lines 456-472**
**What it does**: Open surgery interface at cirujano NPC to change race appearance.

### 59. `/VOTAR` — Vote in Poll
**Lines 494-505**
**What it does**: Open poll voting interface.

### 60. `/RESULTADOS` — Poll Results
**Lines 507-538**
**What it does**: Show poll results with vote counts and percentages.

### 61. `/ENTRENAR` — Train Creatures
**Lines 979-996**
**What it does**: Open creature training list from trainer NPC.

### 62. `/NOBLE` — Become Noble
**Lines 998-1052** (55 lines)
**What it does**: Become noble by presenting 5 specific items. Grants spell 46 and noble status.

### 63. `/DESENTERRAR` — Dig Treasure
**Lines 1054-1072**
**What it does**: Dig up treasure at specific map coordinates. Requires treasure key.

### 64. `/SICV` — Start CVC (Clan vs Clan)
**Lines 1087-1227** (141 lines)
**What it does**: Initiate a clan vs clan battle. Complex: validates participants, charges gold, warps players.

### 65. `/CVC ` — Challenge Clan
**Lines 1887-1970** (84 lines)
**What it does**: Send CVC challenge to another clan's leader.

### 66. `/PMSG` — Party Message
**Lines 1972-1978**
```vb
If UCase$(Left$(rData, 6)) = "/PMSG " Then
  rData = Right$(rData, Len(rData) - 6)
      If rData <> " " And rData <> "" Then
        Call SendData(SendTarget.ToPartyArea, userindex, 0, "||415@" & UserList(userindex).Name & "@" & rData)
      End If
    Exit Sub
End If
```
**What it does**: Send message to party members.

### 67. `/CENTINELA` — Anti-AFK Check
**Lines 1980-1987**
**What it does**: Respond to centinela anti-AFK challenge with numeric code.

### 68. `/IR` — Premium Travel
**Lines 1989-2000+** (long handler)
**What it does**: Premium player teleport to castle locations. Checks premium status, charges gold.

### 69. `/HORA` — Server Time
**Not fully visible but exists in HandleData2**
**What it does**: Shows current server time.

### 70. `/GMSG` — GM Message
**Exists in HandleData2**
**What it does**: GM sends a broadcast message.

### 71. `/VOTO` — Guild Vote
**Exists in HandleData2**
**What it does**: Vote in guild election.

### 72. `/PAREJA` — 2v2 Pair
**Exists in HandleData2**
**What it does**: Form a 2v2 team for pair battles.

### 73. `/NOADV` — Clear Warnings (GM)
**Exists in HandleData2**
**What it does**: GM clears a player's warning count.

### 74. `/LIBERAR` — Free from Jail (GM)
**Exists in HandleData2**
**What it does**: GM releases player from jail.

### 75. `/PENAS` — View Penalties (GM)
**Exists in HandleData2**
**What it does**: GM views a player's penalty history.

---

## FROM HandleData3 (TCP_HandleData3.bas)

### 76. `/VIAJAR` — Travel to City
**Lines 760-847** (88 lines)
**What it does**: Travel to a named city via NPC (Tanaris, Anvilmar, Kahlimdor, Thir, Inthak, Jhumbel, Ruvendel, Helka). Level/gold-based cost.

### 77. `/DUELO` — Duel Challenge
**Lines 849-939** (91 lines)
**What it does**: Challenge another player to a gold-wagered duel. Complex validation (gold, maps, special items).

### 78. `/SIDUELO` — Accept Duel
**Lines 942-1079** (138 lines)
**What it does**: Accept a duel challenge. Warps both players to arena, sets up arena state.

### 79. `/GLOBAL` — Global Chat
**Lines 1081-1134** (54 lines)
**What it does**: Send message to all players. Costs 50k gold for non-GMs. Level 50+ required.

### 80. `/CMSG` — Clan Message
**Lines 1137-1170** (34 lines)
**What it does**: Send message to clan members. Leader messages shown differently.

### 81. `/FMSG` — Faction Message
**Lines 1173-1193** (21 lines)
**What it does**: Send message to faction members (citizens or criminals council).

### 82. `/CASAR` — Marriage System
**Lines 1195-1258** (64 lines)
**What it does**: Propose/accept marriage. Creates permanent pair bond saved to charfile.

### 83. `/DIVORCIARSE` — Divorce
**Lines 1262-1296** (35 lines)
**What it does**: Divorce from partner.

### 84. Admin Commands (HandleData3, lines 1298+):
- `/DONACION` — Give donation points
- `/CONSEJERO` — Set Consejero rank
- `/CHANGENICK` — Change nickname
- `/SEMIDIOS` — Set Semidios rank
- `/DIOS` — Set Dios rank
- `/GDIOS` — Set Gran Dios rank
- `/EVENT` — Set Event Master rank
- `/ADMIN` — Set Admin rank
- `/DIRECTOR` — Set Director rank
- `/SUBADMINISTRADOR` — Set Sub Admin rank
- `/DEVELOPER` — Set Developer rank
- `/PJ` — Reset to Player rank
- `/HECHIZO` — Give spell to player
- `/SMSG` — System broadcast message
- `/ACC` — Spawn NPC
- `/RACC` — Spawn NPC with respawn
- `/NAVE` — Toggle navigation mode
- `/HABILITAR` — Toggle server GM-only mode
- `/BORRARPJ` — Delete character
- `/BANHD` — Hardware ban
- `/MOD` — Modify self stats (PART, AURA, FX, ATRI, ORO, EXP, BODY, HEAD, CRI, CIU, LEVEL, CLASE, HAM, AGU, STA, MP, HP, ESCU, CASCO, ARMA)
- `/SMOD` — Modify other player stats
- `/RESETVALS` — Reset arena/duel/CVC state
- `/COL` — Colored broadcast message
- `/DESAFIO` with name argument (3-way)
- `/HACLIDER` — Make clan leader
- `/SUBLIDER` — Make sub-leader
- `/QSUBLIDR` — Remove sub-leader
- `/DARORO` — Give gold
- `/INISUB` — Init auction
- `/OFRECER` — Bid on auction
- `/ITEMNOBLE` — Noble item craft

---

## FROM HandleData4 (TCP_HandleData4.bas)

### 85. `DPX` — Donation Item Preview
**Lines 40-115** (76 lines)
**What it does**: Preview a donation reward item. Shows body, weapon, shield, helmet, aura animations. Builds description string with all items in the donation pack.

### 86. `DRX` — Donation Redeem
**Lines 117-247** (131 lines)
**What it does**: Redeem a donation pack. Complex handler supporting:
- Regular items (index < 9995)
- Skins (index 9995)
- Premium subscription (index 9996)
- Red Dragon mount spell (index 9998)
- Gold Dragon mount spell (index 9997)
- Tournament points (index 9999)

### 87. `DOWNSI` — Cast Spell by Target Name
**Lines 253-270**
```vb
Case "DOWNSI"
    rData = Right$(rData, Len(rData) - 6)
    tIndex = NameIndex(rData)
    If tIndex > 0 Then
        If UserList(userindex).flags.Hechizo = 0 Then Exit Sub
        If (Mod_AntiCheat.PuedoCasteoHechizo(userindex) = False) Then Exit Sub
        UserList(userindex).flags.TargetUser = tIndex
        Call LanzarHechizo(UserList(userindex).flags.Hechizo, userindex)
        SendUserData (userindex)
        SendUserData (tIndex)
    Else
        Exit Sub
    End If
Exit Sub
```
**What it does**: Cast currently selected spell on a player by name (not by click target).

### 88. `RANKIN` — Rankings
**Lines 272-300**
```vb
Case "RANKIN"
    rData = Right$(rData, Len(rData) - 6)
    Arg1 = ReadField(1, rData, 44)

    Dim Rank As Integer

    If UCase(Arg1) = "DUELOS" Then Rank = eRanking.TOPDuelos
    If UCase(Arg1) = "PAREJAS" Then Rank = eRanking.TOPParejas
    If UCase(Arg1) = "RONDAS" Then Rank = eRanking.TOPRondas
    If UCase(Arg1) = "REPUTACION" Then Rank = eRanking.TOPReputacion
    If UCase(Arg1) = "TORNEOS" Then Rank = eRanking.TOPTorneos
    If UCase(Arg1) = "CVCS" Then Rank = eRanking.TOPCVCS
    If UCase(Arg1) = "CASTILLOS" Then Rank = eRanking.TOPCastillos
    If UCase(Arg1) = "REPUCLANES" Then Rank = eRanking.TOPRepuClanes
    If UCase(Arg1) = "FRAGS" Then Rank = eRanking.TOPFrags

    tStr = ""
        For i = 1 To 10
            If (Ranking(Rank).Nombre(i) <> "") Then
                tStr = tStr & Ranking(Rank).Nombre(i) & "-" & Ranking(Rank).Value(i) & ","
            Else
                tStr = tStr & "N/A-0,"
            End If
        Next i

            Call SendData(SendTarget.toindex, userindex, 0, "MTOP" & tStr)
Exit Sub
```
**What it does**: Display top-10 rankings for various categories (duels, pairs, rounds, reputation, tournaments, CVCs, castles, clan reputation, frags).

---

## SUMMARY: Priority Handlers for Implementation

**High Priority (core gameplay):**
1. SWAP - Inventory swap
2. SKSE - Skill point distribution
3. INFS - Spell info
4. DESPHE - Move spell position
5. DAMINF - Player stats form
6. FEST - Mini statistics
7. CABEZI - Head change
8. TR - Mouse drop item
9. GLINFO - Guild info panel
10. BOF - Level bonuses
11. ENTR - Train creatures
12. ACTPT - Send points
13. RANKIN - Rankings

**Medium Priority (systems):**
14. DCANJE/DPX/DRX - Donation system
15. CCANJE/IPX/SPX - Tournament prizes
16. GEMS/GEPS - Gem/Medal exchange
17. FTSPTS - TS points exchange
18. OFDIOZ - God worship
19. DYDTRA - Drag & drop transfer
20. NVOT - Voting
21. NEWD - Reports

**Lower Priority (events/PvP):**
22. /DUELO, /SIDUELO - Duel system
23. /SICV, /CVC - Clan vs Clan
24. /GLOBAL - Global chat
25. /VIAJAR - City travel
26. /MONTAR, /DESMONTAR - Mounts
27. /NOBLE - Noble status
28. ARE - Arena spectator
29. /GUERRA - War events

**Admin/GM commands:**
30. All /MOD, /SMOD, /RESETVALS commands
31. X, #, CONSUL - SOS system
32. TENGOMACROS - Macro detection

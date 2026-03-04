# VB6 Banking System - Complete Extraction

## Overview
The VB6 server implements TWO distinct banking systems:
1. **Personal Bank** - Per-account item storage (40 slots)
2. **Guild Bank** - Per-guild item and gold storage (40 slots per type)

Both systems store gold and items. Items are stackable.

---

## Part 1: PERSONAL BANK (Account-based)

### Storage Location
- **Items**: Accounts/*.act file, section `[BancoInventory]`
- **Gold**: Accounts/*.act file, section `[ACCOUNTNAME]`, key `BANCO`

### File Format (Accounts/PlayerName.act)
```ini
[BancoInventory]
CantidadItems=5
Obj1=101-50
Obj2=102-1
Obj3=0-0
Obj4=0-0
...
Obj40=0-0

[PlayerAccountName]
BANCO=50000
```

**Format**: `ObjIndex-Amount` where:
- `ObjIndex`: Object ID (0 = empty slot)
- `Amount`: Stack quantity (1-999 for most items)

### Constants
```vb
MAX_BANCOINVENTORY_SLOTS = 40
```

### Data Structure (Runtime)
```vb
Type BancoInventario
    Object(1 To 40) As UserOBJ
    NroItems As Integer
End Type

Type UserOBJ
    ObjIndex As Integer    ' Item ID
    Amount As Integer      ' Stack size
    Equipped As Byte       ' (not used in bank)
End Type

Type User
    BancoInvent As BancoInventario
    Stats As UserStats
    ...
End Type

Type UserStats
    Banco As Long          ' Gold in bank
    GLD As Long            ' Gold in inventory
    ...
End Type
```

### Loading (ConnectUser & LoadChar)
**File**: `/Servidor/Codigo/TCP.bas` (line 731) & `FileIO.bas` (line 1125)

```vb
' Load bank items
UserList(userindex).BancoInvent.NroItems = CInt(GetVar(
    App.Path & "\Accounts\" & UserList(userindex).Accounted & ".act",
    "BancoInventory",
    "CantidadItems"
))

' Loop through 40 slots
For loopC = 1 To MAX_BANCOINVENTORY_SLOTS
    ln = GetVar(
        App.Path & "\Accounts\" & UserList(userindex).Accounted & ".act",
        "BancoInventory",
        "Obj" & loopC
    )
    UserList(userindex).BancoInvent.Object(loopC).ObjIndex = CInt(ReadField(1, ln, 45))
    UserList(userindex).BancoInvent.Object(loopC).Amount = CInt(ReadField(2, ln, 45))
Next loopC

' Load bank gold
UserList(userindex).Stats.Banco = GetVar(
    App.Path & "\Accounts\" & UserList(userindex).Accounted & ".act",
    "" & UserList(userindex).Accounted & "",
    "BANCO"
)
```

**Delimiter**: ASCII 45 (comma `-`) separates ObjIndex and Amount.

### Saving
**File**: `FileIO.bas` (lines 1850-1905, 2164-2218)

```vb
' Save bank item count
Call WriteVar(
    App.Path & "\Accounts\" & UserList(userindex).Accounted & ".act",
    "BancoInventory",
    "CantidadItems",
    Val(UserList(userindex).BancoInvent.NroItems)
)

' Save each item slot
For LoopD = 1 To MAX_BANCOINVENTORY_SLOTS
    Call WriteVar(
        App.Path & "\Accounts\" & UserList(userindex).Accounted & ".act",
        "BancoInventory",
        "Obj" & LoopD,
        UserList(userindex).BancoInvent.Object(LoopD).ObjIndex & "-" &
        UserList(userindex).BancoInvent.Object(LoopD).Amount
    )
Next LoopD

' Save bank gold
Call WriteVar(
    App.Path & "\Accounts\" & UserList(userindex).Accounted & ".act",
    "" & UserList(userindex).Accounted & "",
    "BANCO",
    UserList(userindex).Stats.Banco
)
```

### TCP Opcodes

#### INITBANCO - Open Bank Window (Personal)
**Source**: `/Servidor/Codigo/modBanco.bas` (line 3)
**Handler**: `TCP_HandleData1.bas`

```vb
' Client sends: /inibov
' Server initiates deposit

Sub IniciarDeposito(ByVal userindex As Integer)
    Call UpdateBanUserInv(True, userindex, 0)  ' Update inventory display
    Call SendUserGLD(userindex)                 ' Send current gold
    SendData SendTarget.toindex, userindex, 0, "INITBANCO"
    UserList(userindex).flags.Comerciando = True
End Sub
```

**Server Response**: `INITBANCO`
- Opens bank window on client
- Client is now in "trading" mode

#### INITCBANK - Open Guild Bank Window
**Source**: `/Servidor/Codigo/modBancoNuevo.bas` (line 10)

```vb
Sub BIniciarDeposito(ByVal userindex As Integer)
    Call BUpdateBanUserInv(True, userindex, 0)
    Call SendUserGLD(userindex)
    SendData SendTarget.toindex, userindex, 0,
        "INITCBANK" & UserList(userindex).flags.PuedeRetirarObj & "," &
        UserList(userindex).flags.PuedeRetirarOro
    UserList(userindex).flags.Comerciando = True
End Sub
```

**Server Response**: `INITCBANK{CanWithdrawObj},{CanWithdrawGold}`
- `CanWithdrawObj`: 0 or 1 (perms to withdraw items)
- `CanWithdrawGold`: 0 or 1 (perms to withdraw gold)

#### DEPO - Deposit Item (Personal Bank)
**Source**: `TCP_HandleData1.bas` (line 2925)
**Client sends**: `DEPO{slot},{quantity}`

```vb
Case "DEPO"
    ' Validate user is alive
    If UserList(userindex).flags.Muerto = 1 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||3")
        Exit Sub
    End If

    ' Validate target NPC is banker
    If UserList(userindex).flags.TargetNPC = 0 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||9")
        Exit Sub
    End If
    If Npclist(UserList(userindex).flags.TargetNPC).NPCtype <> eNPCType.Banquero Then
        Exit Sub
    End If

    rData = Right(rData, Len(rData) - 5)
    ' User deposits item from slot rdata
    Call UserDepositaItem(
        userindex,
        Val(ReadField(1, rData, 44)),  ' Inventory slot
        Val(ReadField(2, rData, 44))   ' Quantity
    )
Exit Sub
```

**Packet format**: `DEPO` (4 bytes) + data (comma-delimited)
- Field 1: Inventory slot (1-25)
- Field 2: Quantity to deposit
- **Delimiter**: ASCII 44 (comma)

**Processing** (`modBanco.bas` line 191):
```vb
Sub UserDepositaItem(ByVal userindex As Integer, ByVal Item As Integer, ByVal Cantidad As Integer)
    On Error GoTo Errhandler

    If UserList(userindex).flags.Privilegios > PlayerType.User And _
       UserList(userindex).flags.Privilegios < PlayerType.Administrador Then Exit Sub

    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")  ' Can't trade with other players
        Exit Sub
    End If

    If UserList(userindex).Invent.Object(Item).Amount > 0 And _
       UserList(userindex).Invent.Object(Item).Equipped = 0 Then

        If Cantidad > 0 And Cantidad > UserList(userindex).Invent.Object(Item).Amount Then
            Cantidad = UserList(userindex).Invent.Object(Item).Amount
        End If

        Call UserDejaObj(userindex, CInt(Item), Cantidad)
        Call UpdateBanUserInv(True, userindex, 0)
        Call UpdateVentanaBanco(Item, 1, userindex)
    End If

Errhandler:
End Sub

Sub UserDejaObj(ByVal userindex As Integer, ByVal ObjIndex As Integer, ByVal Cantidad As Integer)
    Dim slot As Integer
    Dim obji As Integer

    If UserList(userindex).flags.Privilegios > PlayerType.User And _
       UserList(userindex).flags.Privilegios < PlayerType.Director Then
        Call SendData(SendTarget.toindex, userindex, 0, "||185")  ' Can't transfer
        Exit Sub
    End If

    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")
        Exit Sub
    End If

    If Cantidad < 1 Then Exit Sub

    obji = UserList(userindex).Invent.Object(ObjIndex).ObjIndex

    ' Check if item is non-transferable
    If ObjData(UserList(userindex).Invent.Object(ObjIndex).ObjIndex).Intransferible = 1 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||185")  ' Can't transfer
        Exit Sub
    End If

    ' Find existing stack or empty slot
    slot = 1
    Do Until UserList(userindex).BancoInvent.Object(slot).ObjIndex = obji And _
             UserList(userindex).BancoInvent.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS
        slot = slot + 1
        If slot > MAX_BANCOINVENTORY_SLOTS Then
            Exit Do
        End If
    Loop

    ' Find empty slot if no stack found
    If slot > MAX_BANCOINVENTORY_SLOTS Then
        slot = 1
        Do Until UserList(userindex).BancoInvent.Object(slot).ObjIndex = 0
            slot = slot + 1
            If slot > MAX_BANCOINVENTORY_SLOTS Then
                Call SendData(SendTarget.toindex, userindex, 0, "||186")  ' Bank full
                Exit Sub
            End If
        Loop
        If slot <= MAX_BANCOINVENTORY_SLOTS Then
            UserList(userindex).BancoInvent.NroItems = UserList(userindex).BancoInvent.NroItems + 1
        End If
    End If

    ' Add item to bank
    If slot <= MAX_BANCOINVENTORY_SLOTS Then
        If UserList(userindex).BancoInvent.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS Then
            UserList(userindex).BancoInvent.Object(slot).ObjIndex = obji
            UserList(userindex).BancoInvent.Object(slot).Amount = _
                UserList(userindex).BancoInvent.Object(slot).Amount + Cantidad

            Call QuitarUserInvItem(userindex, CByte(ObjIndex), Cantidad)
        Else
            Call SendData(SendTarget.toindex, userindex, 0, "||186")  ' Bank slot full
        End If
    Else
        Call QuitarUserInvItem(userindex, CByte(ObjIndex), Cantidad)
    End If

    Call LogDepositos("" & UserList(userindex).Name & " depositó " & Cantidad & " - " & ObjData(obji).Name & "")

End Sub
```

**Error codes**:
- `||185`: Item non-transferable
- `||186`: Bank full

#### RETR - Withdraw Item (Personal Bank)
**Source**: `TCP_HandleData3.bas` (line 154) - `/RETIRAR` command
**Also**: `TCP_HandleData1.bas` - Bank window interaction

Via `RETR` opcode or `/RETIRAR` command:
```vb
Case "/RETIRAR"  ' RETIRA ORO EN EL BANCO or removes from armada
    If UserList(userindex).flags.Muerto = 1 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||3")
        Exit Sub
    End If
    If UserList(userindex).flags.TargetNPC = 0 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||9")
        Exit Sub
    End If
    If Len(rData) = 8 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||508")
        Exit Sub
    End If
    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")
        Exit Sub
    End If

    rData = Right$(rData, Len(rData) - 9)
    If Npclist(UserList(userindex).flags.TargetNPC).NPCtype <> eNPCType.Banquero Or _
       UserList(userindex).flags.Muerto = 1 Then Exit Sub
    If Distancia(UserList(userindex).Pos, Npclist(UserList(userindex).flags.TargetNPC).Pos) > 10 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||10")
        Exit Sub
    End If

    If Val(rData) > 0 And Val(rData) <= UserList(userindex).Stats.Banco Then
        UserList(userindex).Stats.Banco = UserList(userindex).Stats.Banco - Val(rData)
        UserList(userindex).Stats.GLD = UserList(userindex).Stats.GLD + Val(rData)
        Call SendData(SendTarget.toindex, userindex, 0,
            "N|" & vbWhite & "¿" & "Tenes " & UserList(userindex).Stats.Banco &
            " monedas de oro en tu cuenta." & "¿" &
            Npclist(UserList(userindex).flags.TargetNPC).Char.CharIndex & "~69~190~156")
        Call SendData(SendTarget.toindex, userindex, 0, "[BG" & UserList(userindex).Stats.Banco)
    Else
        Call SendData(SendTarget.toindex, userindex, 0,
            "N|" & vbWhite & "¿" & " No tenes esa cantidad." & "¿" &
            Npclist(UserList(userindex).flags.TargetNPC).Char.CharIndex & "~69~190~156")
    End If
    Call SendUserGLD(Val(userindex))
```

Item withdrawal (`modBanco.bas` line 80):
```vb
Sub UserRetiraItem(ByVal userindex As Integer, ByVal i As Integer, ByVal Cantidad As Integer)
    On Error GoTo Errhandler

    If UserList(userindex).flags.Privilegios > PlayerType.User And _
       UserList(userindex).flags.Privilegios < PlayerType.Administrador Then Exit Sub

    If Cantidad < 1 Then Exit Sub

    Call SendUserGLD(userindex)

    If UserList(userindex).BancoInvent.Object(i).Amount > 0 Then
        If Cantidad > UserList(userindex).BancoInvent.Object(i).Amount Then
            Cantidad = UserList(userindex).BancoInvent.Object(i).Amount
        End If

        Call UserReciveObj(userindex, CInt(i), Cantidad)
        Call UpdateBanUserInv(True, userindex, 0)
        Call UpdateVentanaBanco(i, 0, userindex)
    End If

Errhandler:
End Sub

Sub UserReciveObj(ByVal userindex As Integer, ByVal ObjIndex As Integer, ByVal Cantidad As Integer)
    Dim slot As Integer
    Dim obji As Integer

    If UserList(userindex).BancoInvent.Object(ObjIndex).Amount <= 0 Then Exit Sub

    obji = UserList(userindex).BancoInvent.Object(ObjIndex).ObjIndex

    ' Find existing stack
    slot = 1
    Do Until UserList(userindex).Invent.Object(slot).ObjIndex = obji And _
            UserList(userindex).Invent.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS
        slot = slot + 1
        If slot > MAX_INVENTORY_SLOTS Then
            Exit Do
        End If
    Loop

    ' Find empty slot
    If slot > MAX_INVENTORY_SLOTS Then
        slot = 1
        Do Until UserList(userindex).Invent.Object(slot).ObjIndex = 0
            slot = slot + 1
            If slot > MAX_INVENTORY_SLOTS Then
                Call SendData(SendTarget.toindex, userindex, 0, "||108")  ' Inventory full
                Exit Sub
            End If
        Loop
        UserList(userindex).Invent.NroItems = UserList(userindex).Invent.NroItems + 1
    End If

    ' Add to inventory
    If UserList(userindex).Invent.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS Then
        UserList(userindex).Invent.Object(slot).ObjIndex = obji
        UserList(userindex).Invent.Object(slot).Amount = _
            UserList(userindex).Invent.Object(slot).Amount + Cantidad

        Call UpdateUserInv(False, userindex, slot)
        Call QuitarBancoInvItem(userindex, CByte(ObjIndex), Cantidad)
        Call LogDepositos("" & UserList(userindex).Name & " retiró " & Cantidad & " - " & ObjData(obji).Name & "")
    Else
        Call SendData(SendTarget.toindex, userindex, 0, "||108")  ' Inventory slot full
    End If

End Sub

Sub QuitarBancoInvItem(ByVal userindex As Integer, ByVal slot As Byte, ByVal Cantidad As Integer)
    Dim ObjIndex As Integer
    ObjIndex = UserList(userindex).BancoInvent.Object(slot).ObjIndex

    UserList(userindex).BancoInvent.Object(slot).Amount = _
        UserList(userindex).BancoInvent.Object(slot).Amount - Cantidad

    If UserList(userindex).BancoInvent.Object(slot).Amount <= 0 Then
        UserList(userindex).BancoInvent.NroItems = UserList(userindex).BancoInvent.NroItems - 1
        UserList(userindex).BancoInvent.Object(slot).ObjIndex = 0
        UserList(userindex).BancoInvent.Object(slot).Amount = 0
        Call updateBInventory(slot, userindex)
    End If
End Sub
```

**Error codes**:
- `||108`: Inventory full

#### /DEPOSITAR {gold_amount} - Deposit Gold
**Source**: `TCP_HandleData3.bas` (line 202)

```vb
Case "/DEPOSITAR "  ' DEPOSITAR ORO EN EL BANCO
    If UserList(userindex).flags.Muerto = 1 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||3")
        Exit Sub
    End If
    If UserList(userindex).flags.TargetNPC = 0 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||9")
        Exit Sub
    End If
    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")
        Exit Sub
    End If
    If Distancia(Npclist(UserList(userindex).flags.TargetNPC).Pos, UserList(userindex).Pos) > 10 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||10")
        Exit Sub
    End If

    rData = Right$(rData, Len(rData) - 11)
    If Npclist(UserList(userindex).flags.TargetNPC).NPCtype <> eNPCType.Banquero Or _
       UserList(userindex).flags.Muerto = 1 Then Exit Sub
    If Distancia(UserList(userindex).Pos, Npclist(UserList(userindex).flags.TargetNPC).Pos) > 10 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||10")
        Exit Sub
    End If

    If CLng(Val(rData)) > 0 And CLng(Val(rData)) <= UserList(userindex).Stats.GLD Then
        UserList(userindex).Stats.Banco = UserList(userindex).Stats.Banco + Val(rData)
        UserList(userindex).Stats.GLD = UserList(userindex).Stats.GLD - Val(rData)
        Call SendData(SendTarget.toindex, userindex, 0,
            "N|" & vbWhite & "¿" & "Tenes " & UserList(userindex).Stats.Banco &
            " monedas de oro en tu cuenta." & "¿" &
            Npclist(UserList(userindex).flags.TargetNPC).Char.CharIndex & "~69~190~156")
        Call SendData(SendTarget.toindex, userindex, 0, "[BG" & UserList(userindex).Stats.Banco)
    Else
        Call SendData(SendTarget.toindex, userindex, 0,
            "N|" & vbWhite & "¿" & " No tenes esa cantidad." & "¿" &
            Npclist(UserList(userindex).flags.TargetNPC).Char.CharIndex & "~69~190~156")
    End If
    Call SendUserGLD(Val(userindex))
Exit Sub
```

**Validation**:
- Distance <= 10 tiles
- Target is Banker NPC
- User is alive
- Amount > 0
- User has enough gold
- Not currently trading with another player

#### SBO{slot},{ObjIndex},{Name},{Amount},{GrhIndex},{Type},{MaxHIT},{MinHIT},{MaxDef}
**Server → Client**: Update bank slot display

From `modBanco.bas` (line 31):
```vb
Sub SendBanObj(userindex As Integer, slot As Byte, Object As UserOBJ)
    UserList(userindex).BancoInvent.Object(slot) = Object

    If Object.ObjIndex > 0 Then
        Call SendData(SendTarget.toindex, userindex, 0,
            "SBO" & slot & "," & Object.ObjIndex & "," &
            ObjData(Object.ObjIndex).Name & "," & Object.Amount & "," &
            ObjData(Object.ObjIndex).GrhIndex & "," &
            ObjData(Object.ObjIndex).OBJType & "," &
            ObjData(Object.ObjIndex).MaxHIT & "," &
            ObjData(Object.ObjIndex).MinHIT & "," &
            ObjData(Object.ObjIndex).MaxDef)
    End If
End Sub
```

**Packet format**: `SBO{slot},{ObjIndex},{Name},{Amount},{GrhIndex},{Type},{MaxHIT},{MinHIT},{MaxDef}`

#### SBR - Refresh Bank Display
From `modBanco.bas` (line 64):
```vb
Sub UpdateBanUserInv(ByVal UpdateAll As Boolean, ByVal userindex As Integer, ByVal slot As Byte)
    Dim NullObj As UserOBJ
    Dim loopC As Byte

    If Not UpdateAll Then
        If UserList(userindex).BancoInvent.Object(slot).ObjIndex > 0 Then
            Call SendBanObj(userindex, slot, UserList(userindex).BancoInvent.Object(slot))
        End If
    Else
        Call SendData(SendTarget.toindex, userindex, 0, "SBR")
        For loopC = 1 To MAX_BANCOINVENTORY_SLOTS
            If UserList(userindex).BancoInvent.Object(loopC).ObjIndex > 0 Then
                Call SendBanObj(userindex, loopC, UserList(userindex).BancoInvent.Object(loopC))
            End If
        Next loopC
    End If
End Sub
```

**Packet**: `SBR` - triggers full bank window refresh

#### BANCOOK{slot},{NpcSlot} - Confirm Update
From `modBanco.bas` (line 184):
```vb
Sub UpdateVentanaBanco(ByVal slot As Integer, ByVal NpcInv As Byte, ByVal userindex As Integer)
    Call SendData(SendTarget.toindex, userindex, 0, "BANCOOK" & slot & "," & NpcInv)
End Sub
```

**Packet**: `BANCOOK{slot},{NpcInv}`
- `slot`: Inventory slot updated
- `NpcInv`: 0=bank, 1=inventory

#### FINBAN - Close Bank
**Source**: `TCP_HandleData1.bas` (line 540)

```vb
Case "FINBAN"
    'User sale del modo BANCO
    UserList(userindex).flags.Comerciando = False
    Call SendData(SendTarget.toindex, userindex, 0, "FINBANOK")
    Exit Sub
```

**Client sends**: `FINBAN`
**Server response**: `FINBANOK`

#### [BG{gold} - Update Bank Gold Display
From `DatosUser.bas` - used in `/DEPOSITAR` and `/RETIRAR` commands

**Packet**: `[BG{amount}`
- Updates client display of bank gold

---

## Part 2: GUILD BANK (Clan-based)

### Storage Location
- **Path**: `guilds/Bancos/{GuildName}.bov`
- **Format**: INI file (Latin-1 encoding)

### File Format (guilds/Bancos/ClanName.bov)
```ini
[ClanName]
Creador=CharName
BANCO=100000

[BancoInventory]
CantidadItems=3
Obj1=201-10
Obj2=202-5
Obj3=0-0
...
Obj40=0-0
```

### Data Structure
```vb
Type BancoInventarioB
    Object(1 To 40) As UserOBJ
    NroItems As Integer
End Type

Type User
    BancoInventB As BancoInventarioB  ' Guild bank (B = "Banco")
    flags As UserFlags
    ...
End Type

Type UserFlags
    PuedeRetirarObj As Integer    ' Permission: withdraw items
    PuedeRetirarOro As Integer    ' Permission: withdraw gold
    ...
End Type
```

### Loading Guild Bank
**Source**: `Acciones.bas`

```vb
UserList(userindex).BancoInventB.NroItems = CInt(GetVar(
    App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
    "BancoInventory",
    "CantidadItems"
))

For loopC = 1 To MAX_BANCOINVENTORY_SLOTS
    ln = GetVar(
        App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
        "BancoInventory",
        "Obj" & loopC
    )
    UserList(userindex).BancoInventB.Object(loopC).ObjIndex = CInt(ReadField(1, ln, 45))
    UserList(userindex).BancoInventB.Object(loopC).Amount = CInt(ReadField(2, ln, 45))
Next loopC
```

### Saving Guild Bank
**Source**: `TCP_HandleData1.bas` (line 2415)

```vb
Case "CCBG"  ' Guardar items en la cuenta bancaria.
    rData = Right$(rData, Len(rData) - 4)

    Call WriteVar(
        App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
        "BancoInventory",
        "CantidadItems",
        Val(UserList(userindex).BancoInventB.NroItems)
    )

    For LoopD = 1 To MAX_BANCOINVENTORY_SLOTS
        Call WriteVar(
            App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
            "BancoInventory",
            "Obj" & LoopD,
            UserList(userindex).BancoInventB.Object(LoopD).ObjIndex & "-" &
            UserList(userindex).BancoInventB.Object(LoopD).Amount
        )
    Next LoopD
Exit Sub
```

### Guild Bank Gold - Deposit (CCDO)
**Source**: `TCP_HandleData1.bas` (line 2426)
**Client sends**: `CCDO{amount}`

```vb
Case "CCDO"
    rData = Right$(rData, Len(rData) - 4)
    Dim CantidadOroBank As Long
    Dim CantidadOroBank2 As Long
    CantidadOroBank2 = ReadField(1, rData, 44)  ' Amount to deposit

    CantidadOroBank = GetVar(
        App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
        "" & Guilds(UserList(userindex).GuildIndex).GuildName & "",
        "BANCO"
    )

    ' Check if total would overflow (max 999999999)
    If (CantidadOroBank + CantidadOroBank2) > 999999999 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||268")
        Exit Sub
    End If

    ' Check if user has enough gold
    If UserList(userindex).Stats.GLD < CantidadOroBank2 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||238")  ' Not enough gold
        Exit Sub
    End If

    ' Deposit
    Call WriteVar(
        App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
        "" & Guilds(UserList(userindex).GuildIndex).GuildName & "",
        "BANCO",
        CantidadOroBank + CantidadOroBank2
    )
    UserList(userindex).Stats.GLD = UserList(userindex).Stats.GLD - CantidadOroBank2
    SendUserGLD(userindex)
    Call BUpdateBanUserInv(True, userindex, 0)
    Call BUpdateVentanaBanco(0, 0, userindex)

Exit Sub
```

**Error codes**:
- `||238`: Not enough gold
- `||268`: Guild bank would overflow

**Packet format**: `CCDO{amount}`
- Field 1: Gold amount
- Delimiter: ASCII 44 (comma)

### Guild Bank Gold - Withdraw (CCRO)
**Source**: `TCP_HandleData1.bas` (line 2452)
**Client sends**: `CCRO{amount}`

```vb
Case "CCRO"
    rData = Right$(rData, Len(rData) - 4)
    CantidadOroBank2 = ReadField(1, rData, 44)  ' Amount to withdraw

    ' Check permission
    If UserList(userindex).flags.PuedeRetirarOro = 0 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||269")  ' No permission
        Exit Sub
    End If

    CantidadOroBank = GetVar(
        App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
        "" & Guilds(UserList(userindex).GuildIndex).GuildName & "",
        "BANCO"
    )

    ' Check if bank has enough
    If CantidadOroBank2 > CantidadOroBank Then
        Call SendData(SendTarget.toindex, userindex, 0, "||270")  ' Not enough gold
        Exit Sub
    End If

    ' Withdraw
    Call WriteVar(
        App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
        "" & Guilds(UserList(userindex).GuildIndex).GuildName & "",
        "BANCO",
        CantidadOroBank - CantidadOroBank2
    )
    UserList(userindex).Stats.GLD = UserList(userindex).Stats.GLD + CantidadOroBank2
    SendUserGLD(userindex)
    Call BUpdateBanUserInv(True, userindex, 0)
    Call BUpdateVentanaBanco(0, 0, userindex)

Exit Sub
```

**Error codes**:
- `||269`: No permission to withdraw gold
- `||270`: Guild bank has insufficient funds

### Guild Bank Items - Deposit (DEPB)
**Source**: `TCP_HandleData1.bas` (line 2944)
**Client sends**: `DEPB{slot},{quantity}`

```vb
Case "DEPB"
    If UserList(userindex).flags.Muerto = 1 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||3")
        Exit Sub
    End If

    rData = Right(rData, Len(rData) - 5)
    Call BUserDepositaItem(
        userindex,
        Val(ReadField(1, rData, 44)),
        Val(ReadField(2, rData, 44))
    )
Exit Sub
```

**Processing** (`modBancoNuevo.bas` line 184):
```vb
Sub BUserDepositaItem(ByVal userindex As Integer, ByVal Item As Integer, ByVal Cantidad As Integer)
    On Error GoTo Errhandler

    If UserList(userindex).flags.Privilegios > PlayerType.User And _
       UserList(userindex).flags.Privilegios < PlayerType.GranDios Then Exit Sub

    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")  ' Already trading
        Exit Sub
    End If

    If UserList(userindex).Invent.Object(Item).Amount > 0 And _
       UserList(userindex).Invent.Object(Item).Equipped = 0 Then

        If Cantidad > 0 And Cantidad > UserList(userindex).Invent.Object(Item).Amount Then
            Cantidad = UserList(userindex).Invent.Object(Item).Amount
        End If

        Call BUserDejaObj(userindex, CInt(Item), Cantidad)
        Call UpdateUserInv(True, userindex, 0)
        Call BUpdateBanUserInv(True, userindex, 0)
        Call BUpdateVentanaBanco(Item, 1, userindex)
    End If

Errhandler:
End Sub

Sub BUserDejaObj(ByVal userindex As Integer, ByVal ObjIndex As Integer, ByVal Cantidad As Integer)
    Dim slot As Integer
    Dim obji As Integer

    If UserList(userindex).flags.Privilegios > PlayerType.User And _
       UserList(userindex).flags.Privilegios < PlayerType.Director Then Exit Sub

    If UserList(userindex).cComercio.cComercia = True Then
        Call SendData(SendTarget.toindex, userindex, 0, "||153")
        Exit Sub
    End If

    If Cantidad < 1 Then Exit Sub

    obji = UserList(userindex).Invent.Object(ObjIndex).ObjIndex

    If ObjData(UserList(userindex).Invent.Object(ObjIndex).ObjIndex).Intransferible = 1 Then
        Call SendData(SendTarget.toindex, userindex, 0, "||185")  ' Can't transfer
        Exit Sub
    End If

    ' Find existing stack
    slot = 1
    Do Until UserList(userindex).BancoInventB.Object(slot).ObjIndex = obji And _
             UserList(userindex).BancoInventB.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS
        slot = slot + 1
        If slot > MAX_BANCOINVENTORY_SLOTS Then
            Exit Do
        End If
    Loop

    ' Find empty slot
    If slot > MAX_BANCOINVENTORY_SLOTS Then
        slot = 1
        Do Until UserList(userindex).BancoInventB.Object(slot).ObjIndex = 0
            slot = slot + 1
            If slot > MAX_BANCOINVENTORY_SLOTS Then
                Call SendData(SendTarget.toindex, userindex, 0, "||186")  ' Bank full
                Exit Sub
            End If
        Loop
        If slot <= MAX_BANCOINVENTORY_SLOTS Then
            UserList(userindex).BancoInventB.NroItems = _
                UserList(userindex).BancoInventB.NroItems + 1
        End If
    End If

    ' Add to guild bank
    If slot <= MAX_BANCOINVENTORY_SLOTS Then
        If UserList(userindex).BancoInventB.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS Then
            UserList(userindex).BancoInventB.Object(slot).ObjIndex = obji
            UserList(userindex).BancoInventB.Object(slot).Amount = _
                UserList(userindex).BancoInventB.Object(slot).Amount + Cantidad

            Call QuitarUserInvItem(userindex, CByte(ObjIndex), Cantidad)
        Else
            Call SendData(SendTarget.toindex, userindex, 0, "||186")
        End If
    Else
        Call QuitarUserInvItem(userindex, CByte(ObjIndex), Cantidad)
    End If

End Sub
```

**Error codes**:
- `||153`: Already trading with another player
- `||185`: Item is non-transferable
- `||186`: Guild bank full

### Guild Bank Items - Withdraw
**Source**: `modBancoNuevo.bas` (line 75)

```vb
Sub BUserRetiraItem(ByVal userindex As Integer, ByVal i As Integer, ByVal Cantidad As Integer)
    On Error GoTo Errhandler

    If UserList(userindex).flags.Privilegios > PlayerType.User And _
       UserList(userindex).flags.Privilegios < PlayerType.GranDios Then Exit Sub

    If Cantidad < 1 Then Exit Sub

    Call SendUserGLD(userindex)

    If UserList(userindex).BancoInventB.Object(i).Amount > 0 Then
        If Cantidad > UserList(userindex).BancoInventB.Object(i).Amount Then
            Cantidad = UserList(userindex).BancoInventB.Object(i).Amount
        End If

        Call BUserReciveObj(userindex, CInt(i), Cantidad)
        Call UpdateUserInv(True, userindex, 0)
        Call BUpdateBanUserInv(True, userindex, 0)
        Call BUpdateVentanaBanco(i, 0, userindex)
    End If

Errhandler:
End Sub

Sub BUserReciveObj(ByVal userindex As Integer, ByVal ObjIndex As Integer, ByVal Cantidad As Integer)
    Dim slot As Integer
    Dim obji As Integer

    If UserList(userindex).BancoInventB.Object(ObjIndex).Amount <= 0 Then Exit Sub

    obji = UserList(userindex).BancoInventB.Object(ObjIndex).ObjIndex

    ' Find existing stack in inventory
    slot = 1
    Do Until UserList(userindex).Invent.Object(slot).ObjIndex = obji And _
            UserList(userindex).Invent.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS
        slot = slot + 1
        If slot > MAX_INVENTORY_SLOTS Then
            Exit Do
        End If
    Loop

    ' Find empty slot in inventory
    If slot > MAX_INVENTORY_SLOTS Then
        slot = 1
        Do Until UserList(userindex).Invent.Object(slot).ObjIndex = 0
            slot = slot + 1
            If slot > MAX_INVENTORY_SLOTS Then
                Call SendData(SendTarget.toindex, userindex, 0, "||108")  ' Inventory full
                Exit Sub
            End If
        Loop
        UserList(userindex).Invent.NroItems = UserList(userindex).Invent.NroItems + 1
    End If

    ' Add to inventory
    If UserList(userindex).Invent.Object(slot).Amount + Cantidad <= MAX_INVENTORY_OBJS Then
        UserList(userindex).Invent.Object(slot).ObjIndex = obji
        UserList(userindex).Invent.Object(slot).Amount = _
            UserList(userindex).Invent.Object(slot).Amount + Cantidad

        Call BQuitarBancoInvItem(userindex, CByte(ObjIndex), Cantidad)
    Else
        Call SendData(SendTarget.toindex, userindex, 0, "||108")  ' Inventory slot full
    End If

End Sub

Sub BQuitarBancoInvItem(ByVal userindex As Integer, ByVal slot As Byte, ByVal Cantidad As Integer)
    Dim ObjIndex As Integer
    ObjIndex = UserList(userindex).BancoInventB.Object(slot).ObjIndex

    UserList(userindex).BancoInventB.Object(slot).Amount = _
        UserList(userindex).BancoInventB.Object(slot).Amount - Cantidad

    If UserList(userindex).BancoInventB.Object(slot).Amount <= 0 Then
        UserList(userindex).BancoInventB.NroItems = _
            UserList(userindex).BancoInventB.NroItems - 1
        UserList(userindex).BancoInventB.Object(slot).ObjIndex = 0
        UserList(userindex).BancoInventB.Object(slot).Amount = 0
    End If
End Sub
```

**Error codes**:
- `||108`: Inventory full

### Guild Bank Display Updates
**SBG{slot},{ObjIndex},{Name},{Amount},{GrhIndex},{Type},{MaxHIT},{MinHIT},{MaxDef},{GuildGold},{UserGold}**

From `modBancoNuevo.bas` (line 18):
```vb
Sub BSendBanObj(userindex As Integer, slot As Byte, Object As UserOBJ)
    UserList(userindex).BancoInventB.Object(slot) = Object

    If Object.ObjIndex > 0 Then
        Call SendData(SendTarget.toindex, userindex, 0,
            "SBG" & slot & "," & Object.ObjIndex & "," &
            ObjData(Object.ObjIndex).Name & "," & Object.Amount & "," &
            ObjData(Object.ObjIndex).GrhIndex & "," &
            ObjData(Object.ObjIndex).OBJType & "," &
            ObjData(Object.ObjIndex).MaxHIT & "," &
            ObjData(Object.ObjIndex).MinHIT & "," &
            ObjData(Object.ObjIndex).MaxDef & "," &
            GetVar(App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
                   "" & Guilds(UserList(userindex).GuildIndex).GuildName & "", "BANCO") & "," &
            UserList(userindex).Stats.GLD)
    Else
        Call SendData(SendTarget.toindex, userindex, 0,
            "SBG" & slot & "," & "0" & "," & "(Nada)" & "," & "0" & "," & "0" & "," & "0" & "," &
            "0" & "," & "0" & "," & "0" & "," &
            GetVar(App.Path & "\guilds\Bancos\" & Guilds(UserList(userindex).GuildIndex).GuildName & ".bov",
                   "" & Guilds(UserList(userindex).GuildIndex).GuildName & "", "BANCO") & "," &
            UserList(userindex).Stats.GLD)
    End If
End Sub
```

### Guild Bank Open - INITCBANK
**Source**: `modBancoNuevo.bas` (line 2)

```vb
Sub BIniciarDeposito(ByVal userindex As Integer)
    Call BUpdateBanUserInv(True, userindex, 0)
    Call SendUserGLD(userindex)
    SendData SendTarget.toindex, userindex, 0,
        "INITCBANK" & UserList(userindex).flags.PuedeRetirarObj & "," &
        UserList(userindex).flags.PuedeRetirarOro
    UserList(userindex).flags.Comerciando = True
End Sub
```

**Packet**: `INITCBANK{CanWithdrawObj},{CanWithdrawGold}`

### Guild Bank Close - FINCBN
**Source**: `TCP_HandleData1.bas` (line 545)

```vb
Case "FINCBN"
    UserList(userindex).flags.Comerciando = False
    UserList(userindex).flags.CuentaBancaria = ""
    Call SendData(SendTarget.toindex, userindex, 0, "FINCBNOK")
    Exit Sub
```

---

## Summary of Opcodes

### Personal Bank
| Opcode | Direction | Format | Purpose |
|--------|-----------|--------|---------|
| INITBANCO | Server→Client | `INITBANCO` | Open bank window |
| DEPO | Client→Server | `DEPO{slot},{qty}` | Deposit item |
| RETR | Command | `/RETIRAR {amount}` | Withdraw gold |
| /DEPOSITAR | Command | `/DEPOSITAR {amount}` | Deposit gold |
| SBO | Server→Client | `SBO{slot},...` | Update bank slot |
| SBR | Server→Client | `SBR` | Refresh all slots |
| BANCOOK | Server→Client | `BANCOOK{slot},{type}` | Confirm update |
| FINBAN | Bidirectional | `FINBAN` / `FINBANOK` | Close bank |
| [BG | Server→Client | `[BG{amount}` | Update gold display |
| [G | Server→Client | `[G{amount}` | Update inventory gold |

### Guild Bank
| Opcode | Direction | Format | Purpose |
|--------|-----------|--------|---------|
| INITCBANK | Server→Client | `INITCBANK{canObj},{canGold}` | Open guild bank |
| DEPB | Client→Server | `DEPB{slot},{qty}` | Deposit item |
| CCDO | Client→Server | `CCDO{amount}` | Deposit gold |
| CCRO | Client→Server | `CCRO{amount}` | Withdraw gold |
| CCBG | Client→Server | `CCBG` | Save items |
| SBG | Server→Client | `SBG{slot},...` | Update bank slot |
| BANCOBK | Server→Client | `BANCOBK{slot},{type}` | Confirm update |
| FINCBN | Bidirectional | `FINCBN` / `FINCBNOK` | Close guild bank |

---

## Key Validation Rules

### Both Banks
- User must be **alive** (flags.Muerto = 0)
- User **cannot be dead** (||3)
- User **cannot be trading** with another player (||153)
- Distance to banker NPC **<= 10 tiles** (||10)
- Target **must be a Banker NPC** (NPCtype = Banquero)
- Items must **not be equipped** (Equipped = 0)
- Items must **not be non-transferable** (||185)
- Item stacks **<= 999** (MAX_INVENTORY_OBJS)
- Bank **max 40 items/types** (MAX_BANCOINVENTORY_SLOTS)
- Gold **max 999,999,999** (||268)

### Permissions (Guild Bank)
- `flags.PuedeRetirarObj`: Can withdraw items (0=no, 1=yes)
- `flags.PuedeRetirarOro`: Can withdraw gold (0=no, 1=yes)

---

## Error Codes

| Code | Meaning |
|------|---------|
| `||3` | User is dead |
| `||9` | Invalid NPC target |
| `||10` | Too far from NPC |
| `||108` | Inventory full |
| `||153` | Already trading with player |
| `||185` | Item non-transferable |
| `||186` | Bank full |
| `||238` | Not enough gold |
| `||268` | Bank would overflow |
| `||269` | No permission to withdraw gold |
| `||270` | Guild bank has insufficient funds |

---

## Implementation Notes

### Encoding
- **Delimiters**: ASCII 44 (comma) for fields, ASCII 45 (dash) for object pairs
- **Character encoding**: Latin-1 (for INI files)

### Persistence
- **Account bank**: `Accounts/{account}.act`
- **Guild bank**: `guilds/Bancos/{GuildName}.bov`
- Both use INI file format with GetVar/WriteVar

### Memory vs. Disk
- Data loaded to `UserList[].BancoInvent` or `BancoInventB` on login
- Data persists to disk on logout (`SaveUser`)
- Guild bank also saved on `CCBG` opcode

### Logging
- Deposits/withdrawals logged to file via `LogDepositos()`
- Pattern: `"{PlayerName} depositó/retiró {Cantidad} - {ItemName}"`

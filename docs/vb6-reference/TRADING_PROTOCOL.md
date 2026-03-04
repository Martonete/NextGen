# Player-to-Player Trading Protocol (VB6 → Rust)

## Source
- **VB6 Implementation**: `/Servidor/Codigo/AA_ComercioUsuarios.bas` (305 lines)
- **Opcode Handlers**: `TCP_HandleData1.bas` (lines 1578-1602), `TCP_HandleData2.bas` (line 1755)
- **Data Structure**: `Declares.bas` (lines 1565-1573)

---

## Trading State Structure

### UserType.cComercio (cFlagComer)
```vb
Public Type cFlagComer
    cObj(20)      As obj           ' Inventory slots 1-20
    cComercia     As Boolean       ' Is actively trading
    cQuien        As Integer       ' Trading partner's userindex
    cOfrecio      As Boolean       ' Has offered items
    cRecivio      As Boolean       ' Has received offer from partner
    cRespuesta    As Byte          ' Response: 0=none, 1=reject, 2=accept
    cOro          As Long          ' (unused - use flags.OroQueOferto instead)
End Type

' Gold offered during trade (stored in User.flags)
Public flags.OroQueOferto As Long
```

**Inventory Structure** (obj type):
```vb
Type obj
    ObjIndex As Integer   ' Item ID (0 = empty slot)
    Amount   As Integer   ' Quantity
    Equipped As Byte      ' Equipped slot (0 = not equipped)
End Type
```

---

## Trade Flow State Machine

```
User1                              User2
  |                                  |
  +--- /COMERCIAR (target User2) ---->|
  |                                  |
  |<---- "593@User1" message --------|
  |                                  |
  | (User2 accepts trade invite)     |
  |<---- ICO packet (inventories) ----|
  | ---> ICO packet (inventories) --->|
  |                                  |
  | (Both send gold + items offered) |
  | ---> UOR + UOC packets --------->|
  |<---- IOR + ICI packets ----------|
  |                                  |
  | (Both accept/reject)             |
  | ---> TDR (response) ------------->|
  |<---- TDR (response) --------------|
  |                                  |
  | If both accept (TDR=2):          |
  | <------- Execute trade --------->|
  | ---> TCM or error -------------->|
  |<---- TCM or error --------------|
```

---

## Opcodes & Packet Formats

### CLIENT → SERVER

#### 1. **UOR** - Offer Gold
```
Packet: "UOR" + <gold_amount>

Example: "UOR1000"
         "UOR0" (no gold)

Purpose: Specify how much gold to offer in the trade
State Check: Must be in active trade (cComercio=true)
Validation:
  - User must have enough gold (Stats.GLD >= gold_amount)
  - Stores in User.flags.OroQueOferto
  - No duplicate offers allowed
```

#### 2. **UOC** - Offer Items
```
Packet: "UOC" + <item_data>

Item Data Format (comma-separated):
  slot1: "<amount>-<trash>" (amount=0 means nothing offered)
  slot2: "<amount>-<trash>"
  ...
  slot20: "<amount>-<trash>"

Example: "UOC0-0,0-0,1-0,5-0,0-0,..." (offering 1x item from slot 3, 5x from slot 4)

Note: <trash> is ignored (was used for item name display in VB6)

Purpose: Mark which inventory items to trade
State Check: Must be in active trade
Process:
  1. Parse each slot's <amount> field
  2. Copy amount from User.Invent.Object(slot) to User.cComercio.cObj(slot)
  3. Validate all items exist and have enough quantity
  4. Send back server confirmation via IOR + ICI
  5. Set User.cComercio.cOfrecio = true

After both players offer:
  Message: "Servidor> Ya has recibido respuesta, debes ACEPTAR o RECHAZAR la oferta"
```

#### 3. **TDR** - Accept/Reject Trade
```
Packet: "TDR" + <response>

Response Values:
  0 = No response (ignored)
  1 = Reject trade
  2 = Accept trade

Example: "TDR1" (reject)
         "TDR2" (accept)

Purpose: Accept or reject the other player's offer
Behavior:
  - If response=1 (reject): cancel entire trade via comCancelar()
  - If response=2 (accept):
    * Set User.cComercio.cRespuesta = 2
    * Check if partner also set cRespuesta=2
    * If both accepted: execute comHacerCambio()
    * Otherwise: wait for partner response
```

#### 4. **TCM** - Cancel Trade
```
Packet: "TCM" (no data)

Purpose: Manually cancel trade
Effect: Calls comCancelar() which:
  - Notifies partner with "||596" message
  - Resets both players' trade state via comReset()
```

#### 5. **VHC** - Trade Chat
```
Packet: "VHC" + <message>

Example: "VHCHi, is this item good?"

Purpose: Send chat message to trading partner
Delivery: Sent with "MEC" opcode: "MEC<player_name>> <message><FONTTYPE>"
```

---

### SERVER → CLIENT

#### 1. **ICO** - Show Inventories (Trade Window Open)
```
Packet: "ICO" + <partner_name> + "$" + <inventory_list>

Inventory List Format (comma-separated):
  ObjIndex-Amount-ItemName,ObjIndex-Amount-ItemName,...

Example:
  "ICOUser2$5-1-(Sword of Fire),0-0-(Nada),23-3-(Magic Potion),..."

Purpose: Initialize trade window with both inventories
Sent:
  1. When User1 initiates trade and User2 accepts
  2. Sent to User1 showing User2's inventory
  3. Sent to User2 showing User1's inventory

Fields:
  - User2 name: trading partner's character name
  - ObjIndex: item ID (0 = empty slot)
  - Amount: quantity in inventory
  - ItemName: item name for UI display (from ObjData)
```

#### 2. **IOR** - Show Offered Gold
```
Packet: "IOR" + <gold_amount>

Example: "IOR500"
         "IOR0"

Purpose: Show how much gold the OTHER player is offering
Sent: When trading partner executes UOC (offers items)
```

#### 3. **ICI** - Show Offered Items
```
Packet: "ICI" + <item_data>

Item Data Format (comma-separated):
  grhindex-amount-itemname,grhindex-amount-itemname,...

Example: "ICI45-1-(Sword),0-0-(Nada),23-3-(Potion),..."

Fields:
  - GrhIndex: graphics ID for UI display
  - Amount: quantity being offered
  - ItemName: item name for UI display

Purpose: Show which items the OTHER player is offering
Sent: When trading partner executes UOC (offers items)
Note: Uses GrhIndex (graphics) instead of ObjIndex (item ID)
```

#### 4. **VCC** - Reset Trade State
```
Packet: "VCC" (no data)

Purpose: Reset client-side trade state (clear UI, close window)
Sent:
  1. When trade is cancelled (comCancelar)
  2. When trade completes (comReset)
  3. When player logs out while trading
```

#### 5. **||596** - "Trade Cancelled" Message
```
Packet: "||596"

Purpose: Notify that partner cancelled the trade
Response: Client should close trade window
```

#### 6. **||597 / ||598** - Item Validation Errors
```
||597 = Partner doesn't have all offered items
||598 = You don't have all offered items
```

#### 7. **||599** - Can't Trade Keys/Special Items
```
||599 = One player tried to offer keys or invalid items
```

#### 8. **||600 / ||601** - Untradeable/Divine Items
```
||600 = You tried to offer untradeable/divine item
||601 = Partner tried to offer untradeable/divine item
```

#### 9. **||602 / ||603** - Insufficient Gold
```
||602 = You don't have enough gold
||603 = Partner doesn't have enough gold
```

#### 10. **||604** - Trade Complete
```
||604 = Trade successful, items exchanged
```

#### System Messages (comMen)
```
Message codes sent as: "||<code>"
9    = Invalid target (no target selected)
158  = Target too far away (not adjacent)
291  = Trading forbidden in battle zone
323  = Trading forbidden in PK zone
422  = Target already trading
592  = Already trading with this player (redundant request)
593@<name> = Incoming trade request from <name>
594@<name> = Outgoing trade request to <name>
595  = Insufficient gold
596  = Trade cancelled by partner
597  = You don't have the items
598  = Partner doesn't have the items
599  = Can't trade keys/special items
600  = You offered untradeable item
601  = Partner offered untradeable item
602  = You don't have enough gold
603  = Partner doesn't have enough gold
604  = Trade complete
```

---

## Validation & Security Checks

### Pre-Trade Validation (comManda)
```vb
1. Target must be valid userindex (not 0, not self)
2. Both players must be alive
3. Neither player already trading (cComercio = false)
4. Map is not PK zone (MapInfo[map].Pk = false)
5. Map is not battle zone (MapData[x,y].trigger ≠ ZONAPELEA)
6. Both players adjacent (Distancia ≤ 1)
7. Neither in different battle zone at their location
```

### Before Exchange (comHacerCambio)
```vb
For each offered item from BOTH players:
  1. Item must exist in inventory (ObjIndex > 0)
  2. Player must have required quantity (TieneObjetos check)
  3. Item type ≠ otLlaves (keys)
  4. Item not marked Intransferible=1
  5. Item not marked ItemDios=1 (divine)
  6. Player has enough gold (Stats.GLD ≥ OroQueOferto)

Privilege Check:
  - GMod+ (Consejero/Semidios/Dios) CANNOT trade with players
  - Regular users can trade freely

If ANY validation fails:
  - Cancel both players' trades
  - Send error message to initiator + partner
  - Reset all trade state
```

### Gold Exchange Logic
```vb
User1 gold:
  -= User1.OroQueOferto (remove offer)
  += User2.OroQueOferto (receive payment)

User2 gold:
  -= User2.OroQueOferto (remove offer)
  += User1.OroQueOferto (receive payment)

SendUserGLD() called for both (updates client)
```

### Item Exchange Logic
```vb
For each slot (1-20):
  1. If User1.cComercio.cObj[i].ObjIndex > 0:
     - Deequip if equipped
     - Remove from User1 inventory
     - Add to User2 inventory (or drop on ground if full)
     - Log trade to file

  2. Same for User2 → User1
```

---

## GM/Admin Bypass

### Restrictions for Non-Admins
```vb
In comHacerCambio():
  If User.flags.Privilegios > PlayerType.User AND
     User.flags.Privilegios < PlayerType.Administrador
  Then Exit Sub  ' Mods cannot initiate trades
```

**Interpretation**:
- Regular players (Privilegios=0): can trade
- Admins (Privilegios=Admin): can trade
- Mods/Consejeros: CANNOT trade (probably to prevent duping)

---

## Constraints & Limits

| Constraint | Value | Notes |
|---|---|---|
| Max inventory slots | 20 | Fixed array size |
| Max items per slot | varies | Type depends (stackable vs single) |
| Max gold per trade | Long (2^31-1) | OroQueOferto is Long |
| Trade distance | ≤1 tile | Distancia(pos1, pos2) > 1 = too far |
| Dead players | Cannot trade | flags.Muerto check |
| PK maps | Cannot trade | MapInfo[map].Pk=true blocks |
| Battle zones | Cannot trade | MapData[x,y].trigger=ZONAPELEA blocks |
| Untradeable items | Keys, divine items | OBJType=otLlaves or Intransferible=1 or ItemDios=1 |

---

## Error Handling

All subroutines wrap in `On Error GoTo` blocks that call:
```vb
comLogBug("Error context + player names + subroutine name")
```

Writes to: `Server.Path\BugsComercio.txt` with timestamp

**Never stops trade silently** — logs every error for admin review.

---

## Implementation Notes

1. **No atomic transactions**: If one player disconnects mid-trade, items may be duplicated. Validation at step 2 (comHacerCambio) attempts to catch this, but race conditions possible with dropped connections.

2. **Equipped items unequipped before removal**: Prevents inventory locks.

3. **Dropped items fallback**: If inventory full, items drop on ground at player's location.

4. **Chat during trade**: Separate from normal chat, prefixed with player name + ">" in trade window.

5. **Mod privileges**: Consejeros/Semidios/Dios cannot initiate trades (design choice).

6. **No trade with NPCs**: "/COMERCIAR" checks for TargetUser > 0 (player) vs TargetNPC > 0 (NPC shops are separate).

---

## Data Flow Example

```
User1 initiates trade with User2:
  /COMERCIAR command → comManda(User1)
    → Sets User2.cComercio.cQuien = User1
    → Sends "593@User1" to User2

User2 accepts:
  /COMERCIAR command → comManda(User2)
    → Detects User1 is already requesting
    → Calls comIniciarForm(User1, User2)
    → Sends "ICO" + inventories to both

User1 offers 1000 gold + Sword (slot 3):
  UOR1000 → comManda parses, sets OroQueOferto=1000
  UOC0-0,0-0,1-0,... → comMandoOferta()
    → Copies Invent.Object(3) to cComercio.cObj(3)
    → Sends "IOR1000" to User2
    → Sends "ICI<items>" to User2
    → Sets cOfrecio=true

User2 offers 500 gold + Potion (slot 2):
  UOR500 → Sets OroQueOferto=500
  UOC0-0,1-0,0-0,... → comMandoOferta()
    → Sends "IOR500" to User1
    → Sends "ICI<items>" to User1
    → Displays message: "Servidor> Ya has recibido respuesta..."

User1 accepts:
  TDR2 → comAceptaORechaza(User1, 2)
    → Sets cRespuesta=2
    → Checks if User2.cRespuesta also = 2 (not yet)

User2 accepts:
  TDR2 → comAceptaORechaza(User2, 2)
    → Sets cRespuesta=2
    → Checks if User1.cRespuesta = 2 (YES!)
    → Calls comHacerCambio(User2, User1)

comHacerCambio executes:
  1. Validates all items/gold for both players
  2. Removes items from User1, adds to User2
  3. Removes items from User2, adds to User1
  4. Deducts gold from User1, adds User2's gold
  5. Deducts gold from User2, adds User1's gold
  6. Logs transaction
  7. Sends "||604" (success) to both
  8. Calls comReset() on both
  9. Sends "VCC" to both (close window)
```

---

## File References

- **Main logic**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/AA_ComercioUsuarios.bas`
- **Opcode routing**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/TCP_HandleData1.bas` (lines 1578-1602)
- **Trade initiation**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/TCP_HandleData2.bas` (lines 1755-1786)
- **Data structures**: `/workspace/Tierras-Sagradas-AO/Servidor/Codigo/Declares.bas` (lines 1565-1573, 1327)

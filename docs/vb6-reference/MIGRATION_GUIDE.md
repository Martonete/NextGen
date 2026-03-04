# Guía de Migración: Protocolo Text 11.5 → Binary 13.3

## Resumen

TSAO usaba un protocolo text-based con 4 capas de encriptación. El protocolo 13.3 es binario puro, sin encriptación, más eficiente y type-safe.

| Aspecto | TSAO (11.5) | 13.3 (Binary) |
|---------|-------------|---------------|
| Packet ID | Texto variable (`CC`, `M1`, `ALOGIN`) | 1 byte numérico (0-129) |
| Campos | Comma/tilde separated ASCII | Tipos binarios LE (ByteQueue) |
| Encriptación | XOR + Base64 + Hex + Key rotation | **Ninguna** |
| Strings | Inline, delimitados | 2-byte LE length prefix + ASCII |
| Framing | Null byte (0x00) delimited | Sin framing explícito (tamaños conocidos por opcode) |

---

## Cómo adaptar un sistema del servidor 11.5 al protocolo 13.3

### Paso 1: Identificar los paquetes involucrados

Cada sistema del juego tiene paquetes Client→Server y Server→Client. Ejemplo para el sistema de combate:

```
Client→Server: AT (texto) → ClientPacketID::Attack (byte 8)
Server→Client: U2{charIdx},{dmg} (texto) → MultiMessage::UserHitNPC (104 + sub 13)
```

### Paso 2: Reescribir Server→Client (outbound)

**ANTES (11.5 text):**
```rust
// Construir paquete de texto con format!
let pkt = format!("CC{},{},{},{},{},{},{},{},{},{},{},{}",
    body, head, heading, char_index, x, y, weapon, shield, helmet, name, status, priv);
state.send_to(conn_id, &pkt).await;
```

**DESPUÉS (13.3 binary):**
```rust
use crate::protocol::{ByteQueue, packets::ServerPacketID};

let mut pkt = ByteQueue::new();
pkt.write_byte(ServerPacketID::CharacterCreate.to_byte());
pkt.write_integer(char_index);  // i16
pkt.write_integer(body);        // i16
pkt.write_integer(head);        // i16
pkt.write_byte(heading);        // u8
pkt.write_byte(x);              // u8
pkt.write_byte(y);              // u8
pkt.write_integer(weapon);      // i16
pkt.write_integer(shield);      // i16
pkt.write_integer(helmet);      // i16
pkt.write_integer(fx_index);    // i16
pkt.write_integer(fx_loops);    // i16
pkt.write_ascii_string(&name);  // 2B len + ASCII
pkt.write_byte(nick_color);     // u8
pkt.write_byte(privileges);     // u8
state.send_bytes(conn_id, pkt.as_bytes()).await;
```

### Paso 3: Reescribir Client→Server (inbound)

**ANTES (11.5 text):**
```rust
// Dispatcher basado en starts_with
if data.starts_with("ALOGIN") {
    let fields = &data[6..]; // skip opcode
    let account = fields.read_field(1, ',');
    let password = fields.read_field(2, ',');
    let version = fields.read_field(3, ',');
}
```

**DESPUÉS (13.3 binary):**
```rust
use crate::protocol::{ByteQueue, packets::ClientPacketID};

// Dispatcher basado en peek_byte
let packet_id = buffer.peek_byte()?;
match ClientPacketID::from_byte(packet_id) {
    Some(ClientPacketID::LoginExistingChar) => {
        buffer.read_byte()?; // consume ID
        let name = buffer.read_ascii_string()?;
        let password = buffer.read_ascii_string()?;
        let ver_major = buffer.read_byte()?;
        let ver_minor = buffer.read_byte()?;
        let ver_revision = buffer.read_byte()?;
    }
    // ...
}
```

### Paso 4: Actualizar MultiMessage para mensajes frecuentes

13.3 usa `MultiMessage` (ServerPacketID 104) para combate y estados:

```rust
// ANTES: paquetes individuales de texto
state.send_to(conn_id, &format!("U2{},{}", char_idx, damage)).await;

// DESPUÉS: MultiMessage sub-type
let mut pkt = ByteQueue::new();
pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
pkt.write_byte(MultiMessageID::UserHitNPC.to_byte());
pkt.write_long(damage);
state.send_bytes(conn_id, pkt.as_bytes()).await;
```

### Paso 5: Actualizar GM Commands

Todos los comandos GM van bajo `ClientPacketID::GMCommands` (122):

```rust
// ANTES: cada GM command era un texto distinto
if data.starts_with(";/TELEP") { ... }

// DESPUÉS: un solo dispatcher con sub-byte
Some(ClientPacketID::GMCommands) => {
    buffer.read_byte()?; // consume 122
    let sub_cmd = buffer.read_byte()?;
    match GMCommandID::from_byte(sub_cmd) {
        Some(GMCommandID::WarpChar) => {
            let name = buffer.read_ascii_string()?;
            let map = buffer.read_integer()?;
            let x = buffer.read_byte()?;
            let y = buffer.read_byte()?;
        }
        // ...
    }
}
```

---

## Tipos de datos del ByteQueue

| VB6 Type | Wire Size | Rust Type | ByteQueue Method |
|----------|-----------|-----------|------------------|
| Byte | 1 byte | u8 | write_byte / read_byte |
| Boolean | 1 byte | bool | write_boolean / read_boolean |
| Integer | 2 bytes LE | i16 | write_integer / read_integer |
| Long | 4 bytes LE | i32 | write_long / read_long |
| Single | 4 bytes IEEE754 | f32 | write_single / read_single |
| Double | 8 bytes IEEE754 | f64 | write_double / read_double |
| String | 2B LE len + N ASCII | String | write_ascii_string / read_ascii_string |

---

## Tabla de correspondencia: Opcodes text → Packet IDs binary

### Client → Server (más comunes)

| Text Opcode (11.5) | Binary ID (13.3) | Nombre |
|---------------------|-------------------|--------|
| `KERD22` | 0 | LoginExistingChar* |
| `ALOGIN` | 0 | LoginExistingChar |
| `TIRDAD` | 1 | ThrowDices |
| `NLOGIN` | 2 | LoginNewChar |
| `;` (talk) | 3 | Talk |
| `-` (yell) | 4 | Yell |
| `\` (whisper) | 5 | Whisper |
| `M<1-4>` | 6 | Walk (heading in payload) |
| `RPU` | 7 | RequestPositionUpdate |
| `AT` | 8 | Attack |
| `AG` | 9 | PickUp |
| `SEG` | 10 | SafeToggle |
| `TI` | 24 | Drop |
| `LH` | 25 | CastSpell |
| `LC` | 26 | LeftClick |
| `USA` | 30 | UseItem |
| `EQUI` | 36 | EquipItem |
| `CHEA` | 37 | ChangeHeading |
| `COMP` | 40 | CommerceBuy |
| `VEND` | 42 | CommerceSell |

*Nota: TSAO separaba KERD22/ALOGIN/OOLOGI/THCJXD en flujo multi-paso. 13.3 usa solo LoginExistingChar (0) con todo en un paquete.*

### Server → Client (más comunes)

| Text Opcode (11.5) | Binary ID (13.3) | Nombre |
|---------------------|-------------------|--------|
| `LOGGED` | 0 | Logged |
| `CM` | 21 | ChangeMap |
| `PU` | 22 | PosUpdate |
| `T\|` | 23 | ChatOverHead |
| `P\|` | 24 | ConsoleMsg |
| `CC` | 29 | CharacterCreate |
| `BP` | 30 | CharacterRemove |
| `MP` / `+` | 32 | CharacterMove |
| `CP` | 34 | CharacterChange |
| `HO` | 35 | ObjectCreate |
| `BO` | 36 | ObjectDelete |
| `CSI` | 47 | ChangeInventorySlot |
| `SHS` | 49 | ChangeSpellSlot |
| `[ES` | 45 | UpdateUserStats |
| `ERR` | 55 | ErrorMsg |
| `MUERT` | — | (use MultiMessage) |

---

## Checklist para migrar un sistema completo

- [ ] Listar TODOS los paquetes del sistema (client→server y server→client)
- [ ] Para cada paquete outbound: reemplazar `format!()` con `ByteQueue::write_*`
- [ ] Para cada paquete inbound: reemplazar `read_field()` con `ByteQueue::read_*`
- [ ] Verificar tipos: ¿el campo es Byte, Integer o Long? (ver PROTOCOL_13.3.md)
- [ ] Actualizar dispatcher en `handle_packet()` para usar `ClientPacketID::from_byte`
- [ ] Actualizar cliente Godot: PacketHandler.cs y InputHandler.cs
- [ ] Probar con el cliente conectado

---

*Referencia completa de paquetes: ver `PROTOCOL_13.3.md` en este mismo directorio.*

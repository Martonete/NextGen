# Data File Formats

## Overview

All game data files are loaded at server startup from the `server/` directory. Most use INI format (parsed by the custom INI parser that handles UTF-8, Latin-1, and UTF-16 LE/BE encodings).

## 1. Server Configuration (`server.ini`)

INI format with multiple sections:

```ini
[INIT]
Port=5028
MaxUsers=500
Version=13.4.0
StartMap=1
StartX=50
StartY=50
Encrypt=1
ExpMultiplier=1
CanCreateCharacters=1
AllowMultiLogins=0
IntervaloNpcAI=1300
IntervaloParalizado=8

[NOTICE]
Notice=Welcome to Argentum Nextgen!
```

**Key fields**: port, max_users, version, start_map/x/y, encryption toggle, EXP multiplier, NPC AI interval.

## 2. Objects Database (`dat/Obj.dat`)

UTF-16 LE encoded INI file. 1,664 object definitions.

```ini
[OBJ1]
Name=Espada corta
ObjType=1
GrhIndex=1234
WeaponAnim=5
MaxHit=10
MinHit=3
Valor=50
Newbie=1
```

**Key fields per object**:
| Field | Type | Description |
|-------|------|-------------|
| Name | string | Display name |
| ObjType | int | Object category (see ObjType enum) |
| GrhIndex | int | Client graphic index |
| WeaponAnim | int | Weapon animation ID |
| ShieldAnim | int | Shield animation ID |
| CascoAnim | int | Helmet animation ID |
| Ropaje | int | Body graphic when worn (armor/boat) |
| MaxHit/MinHit | int | Damage range (weapons) |
| MaxDef/MinDef | int | Defense range (armor) |
| Valor | int | Gold value |
| TipoPocion | int | Potion subtype (1-6) |
| Envenena | bool | Weapon poisons on hit |
| Refuerzo | int | Armor penetration |
| Newbie | bool | Can be used below level 12 |
| Intransferable | bool | Cannot be traded |
| Snd1/Snd3 | int | Usage/equip sound IDs |

### ObjType Enum
```
0=None, 1=Weapon, 2=Armor, 3=Tree, 4=Money, 5=Door, 6=Container,
7=Sign, 8=Key, 9=Forum, 10=Potion, 11=Reserved, 12=Instrument,
13=Anvil, 14=Forge, 15=Gem, 16=Flower, 17=Boat, 18=Arrow,
19=Empty, 20=Scroll, 21=Mineral, 22=Wood, 23=Teleport, 24=Furniture,
25=Tool, 26=Lure, 27=Helmet, 28=Ring, 29=Teleport2, 30=Shield,
31=Spell, 32=Food, 33=Drink, 34=Mount, 35=WrittenPaper, 36=Fish,
37=Quest, 38=CraftMaterial
```

## 3. Spells Database (`dat/Hechizos.dat`)

UTF-16 LE encoded INI file. 65 spell definitions.

```ini
[HECHIZO1]
Nombre=Curar heridas
ManaRequerido=20
Target=1
HechizeroMsg=Tus heridas se curan.
MinHP=15
MaxHP=25
FXgrh=14
WAV=23
```

**Key fields**: name, mana cost, target type (1=user, 2=NPC, 3=terrain), damage/heal ranges, effects (paralysis, poison, invisibility, summon NPC number), required skill level, class restrictions.

## 4. NPC Database (`dat/NPCs.dat` + `dat/NPCs-HOSTILES.dat`)

Two INI files, 396 NPC definitions combined.

```ini
[NPC1]
Name=Murcielago
NpcType=1
Body=100
Head=0
Movement=3
Hostile=1
MaxHP=50
MaxHit=8
MinHit=2
GiveEXP=30
GiveGLD=5
Attackable=1
Respawn=1
```

**Key fields**: name, type, body/head graphics, AI movement type, hostile flag, HP/hit/def stats, EXP/gold drops, attackable flag, respawn flag, spells, commerce inventory, alignment, vision range.

### NpcType Enum
```
0=Common, 1=Hostile, 2=Trainer, 3=Guard, 4=Merchant, 5=Banker,
6=Noble, 7=QuestGiver, 8=Reviver
```

## 5. Map Files

### Binary Map (`maps/Mapa<N>.map`)

Binary format, 100×100 tile grid:
```
[Header: 263 bytes]
[For each tile (row-major, 1-100 × 1-100)]:
  flags: i8          # bit 0 = blocked
  graphic[0]: i16    # Base terrain
  graphic[1]: i16    # Overlay
  graphic[2]: i16    # Object layer
  graphic[3]: i16    # Roof layer
  trigger: i16       # Trigger type (0-7)
  exit_map: i16      # Exit target map (0 = none)
  exit_x: i16        # Exit target X
  exit_y: i16        # Exit target Y
```

### Binary Info (`maps/Mapa<N>.inf`)

Per-tile overlay information:
```
[For each tile (row-major)]:
  npc_number: i16    # NPC to spawn (0 = none)
  obj_index: i16     # Object on ground (0 = none)
  obj_amount: i16    # Object stack amount
```

### Map Metadata (`maps/Mapa<N>.dat`)

INI format:
```ini
[Mapa<N>]
Name=Ciudad de Ullathorpe
MusicNum=3
Pk=0          # 0 = PvP enabled (INVERTED from intuition!)
BackUp=1      # Whether to persist ground items
Terreno=TIERRA
Zona=CAMPO
RestringirNavegar=0
NoEncriptarMP=0
```

## 6. Character Files (`charfile/<NAME>.chr`)

INI format, one file per character:

```ini
[INIT]
Name=CharName
Race=1
Gender=1
Class=1
Hogar=Ullathorpe
Head=2
Body=1
Heading=3
Level=5
Exp=1500

[STATS]
MaxHP=100
MinHP=85
MaxSTA=100
MinSTA=90
MaxMAN=50
MinMAN=30
MaxHIT=15
MinHIT=5
MaxAGU=100
MinAGU=80
MaxHAM=100
MinHAM=75
GLD=500
BANCO=1000

[ATRIBUTOS]
AT1=18  # Strength
AT2=20  # Agility
AT3=18  # Intelligence
AT4=16  # Charisma
AT5=18  # Constitution

[SKILLS]
SK1=45  # Through SK20

[FLAGS]
Muerto=0
Envenenado=0
Paralizado=0
Criminal=0
Hidden=0
Navegando=0
Privileges=0

[INVENTORY]
Obj1=350,1,1    # slot: obj_index,amount,equipped
Obj2=120,5,0

[BANCOINVENTORY]
Obj1=200,3      # slot: obj_index,amount

[HECHIZOS]
H1=3            # spell number in slot 1
H2=7

[REPUTATION]
Asesino=0
Bandido=0
Burguesia=5000
Ladron=0
Noble=3000
Plebe=0
Promedio=4000

[FACCIONES]
EjercitoReal=0
EjercitoCaos=0
CriminalesMatados=5
CiudadanosMatados=0
RealEnlistadas=0
CaosEnlistadas=0
```

## 7. Account Files (`Accounts/<NAME>.act`)

INI format:

```ini
[INIT]
Password=<hashed>
Email=user@example.com
PIN=1234
SecurityCode=ABC123
Banned=0
BanReason=
HD=<serial>
DateCreated=2024-01-15

[PERSONAJE1]
Name=CharName1

[PERSONAJE2]
Name=CharName2
```

## 8. Experience Table (`dat/Experiencia.dat`)

INI format, 50 levels:

```ini
[INIT]
Cant=50

[NIVEL2]
Exp=300

[NIVEL3]
Exp=900
```

Each entry is the cumulative EXP needed to reach that level.

## 9. Quest Database (`dat/Quests.dat`)

INI format with quest definitions:

```ini
[QUEST1]
Nombre=Caza de ratas
Descripcion=Elimina 10 ratas en las alcantarillas.
NivelMinimo=1
NpcIndex=50
KillNpc=15
KillAmount=10
RewardExp=500
RewardGold=100
RewardObj=350
RewardAmount=1
```

## 10. Guild Data (`guilds/guildsinfo.inf`)

INI format tracking all guilds:

```ini
[GUILD1]
Name=Los Defensores
Leader=PlayerName
Members=Player1,Player2,Player3
Alignment=0
BankGold=5000       # Note: guild bank is not active/implemented
```

## 11. Class Balance (`dat/ClassBonus.dat`)

Level-based class bonuses applied at levels 53, 56, 60:

```ini
[WARRIOR53]
MaxHP=+20
MaxHIT=+3

[MAGE56]
MaxMAN=+50
```

## 12. Text Codes (`client/Data/INIT/Textos.ao`)

Client-side text database with 983 entries:

```ini
[TEXTO1]
Mensaje=Bienvenido a Argentum Nextgen!
Font=1

[TEXTO3]
Mensaje=Estas muerto.
Font=4
```

Font types: 1=Info, 2=Fight, 3=Warning, 4=System, 5=Server, etc.

Server sends `||NNN` to reference these entries. Parameterized: `||60@name@exp`.

## 13. Ban Files

### `BanIps.dat`
One IP per line, plain text.

### `BanHDs.dat`
One HD serial per line, plain text.

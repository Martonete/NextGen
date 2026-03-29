// Centralized game constants — item IDs, object IDs, sound IDs, and other magic
// numbers that were previously duplicated across handler files.

// =====================================================================
// Tool object indices (VB6 constants)
// =====================================================================

/// Lumberjack axe (VB6: HACHA_LENADOR)
pub const HACHA_LENADOR: i32 = 127;
/// Elven lumberjack axe
pub const HACHA_LENA_ELFICA: i32 = 1005;
/// Newbie lumberjack axe
pub const HACHA_LENADOR_NEWBIE: i32 = 565;
/// Mining pick (VB6: PIQUETE_MINERO)
pub const PIQUETE_MINERO: i32 = 187;
/// Newbie mining pick
pub const PIQUETE_MINERO_NEWBIE: i32 = 566;
/// Blacksmith hammer (VB6: MARTILLO_HERRERO)
pub const MARTILLO_HERRERO: i32 = 389;
/// Newbie blacksmith hammer
pub const MARTILLO_HERRERO_NEWBIE: i32 = 567;
/// Carpenter saw (VB6: SERRUCHO_CARPINTERO)
pub const SERRUCHO_CARPINTERO: i32 = 198;
/// Newbie carpenter saw
pub const SERRUCHO_CARPINTERO_NEWBIE: i32 = 564;
/// Fishing rod (VB6: CANA_PESCA)
pub const CANA_PESCA: i32 = 543;
/// Newbie fishing rod
pub const CANA_PESCA_NEWBIE: i32 = 468;

// =====================================================================
// Weapon / equipment special objects
// =====================================================================

/// Viking Sword — enables DoGolpeCritico for Bandido (VB6: ESPADA_VIKINGA = 123)
pub const ESPADA_VIKINGA: i32 = 123;
/// Dragon Slayer sword — 1 damage to players, instakill dragons (VB6: EspadaMataDragonesIndex = 402)
pub const ESPADA_MATA_DRAGONES: i32 = 402;
/// Pickpocket gloves — enables steal and hand-immobilization (VB6: GUANTE_HURTO)
pub const GUANTE_HURTO: i32 = 873;

// =====================================================================
// Resource items
// =====================================================================

pub const LENA_OBJ: i32 = 58;
pub const PESCADO_OBJ: i32 = 139;
pub const HIERRO_CRUDO: i32 = 192;
pub const PLATA_CRUDA: i32 = 193;
pub const ORO_CRUDO: i32 = 194;
pub const LINGOTE_HIERRO: i32 = 386;
pub const LINGOTE_PLATA: i32 = 387;
pub const LINGOTE_ORO: i32 = 388;
/// Magic stones (carpentry material)
pub const PIEDRA_OBJ: i32 = 1225;
/// Lit campfire (VB6: Fogata)
pub const FOGATA_OBJ: i32 = 63;
/// Firewood for campfire
pub const LENA_FOGATA: i32 = 58;

// =====================================================================
// Character appearance defaults
// =====================================================================

/// Ghost/dead body GRH index
pub const DEAD_BODY_NEUTRAL: i32 = 8;
/// Ghost/dead head GRH index
pub const DEAD_HEAD_NEUTRAL: i32 = 500;
/// No weapon equipped animation
pub const NINGUN_ARMA: i32 = 2;
/// No shield equipped animation
pub const NINGUN_ESCUDO: i32 = 2;

// =====================================================================
// Sound IDs
// =====================================================================

pub const SND_TALAR: i32 = 13;
pub const SND_PESCAR: i32 = 14;
pub const SND_MINERO: i32 = 15;
pub const SND_HERRERO: i32 = 41;
pub const SND_CARPINTERO: i32 = 42;

// =====================================================================
// Combat constants
// =====================================================================

/// VB6: PartesCuerpo.bCabeza = 1
pub const BODY_PART_HEAD: i32 = 1;
/// VB6: PartesCuerpo.bTorso = 6
pub const BODY_PART_TORSO: i32 = 6;
/// VB6: PROB_ACUCHILLAR = 20 (20% chance for Pirate throat cut)
pub const PROB_ACUCHILLAR: i32 = 20;
/// VB6: DAÑO_ACUCHILLAR = 0.2 (20% of base damage)
pub const DANO_ACUCHILLAR: f64 = 0.2;

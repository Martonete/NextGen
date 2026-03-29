// Character persistence — PostgreSQL.

use sqlx::PgPool;

/// Minimal character data for account selection screen (ADDPJ packet).
#[derive(Debug, Clone)]
pub struct CharPreview {
    pub name: String,
    pub head: i32,
    pub body: i32,
    pub weapon: i32,
    pub shield: i32,
    pub helmet: i32,
    pub level: i32,
    pub class: String,
    pub dead: bool,
    pub race: String,
}

impl CharPreview {
    /// Format as comma-separated string for ADDPJ packet.
    pub fn to_addpj_data(&self) -> String {
        format!(
            "{},{},{},{},{},{},{},{},{}",
            self.head, self.body, self.weapon, self.shield, self.helmet,
            self.level, self.class,
            if self.dead { 1 } else { 0 },
            self.race,
        )
    }
}

/// Full character data loaded from DB.
#[derive(Debug, Clone)]
pub struct CharData {
    pub id: i32,
    pub account_id: i32,
    pub name: String,
    pub head: i32,
    pub body: i32,
    pub heading: i32,
    pub weapon: i32,
    pub shield: i32,
    pub helmet: i32,
    pub class: String,
    pub race: String,
    pub gender: i32,
    pub hogar: i32,
    pub level: i32,
    pub exp: i64,
    pub max_hp: i32,
    pub min_hp: i32,
    pub max_sta: i32,
    pub min_sta: i32,
    pub max_mana: i32,
    pub min_mana: i32,
    pub max_hit: i32,
    pub min_hit: i32,
    pub max_agua: i32,
    pub min_agua: i32,
    pub max_ham: i32,
    pub min_ham: i32,
    pub gold: i64,
    pub bank_gold: i64,
    pub skill_pts_libres: i32,
    pub attributes: [i32; 5],
    pub skills: [i32; 22],
    pub banned: bool,
    pub dead: bool,
    pub poisoned: bool,
    pub paralyzed: bool,
    pub hidden: bool,
    pub navigating: bool,
    pub barco_slot: i32,
    pub montado: bool,
    pub levitando: bool,
    pub montado_body: i32,
    pub map: i32,
    pub x: i32,
    pub y: i32,
    pub guild_index: i32,
    pub privileges: i32,
    pub inventory: Vec<(i32, i32, bool)>,
    pub weapon_eqp_slot: usize,
    pub armour_eqp_slot: usize,
    pub shield_eqp_slot: usize,
    pub helmet_eqp_slot: usize,
    pub municion_eqp_slot: usize,
    pub bank: Vec<(i32, i32)>,
    pub spells: [i32; 20],
    pub reputation: i32,
    pub criminal: bool,
    pub armada_real: bool,
    pub fuerzas_caos: bool,
    pub criminales_matados: i32,
    pub ciudadanos_matados: i32,
    pub recompensas_real: i32,
    pub recompensas_caos: i32,
    pub reenlistadas: bool,
    pub password: String,
    pub pet_count: i32,
    pub pet_types: Vec<i32>,
    pub description: String,
    // VB6 13.3 fields added for full parity
    pub exp_skills: [i32; 22],
    pub usuarios_matados: i32,
    pub npcs_muertos: i32,
    pub rep_asesino: i32,
    pub rep_bandido: i32,
    pub rep_burgues: i32,
    pub rep_ladrones: i32,
    pub rep_noble: i32,
    pub rep_plebe: i32,
    pub recibio_armadura_real: bool,
    pub recibio_armadura_caos: bool,
    pub recibio_exp_real: bool,
    pub recibio_exp_caos: bool,
    pub nivel_ingreso: i32,
    pub fecha_ingreso: String,
    pub matados_ingreso: i32,
    pub next_recompensa: i32,
    pub counter_pena: i32,
    pub skills_asignados: i32,
    pub last_map: i32,
    pub uptime: i64,
    pub mochila_eqp_slot: usize,
    pub anillo_eqp_slot: usize,
    pub pareja: String,
}

/// Load the obj_indices of equipped weapon/shield/helmet for a character.
/// Uses correlated subqueries — a single lightweight query, no full charfile load.
/// Returns (weapon_obj_idx, shield_obj_idx, helmet_obj_idx) — 0 if not equipped.
pub async fn load_equipped_obj_indices(pool: &PgPool, char_name: &str) -> (i32, i32, i32) {
    let row = sqlx::query_as::<_, (i32, i32, i32)>(
        "SELECT
            COALESCE(CASE WHEN c.weapon_eqp_slot > 0 THEN
                (SELECT ci.obj_index FROM character_inventory ci
                 WHERE ci.character_id = c.id AND ci.slot = c.weapon_eqp_slot - 1)
            END, 0),
            COALESCE(CASE WHEN c.shield_eqp_slot > 0 THEN
                (SELECT ci.obj_index FROM character_inventory ci
                 WHERE ci.character_id = c.id AND ci.slot = c.shield_eqp_slot - 1)
            END, 0),
            COALESCE(CASE WHEN c.helmet_eqp_slot > 0 THEN
                (SELECT ci.obj_index FROM character_inventory ci
                 WHERE ci.character_id = c.id AND ci.slot = c.helmet_eqp_slot - 1)
            END, 0)
         FROM characters c WHERE UPPER(c.name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_optional(pool)
    .await
    .unwrap_or(None)
    .unwrap_or((0, 0, 0));
    row
}

/// Check if a character exists.
pub async fn character_exists(pool: &PgPool, char_name: &str) -> bool {
    let result = sqlx::query_scalar::<_, i64>(
        "SELECT COUNT(*) FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_one(pool)
    .await;

    matches!(result, Ok(count) if count > 0)
}

/// Load minimal character data for account selection screen.
pub async fn load_char_preview(pool: &PgPool, char_name: &str) -> Result<CharPreview, String> {
    let row = sqlx::query_as::<_, (String, i32, i32, i32, i32, i32, i32, String, bool, String)>(
        "SELECT name, head, body, weapon, shield, helmet, level, class, dead, race
         FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_optional(pool)
    .await
    .map_err(|e| format!("DB error loading char preview: {}", e))?
    .ok_or_else(|| format!("Character '{}' not found", char_name))?;

    let (name, head, body, weapon, shield, helmet, level, class, dead, race) = row;
    Ok(CharPreview { name, head, body, weapon, shield, helmet, level, class, dead, race })
}

/// Helper: convert SQL INT[] to fixed-size Rust array.
fn sql_array_to_5(arr: &[i32]) -> [i32; 5] {
    let mut out = [0i32; 5];
    for (i, v) in arr.iter().enumerate().take(5) {
        out[i] = *v;
    }
    out
}

fn sql_array_to_22(arr: &[i32]) -> [i32; 22] {
    let mut out = [0i32; 22];
    for (i, v) in arr.iter().enumerate().take(22) {
        out[i] = *v;
    }
    out
}

fn sql_array_to_20(arr: &[i32]) -> [i32; 20] {
    let mut out = [0i32; 20];
    for (i, v) in arr.iter().enumerate().take(20) {
        out[i] = *v;
    }
    out
}

/// Load full character data.
pub async fn load_charfile(pool: &PgPool, char_name: &str) -> Result<CharData, String> {
    use sqlx::Row;

    // Single query for all character columns
    let row = sqlx::query(
        "SELECT id, account_id, name,
                head, body, heading, weapon, shield, helmet,
                class, race, gender, hogar,
                map, x, y,
                level, exp,
                max_hp, min_hp, max_mana, min_mana, max_sta, min_sta, max_hit, min_hit,
                max_agua, min_agua, max_ham, min_ham,
                gold, bank_gold, skill_pts_libres,
                attributes, skills, spells,
                banned, dead, poisoned, paralyzed, hidden, navigating, criminal,
                privileges, barco_slot, montado, levitando, montado_body,
                weapon_eqp_slot, armour_eqp_slot, shield_eqp_slot, helmet_eqp_slot, municion_eqp_slot,
                guild_index, reputation,
                armada_real, fuerzas_caos, criminales_matados, ciudadanos_matados,
                recompensas_real, recompensas_caos, reenlistadas,
                pet_count, pet_types,
                exp_skills,
                usuarios_matados, npcs_muertos,
                rep_asesino, rep_bandido, rep_burgues, rep_ladrones, rep_noble, rep_plebe,
                recibio_armadura_real, recibio_armadura_caos,
                recibio_exp_real, recibio_exp_caos,
                nivel_ingreso, fecha_ingreso, matados_ingreso, next_recompensa,
                counter_pena, skills_asignados, last_map, uptime,
                mochila_eqp_slot, anillo_eqp_slot, pareja,
                description
         FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_optional(pool)
    .await
    .map_err(|e| format!("DB error loading charfile: {}", e))?
    .ok_or_else(|| format!("Character '{}' not found", char_name))?;

    let id: i32 = row.get("id");
    let account_id: i32 = row.get("account_id");
    let name: String = row.get("name");
    let head: i32 = row.get("head");
    let body: i32 = row.get("body");
    let heading: i32 = row.get("heading");
    let weapon: i32 = row.get("weapon");
    let shield: i32 = row.get("shield");
    let helmet: i32 = row.get("helmet");
    let class: String = row.get("class");
    let race: String = row.get("race");
    let gender: i32 = row.get("gender");
    let hogar: i32 = row.get("hogar");
    let map: i32 = row.get("map");
    let x: i32 = row.get("x");
    let y: i32 = row.get("y");
    let level: i32 = row.get("level");
    let exp: i64 = row.get("exp");
    let max_hp: i32 = row.get("max_hp");
    let min_hp: i32 = row.get("min_hp");
    let max_mana: i32 = row.get("max_mana");
    let min_mana: i32 = row.get("min_mana");
    let max_sta: i32 = row.get("max_sta");
    let min_sta: i32 = row.get("min_sta");
    let max_hit: i32 = row.get("max_hit");
    let min_hit: i32 = row.get("min_hit");
    let max_agua: i32 = row.get("max_agua");
    let min_agua: i32 = row.get("min_agua");
    let max_ham: i32 = row.get("max_ham");
    let min_ham: i32 = row.get("min_ham");
    let gold: i64 = row.get("gold");
    let bank_gold: i64 = row.get("bank_gold");
    let skill_pts_libres: i32 = row.get("skill_pts_libres");
    let attributes_vec: Vec<i32> = row.get("attributes");
    let skills_vec: Vec<i32> = row.get("skills");
    let spells_vec: Vec<i32> = row.get("spells");
    let banned: bool = row.get("banned");
    let dead: bool = row.get("dead");
    let poisoned: bool = row.get("poisoned");
    let paralyzed: bool = row.get("paralyzed");
    let hidden: bool = row.get("hidden");
    let navigating: bool = row.get("navigating");
    let barco_slot: i32 = row.try_get("barco_slot").unwrap_or(0);
    let montado: bool = row.try_get("montado").unwrap_or(false);
    let levitando: bool = row.try_get("levitando").unwrap_or(false);
    let montado_body: i32 = row.try_get("montado_body").unwrap_or(0);
    let criminal: bool = row.get("criminal");
    let privileges: i32 = row.get("privileges");
    let weapon_eqp_slot: i32 = row.get("weapon_eqp_slot");
    let armour_eqp_slot: i32 = row.get("armour_eqp_slot");
    let shield_eqp_slot: i32 = row.get("shield_eqp_slot");
    let helmet_eqp_slot: i32 = row.get("helmet_eqp_slot");
    let municion_eqp_slot: i32 = row.get("municion_eqp_slot");
    let guild_index: i32 = row.get("guild_index");
    let reputation: i32 = row.get("reputation");
    let armada_real: bool = row.get("armada_real");
    let fuerzas_caos: bool = row.get("fuerzas_caos");
    let criminales_matados: i32 = row.get("criminales_matados");
    let ciudadanos_matados: i32 = row.get("ciudadanos_matados");
    let recompensas_real: i32 = row.get("recompensas_real");
    let recompensas_caos: i32 = row.get("recompensas_caos");
    let reenlistadas: bool = row.get("reenlistadas");
    let pet_count: i32 = row.try_get("pet_count").unwrap_or(0);
    let pet_types_str: String = row.try_get("pet_types").unwrap_or_default();
    let pet_types: Vec<i32> = if pet_types_str.is_empty() {
        Vec::new()
    } else {
        pet_types_str.split(',').filter_map(|s| s.trim().parse::<i32>().ok()).collect()
    };

    // New VB6 13.3 fields
    let exp_skills_vec: Vec<i32> = row.try_get("exp_skills").unwrap_or_default();
    let usuarios_matados: i32 = row.try_get("usuarios_matados").unwrap_or(0);
    let npcs_muertos: i32 = row.try_get("npcs_muertos").unwrap_or(0);
    let rep_asesino: i32 = row.try_get("rep_asesino").unwrap_or(0);
    let rep_bandido: i32 = row.try_get("rep_bandido").unwrap_or(0);
    let rep_burgues: i32 = row.try_get("rep_burgues").unwrap_or(0);
    let rep_ladrones: i32 = row.try_get("rep_ladrones").unwrap_or(0);
    let rep_noble: i32 = row.try_get("rep_noble").unwrap_or(0);
    let rep_plebe: i32 = row.try_get("rep_plebe").unwrap_or(0);
    let recibio_armadura_real: bool = row.try_get("recibio_armadura_real").unwrap_or(false);
    let recibio_armadura_caos: bool = row.try_get("recibio_armadura_caos").unwrap_or(false);
    let recibio_exp_real: bool = row.try_get("recibio_exp_real").unwrap_or(false);
    let recibio_exp_caos: bool = row.try_get("recibio_exp_caos").unwrap_or(false);
    let nivel_ingreso: i32 = row.try_get("nivel_ingreso").unwrap_or(0);
    let fecha_ingreso: String = row.try_get("fecha_ingreso").unwrap_or_default();
    let matados_ingreso: i32 = row.try_get("matados_ingreso").unwrap_or(0);
    let next_recompensa: i32 = row.try_get("next_recompensa").unwrap_or(0);
    let counter_pena: i32 = row.try_get("counter_pena").unwrap_or(0);
    let skills_asignados: i32 = row.try_get("skills_asignados").unwrap_or(0);
    let last_map: i32 = row.try_get("last_map").unwrap_or(0);
    let uptime: i64 = row.try_get("uptime").unwrap_or(0);
    let mochila_eqp_slot: i32 = row.try_get("mochila_eqp_slot").unwrap_or(0);
    let anillo_eqp_slot: i32 = row.try_get("anillo_eqp_slot").unwrap_or(0);
    let pareja: String = row.try_get("pareja").unwrap_or_default();
    let description: String = row.try_get("description").unwrap_or_default();

    // Load inventory
    let inv_rows: Vec<(i16, i32, i32, bool)> = sqlx::query_as(
        "SELECT slot, obj_index, amount, equipped FROM character_inventory
         WHERE character_id = $1 ORDER BY slot"
    )
    .bind(id)
    .fetch_all(pool)
    .await
    .map_err(|e| format!("DB error loading inventory: {}", e))?;

    let mut inventory = vec![(0i32, 0i32, false); 30];
    for (slot, obj_idx, amount, equipped) in &inv_rows {
        let s = *slot as usize;
        if s < 30 {
            inventory[s] = (*obj_idx, *amount, *equipped);
        }
    }

    // Load bank
    let bank_rows: Vec<(i16, i32, i32)> = sqlx::query_as(
        "SELECT slot, obj_index, amount FROM character_bank
         WHERE character_id = $1 ORDER BY slot"
    )
    .bind(id)
    .fetch_all(pool)
    .await
    .map_err(|e| format!("DB error loading bank: {}", e))?;

    let mut bank = vec![(0i32, 0i32); 40];
    for (slot, obj_idx, amount) in &bank_rows {
        let s = *slot as usize;
        if s < 40 {
            bank[s] = (*obj_idx, *amount);
        }
    }

    // Get security_code from account (used as "password" for VB6 compat)
    let password: String = sqlx::query_scalar(
        "SELECT security_code FROM accounts WHERE id = $1"
    )
    .bind(account_id)
    .fetch_optional(pool)
    .await
    .map_err(|e| format!("DB error: {}", e))?
    .unwrap_or_default();

    Ok(CharData {
        id, account_id, name,
        head, body, heading, weapon, shield, helmet,
        class, race, gender, hogar,
        level, exp, max_hp, min_hp, max_sta, min_sta, max_mana, min_mana,
        max_hit, min_hit, max_agua, min_agua, max_ham, min_ham,
        gold, bank_gold, skill_pts_libres,
        attributes: sql_array_to_5(&attributes_vec),
        skills: sql_array_to_22(&skills_vec),
        banned, dead, poisoned, paralyzed, hidden, navigating, barco_slot,
        montado, levitando, montado_body,
        map, x, y, guild_index, privileges,
        inventory,
        weapon_eqp_slot: weapon_eqp_slot as usize,
        armour_eqp_slot: armour_eqp_slot as usize,
        shield_eqp_slot: shield_eqp_slot as usize,
        helmet_eqp_slot: helmet_eqp_slot as usize,
        municion_eqp_slot: municion_eqp_slot as usize,
        bank,
        spells: sql_array_to_20(&spells_vec),
        reputation, criminal,
        armada_real, fuerzas_caos, criminales_matados, ciudadanos_matados,
        recompensas_real, recompensas_caos, reenlistadas,
        password,
        pet_count,
        pet_types,
        exp_skills: sql_array_to_22(&exp_skills_vec),
        usuarios_matados,
        npcs_muertos,
        rep_asesino, rep_bandido, rep_burgues, rep_ladrones, rep_noble, rep_plebe,
        recibio_armadura_real, recibio_armadura_caos,
        recibio_exp_real, recibio_exp_caos,
        nivel_ingreso, fecha_ingreso, matados_ingreso, next_recompensa,
        counter_pena, skills_asignados, last_map, uptime,
        mochila_eqp_slot: mochila_eqp_slot as usize,
        anillo_eqp_slot: anillo_eqp_slot as usize,
        pareja,
        description,
    })
}

/// Create a new character with starter stats (VB6 13.3 ConnectNewUser exact).
pub async fn create_charfile(
    pool: &PgPool,
    account_id: i32,
    name: &str,
    race: &str,
    gender: i32,
    class: &str,
    hogar: i32,
    head: i32,
    attributes: [i32; 5],
    start_map: i32,
    start_x: i32,
    start_y: i32,
    balance: &crate::data::balance::BalanceData,
) -> Result<i32, String> {
    if character_exists(pool, name).await {
        return Err("El nombre del personaje ya esta siendo utilizado.".into());
    }

    let body = starter_body(race, gender);
    let head_val = if head > 0 { head } else { starter_head(race, gender) };

    // VB6: Apply MODRAZA attribute bonuses from Balance.dat
    let mut attrs = attributes;
    let race_mods = balance.race_modifiers(race);
    attrs[0] += race_mods.fuerza;       // Fuerza
    attrs[1] += race_mods.agilidad;     // Agilidad
    attrs[2] += race_mods.inteligencia; // Inteligencia
    attrs[3] += race_mods.carisma;      // Carisma
    attrs[4] += race_mods.constitucion; // Constitucion
    for a in attrs.iter_mut() {
        *a = (*a).clamp(1, 25);
    }

    // VB6 ConnectNewUser: HP = 15 + Random(1, CON/3)
    let con_div3 = (attrs[4] / 3).max(1);
    let hp_roll = random_range(1, con_div3);
    let base_hp = 15 + hp_roll;

    // VB6 ConnectNewUser: STA = 20 * Random(2, AGI/6)
    let agi_div6 = (attrs[1] / 6).max(2);
    let sta_roll = random_range(1, agi_div6).max(2);
    let base_sta = 20 * sta_roll;

    // VB6 ConnectNewUser: Mana by class
    let class_upper = class.to_uppercase();
    let base_mana = match class_upper.as_str() {
        "MAGO" => attrs[2] * 3, // INT * 3
        "CLERIGO" | "DRUIDA" | "BARDO" | "ASESINO" | "BANDIDO" => 50,
        _ => 0, // Guerrero, Cazador, Ladron, Paladin, Trabajador, Pirata
    };

    // VB6: All skills start at 0 with 10 free skill points
    let skills = [0i32; 22];

    // VB6: Starting spells
    let mut spells = [0i32; 20];
    match class_upper.as_str() {
        "MAGO" | "CLERIGO" | "BARDO" | "ASESINO" => {
            spells[0] = 2; // Magia Misil
        }
        "DRUIDA" => {
            spells[0] = 2;  // Magia Misil
            spells[1] = 46; // Druida extra spell
        }
        _ => {}
    }

    let slot: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM characters WHERE account_id = $1"
    )
    .bind(account_id)
    .fetch_one(pool)
    .await
    .map_err(|e| format!("DB error: {}", e))?;

    // VB6: MaxHIT=2, MinHIT=1
    let char_id: (i32,) = sqlx::query_as(
        "INSERT INTO characters (
            account_id, name, slot, head, body, heading, weapon, shield, helmet,
            class, race, gender, hogar, map, x, y,
            level, exp, max_hp, min_hp, max_mana, min_mana, max_sta, min_sta,
            max_hit, min_hit, max_agua, min_agua, max_ham, min_ham,
            gold, bank_gold, skill_pts_libres,
            attributes, skills, spells,
            privileges
        ) VALUES (
            $1, $2, $3, $4, $5, 0, 0, 0, 0,
            $6, $7, $8, $9, $10, $11, $12,
            1, 0, $13, $13, $14, $14, $15, $15,
            2, 1, 100, 100, 100, 100,
            0, 0, 10,
            $16, $17, $18,
            0
        ) RETURNING id"
    )
    .bind(account_id)
    .bind(name)
    .bind(slot as i16)
    .bind(head_val)
    .bind(body)
    .bind(class)
    .bind(race)
    .bind(gender)
    .bind(hogar)
    .bind(start_map)
    .bind(start_x)
    .bind(start_y)
    .bind(base_hp)
    .bind(base_mana)
    .bind(base_sta)
    .bind(&attrs[..])
    .bind(&skills[..])
    .bind(&spells[..])
    .fetch_one(pool)
    .await
    .map_err(|e| format!("DB error creating character: {}", e))?;

    let cid = char_id.0;

    // VB6 ConnectNewUser: Race-specific armor
    let race_armor = match race.to_uppercase().as_str() {
        "HUMANO" => 463,
        "ELFO" => 464,
        "ELFO OSCURO" => 465,
        "ENANO" | "GNOMO" => 466,
        _ => 463,
    };

    // VB6 ConnectNewUser: Class-specific starter items
    let mut items: Vec<(i16, i32, i32, bool)> = Vec::new();
    let mut slot_idx: i16 = 0;

    // Slot 0: Food (Manzanas=467, 100)
    items.push((slot_idx, 467, 100, false)); slot_idx += 1;
    // Slot 1: Drink (Jugos=468, 100)
    items.push((slot_idx, 468, 100, false)); slot_idx += 1;

    // Slot 2: Weapon (class-specific)
    match class_upper.as_str() {
        "CAZADOR" => {
            items.push((slot_idx, 859, 1, true)); slot_idx += 1; // Arco (bow)
        }
        "TRABAJADOR" => {
            // Random tool: 561-565
            let tool = random_range(561, 565);
            items.push((slot_idx, tool, 1, true)); slot_idx += 1;
        }
        _ => {
            items.push((slot_idx, 460, 1, true)); slot_idx += 1; // Daga (dagger)
        }
    }
    let weapon_slot = slot_idx; // 1-based: slot_idx (already incremented = 3)

    // Slot 3: Armor (race-specific, equipped)
    items.push((slot_idx, race_armor, 1, true)); slot_idx += 1;
    let armor_slot = slot_idx; // 1-based: 4

    // Slot 4: Red potion (PociónRoja=857, 200)
    items.push((slot_idx, 857, 200, false)); slot_idx += 1;

    // Slot 5: Blue potion if caster or Paladin (PociónAzul=856, 200)
    let has_mana = base_mana > 0 || class_upper == "PALADIN";
    if has_mana {
        items.push((slot_idx, 856, 200, false)); slot_idx += 1;
    } else {
        // Non-casters: Yellow potion (855, 100) + Green potion (858, 50)
        items.push((slot_idx, 855, 100, false)); slot_idx += 1;
        items.push((slot_idx, 858, 50, false)); slot_idx += 1;
    }

    // Cazador: Arrows (Flechas=860, 150, equipped)
    if class_upper == "CAZADOR" {
        items.push((slot_idx, 860, 150, true)); slot_idx += 1;
    }

    for (slot, obj_idx, amount, equipped) in &items {
        sqlx::query(
            "INSERT INTO character_inventory (character_id, slot, obj_index, amount, equipped)
             VALUES ($1, $2, $3, $4, $5)"
        )
        .bind(cid)
        .bind(slot)
        .bind(obj_idx)
        .bind(amount)
        .bind(equipped)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error inserting inventory: {}", e))?;
    }

    // Set equipment slots (1-indexed VB6 style: weapon=3, armor=4)
    let municion_slot = if class_upper == "CAZADOR" { slot_idx as i32 } else { 0 };
    sqlx::query(
        "UPDATE characters SET weapon_eqp_slot = $2, armour_eqp_slot = $3, municion_eqp_slot = $4 WHERE id = $1"
    )
    .bind(cid)
    .bind(weapon_slot as i32)
    .bind(armor_slot as i32)
    .bind(municion_slot)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error setting equip slots: {}", e))?;

    Ok(cid)
}

fn random_range(min: i32, max: i32) -> i32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    if min >= max { return min; }
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u64;
    let val = seed.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
    let range = (max - min + 1) as u64;
    min + (val % range) as i32
}

/// Data needed to save a character (extracted from UserState).
pub struct CharSaveData {
    pub head: i32,
    pub body: i32,
    pub heading: i32,
    pub weapon: i32,
    pub shield: i32,
    pub helmet: i32,
    pub map: i32,
    pub x: i32,
    pub y: i32,
    pub level: i32,
    pub exp: i64,
    pub max_hp: i32,
    pub min_hp: i32,
    pub max_sta: i32,
    pub min_sta: i32,
    pub max_mana: i32,
    pub min_mana: i32,
    pub max_hit: i32,
    pub min_hit: i32,
    pub max_agua: i32,
    pub min_agua: i32,
    pub max_ham: i32,
    pub min_ham: i32,
    pub gold: i64,
    pub bank_gold: i64,
    pub attributes: [i32; 5],
    pub skills: [i32; 22],
    pub dead: bool,
    pub poisoned: bool,
    pub paralyzed: bool,
    pub criminal: bool,
    pub hidden: bool,
    pub navigating: bool,
    pub barco_slot: usize,
    pub montado: bool,
    pub levitando: bool,
    pub montado_body: i32,
    pub privileges: i32,
    pub spells: [i32; 20],
    pub inventory: Vec<(i32, i32, bool)>,
    pub bank: Vec<(i32, i32)>,
    pub weapon_eqp_slot: usize,
    pub armour_eqp_slot: usize,
    pub shield_eqp_slot: usize,
    pub helmet_eqp_slot: usize,
    pub municion_eqp_slot: usize,
    pub reputation: i32,
    pub guild_index: i32,
    pub criminales_matados: i32,
    pub ciudadanos_matados: i32,
    pub ejercito_real: bool,
    pub ejercito_caos: bool,
    pub skill_pts_libres: i32,
    pub recompensas_real: i32,
    pub recompensas_caos: i32,
    pub reenlistadas: bool,
    pub description: String,
    pub pet_count: i32,
    pub pet_types: Vec<i32>,
    // VB6 13.3 fields
    pub exp_skills: [i32; 22],
    pub usuarios_matados: i32,
    pub npcs_muertos: i32,
    pub rep_asesino: i32,
    pub rep_bandido: i32,
    pub rep_burgues: i32,
    pub rep_ladrones: i32,
    pub rep_noble: i32,
    pub rep_plebe: i32,
    pub recibio_armadura_real: bool,
    pub recibio_armadura_caos: bool,
    pub recibio_exp_real: bool,
    pub recibio_exp_caos: bool,
    pub nivel_ingreso: i32,
    pub fecha_ingreso: String,
    pub matados_ingreso: i32,
    pub next_recompensa: i32,
    pub counter_pena: i32,
    pub skills_asignados: i32,
    pub last_map: i32,
    pub uptime: i64,
    pub mochila_eqp_slot: usize,
    pub anillo_eqp_slot: usize,
    pub pareja: String,
}

/// Save full character state back to DB (called on disconnect / auto-save).
/// Wrapped in an atomic transaction — either everything saves or nothing does.
/// Inventory and bank use multi-row batch upserts (71 → 3 queries).
pub async fn save_charfile(pool: &PgPool, char_name: &str, data: &CharSaveData) -> Result<(), String> {
    // Begin transaction — all-or-nothing save
    let mut tx = pool.begin().await
        .map_err(|e| format!("DB error starting transaction: {}", e))?;

    // Get character ID
    let char_id: i32 = sqlx::query_scalar(
        "SELECT id FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_optional(&mut *tx)
    .await
    .map_err(|e| format!("DB error: {}", e))?
    .ok_or_else(|| format!("Character '{}' not found for save", char_name))?;

    // Update main character record
    sqlx::query(
        "UPDATE characters SET
            head = $2, body = $3, heading = $4, weapon = $5, shield = $6, helmet = $7,
            map = $8, x = $9, y = $10,
            level = $11, exp = $12,
            max_hp = $13, min_hp = $14, max_sta = $15, min_sta = $16,
            max_mana = $17, min_mana = $18, max_hit = $19, min_hit = $20,
            max_agua = $21, min_agua = $22, max_ham = $23, min_ham = $24,
            gold = $25, bank_gold = $26, skill_pts_libres = $27,
            attributes = $28, skills = $29, spells = $30,
            dead = $31, poisoned = $32, paralyzed = $33, criminal = $34,
            hidden = $35, navigating = $36, privileges = $37,
            weapon_eqp_slot = $38, armour_eqp_slot = $39,
            shield_eqp_slot = $40, helmet_eqp_slot = $41, municion_eqp_slot = $42,
            reputation = $43, guild_index = $44,
            criminales_matados = $45, ciudadanos_matados = $46,
            armada_real = $47, fuerzas_caos = $48,
            barco_slot = $49,
            montado = $50, levitando = $51, montado_body = $52,
            recompensas_real = $53, recompensas_caos = $54, reenlistadas = $55,
            description = $56,
            pet_count = $57, pet_types = $58,
            exp_skills = $59,
            usuarios_matados = $60, npcs_muertos = $61,
            rep_asesino = $62, rep_bandido = $63, rep_burgues = $64,
            rep_ladrones = $65, rep_noble = $66, rep_plebe = $67,
            recibio_armadura_real = $68, recibio_armadura_caos = $69,
            recibio_exp_real = $70, recibio_exp_caos = $71,
            nivel_ingreso = $72, fecha_ingreso = $73,
            matados_ingreso = $74, next_recompensa = $75,
            counter_pena = $76, skills_asignados = $77,
            last_map = $78, uptime = $79,
            mochila_eqp_slot = $80, anillo_eqp_slot = $81,
            pareja = $82,
            logged = FALSE, updated_at = NOW()
         WHERE id = $1"
    )
    .bind(char_id)
    .bind(data.head).bind(data.body).bind(data.heading)
    .bind(data.weapon).bind(data.shield).bind(data.helmet)
    .bind(data.map).bind(data.x).bind(data.y)
    .bind(data.level).bind(data.exp)
    .bind(data.max_hp).bind(data.min_hp).bind(data.max_sta).bind(data.min_sta)
    .bind(data.max_mana).bind(data.min_mana).bind(data.max_hit).bind(data.min_hit)
    .bind(data.max_agua).bind(data.min_agua).bind(data.max_ham).bind(data.min_ham)
    .bind(data.gold).bind(data.bank_gold).bind(data.skill_pts_libres)
    .bind(&data.attributes[..]).bind(&data.skills[..]).bind(&data.spells[..])
    .bind(data.dead).bind(data.poisoned).bind(data.paralyzed).bind(data.criminal)
    .bind(data.hidden).bind(data.navigating).bind(data.privileges)
    .bind(data.weapon_eqp_slot as i32).bind(data.armour_eqp_slot as i32)
    .bind(data.shield_eqp_slot as i32).bind(data.helmet_eqp_slot as i32)
    .bind(data.municion_eqp_slot as i32)
    .bind(data.reputation).bind(data.guild_index)
    .bind(data.criminales_matados).bind(data.ciudadanos_matados)
    .bind(data.ejercito_real).bind(data.ejercito_caos)
    .bind(data.barco_slot as i32)
    .bind(data.montado).bind(data.levitando).bind(data.montado_body)
    .bind(data.recompensas_real).bind(data.recompensas_caos).bind(data.reenlistadas)
    .bind(&data.description)
    .bind(data.pet_count)
    .bind(&data.pet_types.iter().map(|t| t.to_string()).collect::<Vec<_>>().join(","))
    .bind(&data.exp_skills[..])
    .bind(data.usuarios_matados).bind(data.npcs_muertos)
    .bind(data.rep_asesino).bind(data.rep_bandido).bind(data.rep_burgues)
    .bind(data.rep_ladrones).bind(data.rep_noble).bind(data.rep_plebe)
    .bind(data.recibio_armadura_real).bind(data.recibio_armadura_caos)
    .bind(data.recibio_exp_real).bind(data.recibio_exp_caos)
    .bind(data.nivel_ingreso).bind(&data.fecha_ingreso)
    .bind(data.matados_ingreso).bind(data.next_recompensa)
    .bind(data.counter_pena).bind(data.skills_asignados)
    .bind(data.last_map).bind(data.uptime)
    .bind(data.mochila_eqp_slot as i32).bind(data.anillo_eqp_slot as i32)
    .bind(&data.pareja)
    .execute(&mut *tx)
    .await
    .map_err(|e| format!("DB error saving character: {}", e))?;

    // Batch upsert inventory (up to 30 slots in 1 query)
    {
        let count = data.inventory.len().min(30);
        if count > 0 {
            // Build multi-row VALUES: ($1, 0, obj, amt, eq), ($1, 1, obj, amt, eq), ...
            let mut sql = String::from(
                "INSERT INTO character_inventory (character_id, slot, obj_index, amount, equipped) VALUES "
            );
            let mut param_idx = 2u32; // $1 = char_id
            for i in 0..count {
                if i > 0 { sql.push_str(", "); }
                sql.push_str(&format!(
                    "($1, ${}, ${}, ${}, ${})",
                    param_idx, param_idx + 1, param_idx + 2, param_idx + 3
                ));
                param_idx += 4;
            }
            sql.push_str(
                " ON CONFLICT (character_id, slot) DO UPDATE SET \
                 obj_index = EXCLUDED.obj_index, amount = EXCLUDED.amount, equipped = EXCLUDED.equipped"
            );

            let mut query = sqlx::query(&sql).bind(char_id);
            for i in 0..count {
                let (obj_idx, amount, equipped) = data.inventory[i];
                query = query.bind(i as i16).bind(obj_idx).bind(amount).bind(equipped);
            }
            query.execute(&mut *tx).await
                .map_err(|e| format!("DB error batch-saving inventory: {}", e))?;
        }
    }

    // Batch upsert bank (up to 40 slots in 1 query)
    {
        let count = data.bank.len().min(40);
        if count > 0 {
            let mut sql = String::from(
                "INSERT INTO character_bank (character_id, slot, obj_index, amount) VALUES "
            );
            let mut param_idx = 2u32; // $1 = char_id
            for i in 0..count {
                if i > 0 { sql.push_str(", "); }
                sql.push_str(&format!(
                    "($1, ${}, ${}, ${})",
                    param_idx, param_idx + 1, param_idx + 2
                ));
                param_idx += 3;
            }
            sql.push_str(
                " ON CONFLICT (character_id, slot) DO UPDATE SET \
                 obj_index = EXCLUDED.obj_index, amount = EXCLUDED.amount"
            );

            let mut query = sqlx::query(&sql).bind(char_id);
            for i in 0..count {
                let (obj_idx, amount) = data.bank[i];
                query = query.bind(i as i16).bind(obj_idx).bind(amount);
            }
            query.execute(&mut *tx).await
                .map_err(|e| format!("DB error batch-saving bank: {}", e))?;
        }
    }

    // Commit transaction — atomic save complete
    tx.commit().await
        .map_err(|e| format!("DB error committing save: {}", e))?;

    Ok(())
}

/// Delete a character.
pub async fn delete_charfile(pool: &PgPool, char_name: &str) -> Result<(), String> {
    sqlx::query("DELETE FROM characters WHERE UPPER(name) = UPPER($1)")
        .bind(char_name)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error deleting character: {}", e))?;
    Ok(())
}

/// Set the logged flag.
pub async fn set_logged_flag(pool: &PgPool, char_name: &str, logged: bool) -> Result<(), String> {
    sqlx::query("UPDATE characters SET logged = $1 WHERE UPPER(name) = UPPER($2)")
        .bind(logged)
        .bind(char_name)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error setting logged flag: {}", e))?;
    Ok(())
}

/// Get character ID by name (internal helper).
async fn get_char_id(pool: &PgPool, char_name: &str) -> Result<i32, String> {
    sqlx::query_scalar("SELECT id FROM characters WHERE UPPER(name) = UPPER($1)")
        .bind(char_name)
        .fetch_optional(pool)
        .await
        .map_err(|e| format!("DB error: {}", e))?
        .ok_or_else(|| "Character not found".to_string())
}

/// Update only the guild_index column for a character (online or offline).
pub async fn update_guild_index(pool: &PgPool, char_name: &str, guild_index: i32) -> Result<(), String> {
    sqlx::query("UPDATE characters SET guild_index = $1 WHERE UPPER(name) = UPPER($2)")
        .bind(guild_index)
        .bind(char_name)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error: {}", e))?;
    Ok(())
}

/// Set banned flag for a character (online or offline).
pub async fn set_char_banned(pool: &PgPool, char_name: &str, banned: bool) -> Result<(), String> {
    sqlx::query("UPDATE characters SET banned = $1 WHERE UPPER(name) = UPPER($2)")
        .bind(banned)
        .bind(char_name)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error: {}", e))?;
    Ok(())
}

/// Add a penalty to a character.
pub async fn add_penalty(pool: &PgPool, char_name: &str, text: &str) -> Result<(), String> {
    let char_id = get_char_id(pool, char_name).await?;
    sqlx::query(
        "INSERT INTO character_penalties (character_id, penalty_text) VALUES ($1, $2)"
    )
    .bind(char_id)
    .bind(text)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error: {}", e))?;
    Ok(())
}

/// Clear all penalties for a character.
pub async fn clear_penalties(pool: &PgPool, char_name: &str) -> Result<(), String> {
    let char_id = get_char_id(pool, char_name).await?;
    sqlx::query("DELETE FROM character_penalties WHERE character_id = $1")
        .bind(char_id)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error: {}", e))?;
    Ok(())
}

/// Load penalties for a character.
pub async fn load_penalties(pool: &PgPool, char_name: &str) -> Vec<String> {
    let char_id = match get_char_id(pool, char_name).await {
        Ok(id) => id,
        Err(_) => return Vec::new(),
    };
    let rows: Vec<(String,)> = sqlx::query_as(
        "SELECT penalty_text FROM character_penalties WHERE character_id = $1 ORDER BY id"
    )
    .bind(char_id)
    .fetch_all(pool)
    .await
    .unwrap_or_default();
    rows.into_iter().map(|(t,)| t).collect()
}

// --- Starter data helpers (same as data/charfile.rs) ---

fn starter_body(race: &str, gender: i32) -> i32 {
    let race_lower = race.to_lowercase();
    match (race_lower.as_str(), gender) {
        ("humano", 1) | ("elfo", 1) | ("elfo oscuro", 1) => 1,
        ("humano", 2) | ("elfo", 2) | ("elfo oscuro", 2) => 2,
        ("enano", 1) | ("gnomo", 1) => 53,
        ("enano", 2) | ("gnomo", 2) => 54,
        _ => 1,
    }
}

fn starter_head(race: &str, gender: i32) -> i32 {
    let race_lower = race.to_lowercase();
    match (race_lower.as_str(), gender) {
        ("humano", 1) => 1,
        ("humano", 2) => 70,
        ("elfo", 1) => 101,
        ("elfo", 2) => 170,
        ("elfo oscuro", 1) => 201,
        ("elfo oscuro", 2) => 270,
        ("enano", 1) => 301,
        ("enano", 2) => 370,
        ("gnomo", 1) => 401,
        ("gnomo", 2) => 470,
        _ => 1,
    }
}

// starter_stats removed — VB6 13.3 calculates HP/Mana/Sta from attributes inline

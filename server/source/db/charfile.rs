// Character persistence — PostgreSQL.
//
// Replaces data/charfile.rs (INI file I/O).

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
    pub questeando: bool,
    pub quest_num: i32,
    pub quest_kills: i32,
    pub quests_completed: i32,
    pub puntos_donacion: i64,
    pub puntos_torneo: i64,
    pub ts_points: i64,
    // password field kept for VB6 compat (CodeX from account)
    pub password: String,
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
                questeando, quest_num, quest_kills, quests_completed,
                puntos_donacion, puntos_torneo, ts_points
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
    let questeando: bool = row.get("questeando");
    let quest_num: i32 = row.get("quest_num");
    let quest_kills: i32 = row.get("quest_kills");
    let quests_completed: i32 = row.get("quests_completed");
    let puntos_donacion: i64 = row.get("puntos_donacion");
    let puntos_torneo: i64 = row.get("puntos_torneo");
    let ts_points: i64 = row.get("ts_points");

    // Load inventory
    let inv_rows: Vec<(i16, i32, i32, bool)> = sqlx::query_as(
        "SELECT slot, obj_index, amount, equipped FROM character_inventory
         WHERE character_id = $1 ORDER BY slot"
    )
    .bind(id)
    .fetch_all(pool)
    .await
    .map_err(|e| format!("DB error loading inventory: {}", e))?;

    let mut inventory = vec![(0i32, 0i32, false); 25];
    for (slot, obj_idx, amount, equipped) in &inv_rows {
        let s = *slot as usize;
        if s < 25 {
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
        questeando, quest_num, quest_kills, quests_completed,
        puntos_donacion, puntos_torneo, ts_points,
        password,
    })
}

/// Create a new character with starter stats.
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
) -> Result<i32, String> {
    // Check uniqueness
    if character_exists(pool, name).await {
        return Err("El nombre del personaje ya esta siendo utilizado.".into());
    }

    // Determine starter body/head based on race + gender
    let body = starter_body(race, gender);
    let head_val = if head > 0 { head } else { starter_head(race, gender) };
    let (base_hp, base_mana, base_sta) = starter_stats(class);

    // Apply race attribute bonuses
    let mut attrs = attributes;
    let race_upper = race.to_uppercase();
    match race_upper.as_str() {
        "HUMANO" => { attrs[0] += 2; attrs[4] += 2; attrs[3] += 3; }
        "ELFO" => { attrs[0] -= 1; attrs[1] += 2; attrs[2] += 2; attrs[3] += 2; }
        "ELFO OSCURO" => { attrs[0] += 1; attrs[1] += 1; attrs[2] += 2; attrs[3] += 1; attrs[4] += 1; }
        "ENANO" => { attrs[0] += 3; attrs[4] += 4; attrs[2] -= 2; attrs[1] -= 1; attrs[3] -= 1; }
        "GNOMO" => { attrs[0] -= 4; attrs[2] += 3; attrs[1] += 3; attrs[3] += 1; attrs[4] -= 1; }
        _ => {}
    }
    for a in attrs.iter_mut() {
        *a = (*a).clamp(1, 25);
    }

    // All skills start at 100
    let skills = [100i32; 22];

    // Starting spell (casters get Magia Misil = spell 2)
    let class_upper = class.to_uppercase();
    let starting_spell = match class_upper.as_str() {
        "MAGO" | "CLERIGO" | "DRUIDA" | "BARDO" | "BRUJO" => 2,
        _ => 0,
    };
    let mut spells = [0i32; 20];
    spells[0] = starting_spell;

    // Count current characters for slot assignment
    let slot: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM characters WHERE account_id = $1"
    )
    .bind(account_id)
    .fetch_one(pool)
    .await
    .map_err(|e| format!("DB error: {}", e))?;

    // Insert character
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
            1, 1, 100, 100, 100, 100,
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

    // Race-specific armor
    let race_armor = match race_upper.as_str() {
        "HUMANO" => 463,
        "ELFO" => 464,
        "ELFO OSCURO" => 465,
        "ENANO" | "GNOMO" => 466,
        _ => 463,
    };

    // Insert starter inventory (7 items)
    let starter_items: [(i16, i32, i32, bool); 7] = [
        (0, 467, 100, false),   // Food
        (1, 468, 100, false),   // Drink
        (2, 460, 1, true),      // Weapon (equipped)
        (3, race_armor, 1, true), // Armor (equipped)
        (4, 461, 150, false),   // Arrows
        (5, 462, 150, false),   // Potions
        (6, 1491, 150, false),  // Misc
    ];

    for (slot, obj_idx, amount, equipped) in &starter_items {
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

    // Set equipment slots (weapon=slot 2+1=3, armor=slot 3+1=4 — 1-indexed in VB6)
    sqlx::query(
        "UPDATE characters SET weapon_eqp_slot = 3, armour_eqp_slot = 4 WHERE id = $1"
    )
    .bind(cid)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error setting equip slots: {}", e))?;

    Ok(cid)
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
    pub puntos_donacion: i64,
    pub puntos_torneo: i64,
    pub ts_points: i64,
    pub recompensas_real: i32,
    pub recompensas_caos: i32,
    pub reenlistadas: bool,
    pub questeando: bool,
    pub quest_num: i32,
    pub quest_kills: i32,
    pub quests_completed: i32,
    pub description: String,
}

/// Save full character state back to DB (called on disconnect / auto-save).
pub async fn save_charfile(pool: &PgPool, char_name: &str, data: &CharSaveData) -> Result<(), String> {
    // Get character ID
    let char_id: i32 = sqlx::query_scalar(
        "SELECT id FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_optional(pool)
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
            puntos_donacion = $49, puntos_torneo = $50, ts_points = $51,
            barco_slot = $52,
            montado = $53, levitando = $54, montado_body = $55,
            recompensas_real = $56, recompensas_caos = $57, reenlistadas = $58,
            questeando = $59, quest_num = $60, quest_kills = $61, quests_completed = $62,
            description = $63,
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
    .bind(data.puntos_donacion).bind(data.puntos_torneo).bind(data.ts_points)
    .bind(data.barco_slot as i32)
    .bind(data.montado).bind(data.levitando).bind(data.montado_body)
    .bind(data.recompensas_real).bind(data.recompensas_caos).bind(data.reenlistadas)
    .bind(data.questeando).bind(data.quest_num).bind(data.quest_kills).bind(data.quests_completed)
    .bind(&data.description)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error saving character: {}", e))?;

    // Upsert inventory (25 slots)
    for i in 0..data.inventory.len().min(25) {
        let (obj_idx, amount, equipped) = data.inventory[i];
        sqlx::query(
            "INSERT INTO character_inventory (character_id, slot, obj_index, amount, equipped)
             VALUES ($1, $2, $3, $4, $5)
             ON CONFLICT (character_id, slot)
             DO UPDATE SET obj_index = $3, amount = $4, equipped = $5"
        )
        .bind(char_id)
        .bind(i as i16)
        .bind(obj_idx)
        .bind(amount)
        .bind(equipped)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error saving inventory slot {}: {}", i, e))?;
    }

    // Upsert bank (up to 40 slots)
    for i in 0..data.bank.len().min(40) {
        let (obj_idx, amount) = data.bank[i];
        sqlx::query(
            "INSERT INTO character_bank (character_id, slot, obj_index, amount)
             VALUES ($1, $2, $3, $4)
             ON CONFLICT (character_id, slot)
             DO UPDATE SET obj_index = $3, amount = $4"
        )
        .bind(char_id)
        .bind(i as i16)
        .bind(obj_idx)
        .bind(amount)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error saving bank slot {}: {}", i, e))?;
    }

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

/// Get character ID by name.
pub async fn get_char_id(pool: &PgPool, char_name: &str) -> Result<i32, String> {
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

/// Count penalties for a character.
pub async fn count_penalties(pool: &PgPool, char_name: &str) -> i32 {
    let char_id = match get_char_id(pool, char_name).await {
        Ok(id) => id,
        Err(_) => return 0,
    };
    sqlx::query_scalar::<_, i64>(
        "SELECT COUNT(*) FROM character_penalties WHERE character_id = $1"
    )
    .bind(char_id)
    .fetch_one(pool)
    .await
    .unwrap_or(0) as i32
}

/// Remove a character from its account (for /BORRAR command).
/// This deletes the character record entirely.
pub async fn remove_char_from_account(pool: &PgPool, char_name: &str) -> Result<(), String> {
    delete_charfile(pool, char_name).await
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

fn starter_stats(class: &str) -> (i32, i32, i32) {
    let class_lower = class.to_lowercase();
    match class_lower.as_str() {
        "guerrero" => (50, 0, 50),
        "mago" => (30, 100, 30),
        "clerigo" => (40, 75, 40),
        "asesino" => (40, 0, 50),
        "bardo" => (35, 50, 40),
        "druida" => (35, 75, 35),
        "paladin" => (45, 50, 45),
        "cazador" => (40, 0, 55),
        "trabajador" => (45, 0, 60),
        "pirata" => (45, 0, 50),
        "ladron" => (35, 0, 50),
        "bandido" => (40, 0, 50),
        _ => (40, 0, 40),
    }
}

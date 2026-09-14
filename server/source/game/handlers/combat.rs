//! Combat handlers: melee/ranged attack, PvP damage, user_die, combat formulas.
//! Extracted from mod.rs to reduce file size.
//! VB6 13.3 parity: UsuarioImpacto, UsuarioAtacaUsuario, UserDañoUser, CalcularDaño,
//! PoderAtaqueArma, PoderEvasion, PoderEvasionEscudo, DoApuñalar, DoGolpeCritico.

#[path = "combat_pvp.rs"]
mod combat_pvp;

use combat_pvp::*;
pub(crate) use combat_pvp::{equipped_obj, obj_by_index, remove_user_invisibility};
use super::combat_ao20 as ao20;

use super::common::*;
use super::npcs::fire_elemental_react;
use super::skills::{skill_id, try_level_skill};
use super::{check_user_level, send_full_inventory, user_attack_npc, warp_user};
use crate::game::class_race::PlayerClass;
use crate::game::constants::*;
use crate::game::types::{GameState, MAX_INVENTORY_SLOTS, SendTarget};
use crate::game::world;
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, font_index};
use tracing::info;

// =====================================================================
// VB6 Combat Constants
// =====================================================================

// Combat constants imported from crate::game::constants

// =====================================================================
// Helper: get weapon info from user state
// =====================================================================

pub(super) struct WeaponInfo {
    pub obj_index: i32,
    pub is_proyectil: bool,
    pub min_hit: i32,
    pub max_hit: i32,
    pub refuerzo: i32,
    pub envenena: bool,
    pub has_ammo: bool,
    pub ammo_min_hit: i32,
    pub ammo_max_hit: i32,
    pub acuchilla: bool,
    pub apunala: bool,
}

pub(super) fn get_weapon_info(state: &GameState, conn_id: ConnectionId) -> WeaponInfo {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => {
            return WeaponInfo {
                obj_index: 0,
                is_proyectil: false,
                min_hit: 0,
                max_hit: 0,
                refuerzo: 0,
                envenena: false,
                has_ammo: false,
                ammo_min_hit: 0,
                ammo_max_hit: 0,
                acuchilla: false,
                apunala: false,
            };
        }
    };

    if user.equip.weapon == 0 || user.equip.weapon > MAX_INVENTORY_SLOTS {
        return WeaponInfo {
            obj_index: 0,
            is_proyectil: false,
            min_hit: 0,
            max_hit: 0,
            refuerzo: 0,
            envenena: false,
            has_ammo: false,
            ammo_min_hit: 0,
            ammo_max_hit: 0,
            acuchilla: false,
            apunala: false,
        };
    }

    let obj_idx = user.inventory[user.equip.weapon - 1].obj_index;
    if obj_idx <= 0 {
        return WeaponInfo {
            obj_index: 0,
            is_proyectil: false,
            min_hit: 0,
            max_hit: 0,
            refuerzo: 0,
            envenena: false,
            has_ammo: false,
            ammo_min_hit: 0,
            ammo_max_hit: 0,
            acuchilla: false,
            apunala: false,
        };
    }

    let (is_proy, w_min, w_max, refuerzo, envenena, uses_ammo, acuchilla, apunala) =
        match state.get_object(obj_idx) {
            Some(o) => (
                o.proyectil,
                o.min_hit,
                o.max_hit,
                o.refuerzo,
                o.envenena,
                o.municion > 0,
                o.acuchilla,
                o.apunala,
            ),
            None => (false, 0, 0, 0, false, false, false, false),
        };

    let (has_ammo, ammo_min, ammo_max) = if is_proy && uses_ammo {
        if user.equip.municion > 0 && user.equip.municion <= MAX_INVENTORY_SLOTS {
            let ammo_idx = user.inventory[user.equip.municion - 1].obj_index;
            match state.get_object(ammo_idx) {
                Some(a) => (true, a.min_hit, a.max_hit),
                None => (false, 0, 0),
            }
        } else {
            (false, 0, 0)
        }
    } else {
        (false, 0, 0)
    };

    WeaponInfo {
        obj_index: obj_idx,
        is_proyectil: is_proy,
        min_hit: w_min,
        max_hit: w_max,
        refuerzo,
        envenena,
        has_ammo,
        ammo_min_hit: ammo_min,
        ammo_max_hit: ammo_max,
        acuchilla,
        apunala,
    }
}

// =====================================================================
// AT — Melee/ranged attack handler
// =====================================================================

/// AT — Melee/ranged attack.
pub(super) async fn handle_attack(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.heading,
            u.char_index,
            u.dead,
            u.safe_toggle,
            u.level,
            u.attributes[0], // Strength
            u.attributes[1], // Agility
            u.min_hit,
            u.max_hit,
            u.skills[(skill_id::ARMAS - 1) as usize],
            u.skills[(skill_id::PROYECTILES - 1) as usize],
            u.skills[(skill_id::TACTICAS - 1) as usize],
            u.skills[(skill_id::DEFENSA - 1) as usize],
            u.skills[(skill_id::WRESTERLING - 1) as usize],
            u.skills[(skill_id::APUNALAR - 1) as usize],
            u.char_name.clone(),
            u.class,
        ),
        _ => return,
    };
    let (
        map,
        x,
        y,
        heading,
        _char_index,
        dead,
        _safe_on,
        level,
        strength,
        agility,
        min_hit,
        max_hit,
        skill_armas,
        _skill_proyectiles,
        _skill_tacticas,
        _skill_defensa,
        _skill_wrestling,
        _skill_apunalar,
        attacker_name,
        class,
    ) = user_data;

    if dead {
        return;
    }

    // VB6: HandleAttack exits early if Meditando
    let is_meditating = state
        .users
        .get(&conn_id)
        .map(|u| u.meditating)
        .unwrap_or(false);
    if is_meditating {
        return;
    }

    // AO20 UsuarioAtaca does not reveal a hidden attacker by itself: stealth/invisibility
    // is dropped in UsuarioAtacaUsuario (RemoveUserInvisibility), never when hitting NPCs.

    // VB6: If equipped weapon is ranged, block melee attack — "No puedes usar así este arma."
    {
        let weapon_slot = state
            .users
            .get(&conn_id)
            .map(|u| u.equip.weapon)
            .unwrap_or(0);
        if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
            let weapon_obj = state
                .users
                .get(&conn_id)
                .map(|u| u.inventory[weapon_slot - 1].obj_index)
                .unwrap_or(0);
            if weapon_obj > 0 {
                if state
                    .get_object(weapon_obj)
                    .map(|o| o.proyectil)
                    .unwrap_or(false)
                {
                    state.send_console(conn_id, "No puedes usar así este arma.", font_index::INFO);
                    return;
                }
            }
        }
    }

    // AO20 UsuarioAtaca: bow interval (peek) → spell→melee interval (peek) → melee interval.
    if !intervalo_permite_usar_arcos(state, conn_id, false) {
        return;
    }
    if !intervalo_permite_magia_golpe(state, conn_id, false) {
        return;
    }
    if !intervalo_permite_atacar(state, conn_id, true) {
        return;
    }

    // VB6 13.3 parity: melee attacks require min 10 stamina, deduct random 1-10.
    {
        let min_sta = state.users.get(&conn_id).map(|u| u.min_sta).unwrap_or(0);
        if min_sta < 10 {
            state.send_console(
                conn_id,
                "Estás muy cansado.", // AO20 Msg93
                font_index::INFO,
            );
            return;
        }
        let sta_cost = rand_range(1, 10);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_sta = (user.min_sta - sta_cost).max(0);
        }
        send_stats_sta(state, conn_id).await;
    }

    // Get target tile based on heading
    let (dx, dy) = world::heading_to_offset(heading);
    let target_x = x + dx;
    let target_y = y + dy;

    // Check if there's a user on the target tile
    let target_conn = state
        .world
        .grid(map)
        .and_then(|g| g.tile(target_x, target_y))
        .and_then(|t| t.user_conn);

    // Play attack sound/animation to area
    let swing_pkt = binary_packets::write_play_wave(2, x as i16, y as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &swing_pkt);

    if let Some(victim_id) = target_conn {
        let Some(both_in_arena) = puede_atacar(state, conn_id, victim_id).await else { return };
        usuario_ataca_usuario(state, conn_id, victim_id, ao20::AttackType::Melee, both_in_arena).await;
    } else {
        // Zone-aware safe check for NPC attacks
        if is_safe_at(state, map, x, y) {
            state.send_msg_id(conn_id, 164, "");
            return;
        }

        // VB6 UsuarioAtaca: Ctrl/melee attacks only the tile in front of the character.
        let target_npc = state
            .world
            .grid(map)
            .and_then(|g| g.tile(target_x, target_y))
            .map(|t| t.npc_index)
            .unwrap_or(0);

        if target_npc > 0 {
            user_attack_npc(
                state,
                conn_id,
                target_npc as usize,
                map,
                x,
                y,
                strength,
                agility,
                level,
                min_hit,
                max_hit,
                skill_armas,
                &attacker_name,
                class,
            )
            .await;
        } else {
            // AO20 UserAttackPosition (:879-885): swinging at air while paralyzed or
            // immobilized shaves AirHitReductParalisisTime (seconds) off the counters.
            let reduct = state.game_data.balance.backstab.air_hit_reduct_paralisis_time.max(0) * 25;
            if let Some(u) = state.users.get_mut(&conn_id) {
                if u.paralyzed || u.immobilized {
                    u.counter_paralisis = (u.counter_paralisis - reduct).max(0);
                }
            }
        }
    }
}

// =====================================================================
// Player death
// =====================================================================

/// Handle player death.
pub(super) async fn user_die(
    state: &mut GameState,
    conn_id: ConnectionId,
    killer_id: Option<ConnectionId>,
) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
            u.char_name.clone(),
            u.level,
        ),
        None => return,
    };
    let (map, x, y, char_index, victim_name, victim_level) = user_data;

    // Cancel active trade on death (VB6: FinComerciarUsu)
    let trade_partner = state
        .users
        .get(&conn_id)
        .and_then(|u| if u.trading { u.trade_partner } else { None });
    if let Some(partner) = trade_partner {
        super::commerce::cancel_trade(state, conn_id, partner).await;
    }

    // Cancel NPC commerce on death
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }

    // VB6: "¡Aaaahhhh!" floating text in red (vbRed=255) above dying character
    state.send_chat_over_head_to(
        SendTarget::ToArea { map, x, y },
        "\u{00A1}Aaaahhhh!",
        char_index.0 as i16,
        255,
    );

    // Mark as dead, change body to dead model
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = true;
        user.min_hp = 0;
        user.min_sta = 0;
        // Clear status effects
        user.paralyzed = false;
        user.immobilized = false;
        user.paralyzed_by = None;
        user.paralyzed_by_npc = None;
        user.invisible = false;
        user.meditating = false;
        user.resting = false;
        user.poisoned = false;
        user.poisoned_by = None;
        user.poisoned_skill_id = 0;
        user.montado = false;
        user.levitando = false;
        user.mimetizado = false;
        user.hidden = false;
        // VB6 UserDie: if navigating, use ghost boat (iFragataFantasmal=87), else normal dead body
        if user.navigating {
            user.body = 87; // iFragataFantasmal
        } else {
            user.body = DEAD_BODY_NEUTRAL;
        }
        user.head = DEAD_HEAD_NEUTRAL;
        user.weapon_anim = super::common::NINGUN_ARMA;
        user.shield_anim = super::common::NINGUN_ESCUDO;
        user.casco_anim = super::common::NINGUN_CASCO;
        // Clear auras
        user.aura_a = 0;
        user.aura_w = 0;
        user.aura_e = 0;
        user.aura_r = 0;
        user.aura_c = 0;
        // Resurrection cooldown
        user.time_revivir = 20;
        user.montado_obj = 0;
    }
    // AO20 UserDie → ActualizarVelocidadDeUsuario (VelocidadMuerto = 1.4).
    crate::game::handlers::actualizar_velocidad_de_usuario(state, conn_id);

    // Clear duel state on death
    let duel_partner = state
        .users
        .get(&conn_id)
        .map(|u| u.atacable_por)
        .unwrap_or(0);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.atacable_por = 0;
        user.duel_pending = 0;
    }
    if duel_partner > 0 {
        if let Some(partner) = state.users.get_mut(&duel_partner) {
            partner.atacable_por = 0;
            partner.duel_pending = 0;
        }
    }

    // VB6: Restore attributes if TomoPocion (Modulo_UsUaRiOs.bas:1558-1563)
    if let Some(u) = state.users.get_mut(&conn_id) {
        if u.tomo_pocion {
            u.attributes = u.attributes_backup;
            u.tomo_pocion = false;
            u.duracion_efecto = 0;
        }
    }

    // VB6: Kill all pets on death (Modulo_UsUaRiOs.bas:1576-1585)
    let pets = state
        .users
        .get(&conn_id)
        .map(|u| u.mascotas_index)
        .unwrap_or([0; 3]);
    for i in 0..3 {
        let pet_idx = pets[i];
        if pet_idx > 0 {
            // Kill pet NPC — send removal to area first
            if let Some(n) = state.get_npc(pet_idx) {
                let bp = binary_packets::write_character_remove(n.char_index.0 as i16);
                state.send_data_bytes(
                    SendTarget::ToArea {
                        map: n.map,
                        x: n.x,
                        y: n.y,
                    },
                    &bp,
                );
            }
            state.kill_npc(pet_idx);
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.mascotas_index = [0; 3];
        u.mascotas_type = [0; 3];
        u.nro_mascotas = 0;
    }

    // Deequip all items and drop inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.equip.weapon = 0;
        user.equip.armor = 0;
        user.equip.shield = 0;
        user.equip.helmet = 0;
        user.equip.municion = 0;
        user.equip.ring = 0;
        for slot in user.inventory.iter_mut() {
            slot.equipped = false;
        }
    }

    // VB6 parity: item drops on death
    // - ZONAPELEA (trigger 6): NO item drops at all
    // - Non-newbie (level > 12): TirarTodo — drop ALL items
    // - Newbie (level <= 12): TirarTodosLosItemsNoNewbies — drop only non-newbie items
    // Note: VB6 does NOT check criminal status for drops
    let tile_trigger = get_map_tile_trigger(state, map, x, y);
    let in_arena = tile_trigger == crate::data::maps::Trigger::CombatZone;
    let is_newbie = victim_level <= 12;

    if !in_arena {
        let mut items_to_drop: Vec<(i32, i32)> = Vec::new();
        if let Some(user) = state.users.get(&conn_id) {
            for slot in user.inventory.iter() {
                if slot.obj_index > 0 && slot.amount > 0 {
                    if is_newbie {
                        // Newbie: only drop non-newbie items
                        let is_newbie_item = state
                            .game_data
                            .objects
                            .get((slot.obj_index - 1) as usize)
                            .map(|o| o.newbie)
                            .unwrap_or(false);
                        if !is_newbie_item {
                            items_to_drop.push((slot.obj_index, slot.amount));
                        }
                    } else {
                        // Non-newbie: drop ALL items (TirarTodo)
                        items_to_drop.push((slot.obj_index, slot.amount));
                    }
                }
            }
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            for slot in user.inventory.iter_mut() {
                if slot.obj_index > 0 {
                    if is_newbie {
                        let is_newbie_item = state
                            .game_data
                            .objects
                            .get((slot.obj_index - 1) as usize)
                            .map(|o| o.newbie)
                            .unwrap_or(false);
                        if !is_newbie_item {
                            slot.obj_index = 0;
                            slot.amount = 0;
                        }
                    } else {
                        // Non-newbie: clear ALL items
                        slot.obj_index = 0;
                        slot.amount = 0;
                    }
                }
            }
        }

        let offsets = [
            (0, 0),
            (1, 0),
            (0, 1),
            (-1, 0),
            (0, -1),
            (1, 1),
            (-1, 1),
            (1, -1),
            (-1, -1),
        ];
        let mut off_idx = 0;
        let (drop_grid_w, drop_grid_h) = state.grid_dimensions(map);
        for (obj_idx, amount) in items_to_drop {
            if let Some(obj) = state.game_data.objects.get((obj_idx - 1) as usize) {
                let grh = obj.grh_index;
                let mut placed = false;
                for tries in 0..offsets.len() {
                    let idx = (off_idx + tries) % offsets.len();
                    let (ox, oy) = offsets[idx];
                    let tx = x + ox as i32;
                    let ty = y + oy as i32;
                    if tx < 1 || tx > drop_grid_w || ty < 1 || ty > drop_grid_h {
                        continue;
                    }
                    let tile_free = state
                        .world
                        .grid(map)
                        .and_then(|g| g.tile(tx, ty))
                        .map(|t| t.ground_item.obj_index == 0)
                        .unwrap_or(false);
                    if tile_free {
                        {
                            let grid = state.world.grid_mut(map);
                            if let Some(tile) = grid.tile_mut(tx, ty) {
                                tile.ground_item.obj_index = obj_idx;
                                tile.ground_item.amount = amount;
                            }
                        }
                        let ho_pkt = binary_packets::write_object_create(
                            tx as i16, ty as i16, grh as i16, obj_idx as i16,
                        );
                        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &ho_pkt);
                        off_idx = (idx + 1) % offsets.len();
                        placed = true;
                        break;
                    }
                }
                if !placed {
                    continue;
                }
            }
        }
    }

    send_full_inventory(state, conn_id).await;

    let pkt = binary_packets::write_multi_msg_simple(
        crate::protocol::packets::MultiMessageID::NPCKillUser,
    );
    state.send_bytes(conn_id, &pkt);
    send_stats_hp(state, conn_id).await;

    let map_is_pk = state
        .game_data
        .maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if map_is_pk {
        let pkt = binary_packets::write_dead();
        state.send_bytes(conn_id, &pkt);
    }

    let fx_clear_pkt = binary_packets::write_create_fx(char_index.0 as i16, 0, 0);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_clear_pkt);

    let heading = state.users.get(&conn_id).map(|u| u.heading).unwrap_or(2);
    let cp_pkt = binary_packets::write_character_change(
        char_index.0 as i16,
        DEAD_BODY_NEUTRAL as i16,
        DEAD_HEAD_NEUTRAL as i16,
        heading as u8,
        0,
        0,
        0,
        0,
        0,
    );
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp_pkt);

    if let Some(user) = state.users.get(&conn_id) {
        let au_pkt = binary_packets::write_aura_update(
            user.char_index.0 as i16,
            user.aura_a as i16,
            user.aura_w as i16,
            user.aura_e as i16,
            user.aura_r as i16,
            user.aura_c as i16,
        );
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &au_pkt);
    }

    // Award experience to killer
    if let Some(killer) = killer_id {
        let exp_gain = (victim_level as i64) * 2;
        let killer_name = state
            .users
            .get(&killer)
            .map(|u| u.char_name.clone())
            .unwrap_or_default();

        if let Some(k) = state.users.get_mut(&killer) {
            k.exp += exp_gain;
        }

        // VB6 13.3: ZONAPELEA — both players in CombatZone suppresses criminal penalty on kill.
        let victim_in_arena_kill =
            get_map_tile_trigger(state, map, x, y) == crate::data::maps::Trigger::CombatZone;
        let killer_arena_pos = state
            .users
            .get(&killer)
            .map(|k| (k.pos_map, k.pos_x, k.pos_y))
            .unwrap_or((0, 0, 0));
        let killer_in_arena_kill = get_map_tile_trigger(
            state,
            killer_arena_pos.0,
            killer_arena_pos.1,
            killer_arena_pos.2,
        ) == crate::data::maps::Trigger::CombatZone;
        let both_in_arena_kill = victim_in_arena_kill && killer_in_arena_kill;

        // VB6 13.3: PvP kill reputation update
        // Kill citizen: rep_asesino += 2000
        // Kill criminal: rep_noble += 500
        // Also update kill counters (criminales_matados / ciudadanos_matados)
        // ZONAPELEA: skip all reputation and kill-counter changes when both players are in CombatZone
        let victim_criminal = state
            .users
            .get(&conn_id)
            .map(|u| u.criminal)
            .unwrap_or(false);
        let victim_name_upper = victim_name.to_uppercase();
        if !both_in_arena_kill {
            if victim_criminal {
                let last = state
                    .users
                    .get(&killer)
                    .map(|k| k.last_crim_matado.clone())
                    .unwrap_or_default();
                if last != victim_name_upper {
                    if let Some(k) = state.users.get_mut(&killer) {
                        k.last_crim_matado = victim_name_upper;
                        if k.criminales_matados < 65000 {
                            k.criminales_matados += 1;
                        }
                    }
                }
                if let Some(k) = state.users.get_mut(&killer) {
                    k.rep_noble += 500;
                }
            } else {
                let last = state
                    .users
                    .get(&killer)
                    .map(|k| k.last_ciud_matado.clone())
                    .unwrap_or_default();
                if last != victim_name_upper {
                    if let Some(k) = state.users.get_mut(&killer) {
                        k.last_ciud_matado = victim_name_upper;
                        if k.ciudadanos_matados < 65000 {
                            k.ciudadanos_matados += 1;
                        }
                    }
                }
                if let Some(k) = state.users.get_mut(&killer) {
                    k.rep_asesino += 2000;
                }
            }
        }
        // VB6 M26: Guild war kill — increment killer guild's puntos_clan when killing an enemy guild member.
        // Only if both players are in different guilds and those guilds are at war.
        {
            let killer_guild = state.users.get(&killer).map(|u| u.guild_index).unwrap_or(0);
            let victim_guild = state
                .users
                .get(&conn_id)
                .map(|u| u.guild_index)
                .unwrap_or(0);
            if killer_guild > 0 && victim_guild > 0 && killer_guild != victim_guild {
                let relation = crate::game::handlers::guilds_handler::get_guild_relation(
                    state,
                    killer_guild,
                    victim_guild,
                );
                if relation == crate::game::handlers::guilds_handler::GUILD_REL_WAR {
                    // Guilds are at war — award kill point
                    let pool = state.pool.clone();
                    tokio::spawn(async move {
                        if let Some(mut gi) =
                            crate::db::guilds::load_guild(&pool, killer_guild).await
                        {
                            gi.puntos_clan += 1;
                            let _ = crate::db::guilds::save_guild(&pool, &gi).await;
                        }
                    });
                }
            }
        }

        recalc_criminal(state, killer);
        // Broadcast appearance change (criminal status may have changed)
        let (km, kx, ky) = state
            .users
            .get(&killer)
            .map(|u| (u.pos_map, u.pos_x, u.pos_y))
            .unwrap_or((0, 0, 0));
        if km > 0 {
            if let Some(k) = state.users.get(&killer) {
                let cp = binary_packets::write_character_change(
                    k.char_index.0 as i16,
                    k.body as i16,
                    k.head as i16,
                    k.heading as u8,
                    k.weapon_anim as i16,
                    k.shield_anim as i16,
                    k.casco_anim as i16,
                    0,
                    0,
                );
                state.send_data_bytes(
                    SendTarget::ToArea {
                        map: km,
                        x: kx,
                        y: ky,
                    },
                    &cp,
                );
            }
        }

        state.send_msg_id(killer, 60, &format!("{}@{}", victim_name, exp_gain));
        state.send_msg_id(killer, 170, &format!("{}", exp_gain));
        send_stats_exp(state, killer).await;
        check_user_level(state, killer).await;

        info!(
            "[COMBAT] '{}' killed '{}' (+{} exp)",
            killer_name, victim_name, exp_gain
        );

        // VB6 13.3 parity: party death XP penalty — all party members lose ELV * 10 * CantMiembros
        let victim_party_idx = state
            .users
            .get(&conn_id)
            .map(|u| u.party_index)
            .unwrap_or(0);
        if victim_party_idx > 0 {
            if let Some(Some(party)) = state.parties.get(victim_party_idx as usize) {
                let member_count = party.members.len() as i64;
                let member_ids: Vec<ConnectionId> = party.members.clone();
                for member_id in member_ids {
                    let penalty = state
                        .users
                        .get(&member_id)
                        .map(|u| (u.level as i64) * -10 * member_count)
                        .unwrap_or(0);
                    if penalty < 0 {
                        if let Some(m) = state.users.get_mut(&member_id) {
                            m.exp = (m.exp + penalty).max(0);
                        }
                        send_stats_exp(state, member_id).await;
                    }
                }
            }
        }
    }

    // Zone exit point: if the player died inside a zone with a defined exit point,
    // warp their ghost to that exit instead of leaving them at the death tile.
    // VB6 parity: zones with salida_map > 0 expel dead players to the exit coords.
    let zone_exit = state
        .game_data
        .maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
        .and_then(|game_map| game_map.get_zone_at(x - 1, y - 1))
        .filter(|z| z.salida_map > 0)
        .map(|z| (z.salida_map, z.salida_x, z.salida_y));

    if let Some((exit_map, exit_x, exit_y)) = zone_exit {
        info!(
            "[COMBAT] '{}' died in zone with exit — warping to map {} ({},{})",
            victim_name, exit_map, exit_x, exit_y
        );
        warp_user(state, conn_id, exit_map, exit_x, exit_y).await;
    }
}

// =====================================================================
// AO20 UsuarioAtacaUsuario — shared by melee (HandleAttack) and ranged (WorkLeftClick)
// =====================================================================

/// AO20 `UsuarioAtacaUsuario` (SistemaCombate.bas:1037) after `PuedeAtacar` passed:
/// distance check, `UsuarioAtacadoPorUsuario`, `UsuarioImpacto`, `UserDamageToUser`,
/// `UserDañoEspecial`, then our reputation update and the death check.
pub(crate) async fn usuario_ataca_usuario(
    state: &mut GameState,
    conn_id: ConnectionId,
    victim_id: ConnectionId,
    _atype: ao20::AttackType,
    both_in_arena: bool,
) {
    let (map, x, y, heading, char_index, level, strength, agility, min_hit, max_hit, attacker_name, class) =
        match state.users.get(&conn_id) {
            Some(u) => (
                u.pos_map,
                u.pos_x,
                u.pos_y,
                u.heading,
                u.char_index,
                u.level,
                u.attributes[0],
                u.attributes[1],
                u.min_hit,
                u.max_hit,
                u.char_name.clone(),
                u.class,
            ),
            None => return,
        };
    let (victim_name, v_level, v_agility, v_tacticas, v_defensa, v_class, v_char_index, v_meditating) =
        match state.users.get(&victim_id) {
            Some(v) if v.logged && !v.dead => (
                v.char_name.clone(),
                v.level,
                v.attributes[1],
                v.skills[(skill_id::TACTICAS - 1) as usize],
                v.skills[(skill_id::DEFENSA - 1) as usize],
                v.class,
                v.char_index,
                v.meditating,
            ),
            _ => return,
        };
    {
        // ===== AO20 UsuarioAtacaUsuario (SistemaCombate.bas:1037) =====
        let (vx, vy) = state
            .users
            .get(&victim_id)
            .map(|v| (v.pos_x, v.pos_y))
            .unwrap_or((0, 0));
        if ao20::distancia(x, y, vx, vy) > ao20::MAX_DISTANCIA_ARCO {
            state.send_console(conn_id, "Estás demasiado lejos.", font_index::INFO); // AO20 Msg8
            return;
        }

        // UsuarioAtacadoPorUsuario: a targeted victim stops meditating (packets below on hit/miss).
        if v_meditating {
            if let Some(victim) = state.users.get_mut(&victim_id) {
                victim.meditating = false;
            }
            state.send_bytes(victim_id, &binary_packets::write_meditate_toggle());
            let fx_clear = binary_packets::write_create_fx(v_char_index.0 as i16, 0, 0);
            state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &fx_clear);
        }

        // ── UsuarioImpacto (:942) ──
        let weapon_obj = equipped_obj(state, conn_id, |u| u.equip.weapon);
        let attacker_skills = state.users.get(&conn_id).map(|u| u.skills).unwrap_or([0; 22]);
        let (poder_ataque, attack_skill) = ao20::poder_ataque_for_weapon(
            weapon_obj.as_ref(),
            |s| attacker_skills[(s - 1) as usize],
            agility,
            level,
            state.game_data.balance.class_mod_ataque_armas_e(class),
            state.game_data.balance.class_mod_ataque_proyectiles_e(class),
        );
        let attack_skill_idx = attack_skill as usize;

        let shield_obj = equipped_obj(state, victim_id, |u| u.equip.shield);
        let shield_porcentaje = shield_obj.as_ref().map(|s| s.porcentaje).unwrap_or(0);
        let mut user_poder_evasion =
            ao20::poder_evasion(v_tacticas, v_agility, v_level, state.game_data.balance.class_mod_evasion_e(v_class));
        if shield_obj.is_some() && shield_porcentaje > 0 {
            user_poder_evasion += ao20::poder_evasion_escudo(
                v_defensa,
                state.game_data.balance.class_mod_escudo_e(v_class),
                shield_porcentaje,
            );
        }
        let prob_rechazo = ao20::prob_rechazo_escudo_user(shield_obj.is_some(), shield_porcentaje, v_defensa, v_tacticas);
        let prob_exito = ao20::prob_impacto_user(poder_ataque, user_poder_evasion, v_meditating, 0);
        let hit = rand_range(1, 100) <= prob_exito;

        if !hit {
            if rand_range(1, 100) <= prob_rechazo {
                // Se rechazó el ataque con el escudo: sonido, EscudoMov, mensajes, FX 88, sube Defensa.
                let snd = binary_packets::write_play_wave(37, vx as i16, vy as i16); // SND_ESCUDO
                state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &snd);
                let pkt_atk = binary_packets::write_multi_msg_simple(
                    crate::protocol::packets::MultiMessageID::BlockedWithShieldOther,
                );
                state.send_bytes(conn_id, &pkt_atk);
                let pkt_vic = binary_packets::write_multi_msg_simple(
                    crate::protocol::packets::MultiMessageID::BlockedWithShieldUser,
                );
                state.send_bytes(victim_id, &pkt_vic);
                let fx = binary_packets::write_create_fx(v_char_index.0 as i16, 88, 0);
                state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &fx);
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    try_level_skill(victim, skill_id::DEFENSA as usize);
                }
            } else {
                // "¡X te atacó y falló!" (1930) / "¡Has fallado el golpe!" (1043)
                let pkt = binary_packets::write_multi_user_attacked_swing(char_index.0 as i16);
                state.send_bytes(victim_id, &pkt);
                let pkt = binary_packets::write_multi_msg_simple(
                    crate::protocol::packets::MultiMessageID::UserSwing,
                );
                state.send_bytes(conn_id, &pkt);
            }
            // Bandido with Ocultarse < 100 loses stealth even on a miss; everyone else always.
            let keep_stealth = class == PlayerClass::Bandido
                && attacker_skills[(skill_id::OCULTARSE - 1) as usize] >= 100;
            if !keep_stealth {
                remove_user_invisibility(state, conn_id).await;
            }
            // CharSwing to the area (or only to a hidden attacker)
            let snd = binary_packets::write_play_wave(2, x as i16, y as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
            state.send_chat_over_head_to(
                SendTarget::ToArea { map, x, y },
                "\u{00A1}Fallo!",
                v_char_index.0 as i16,
                255,
            );
            return;
        }

        // ── Hit ── SubirSkillDeArmaActual
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, attack_skill_idx);
        }
        // FXSANGRE on the victim unless sailing AND mounted (AO20: `Navegando = 0 Or Montado = 0`)
        {
            let (v_nav, v_mount) = state
                .users
                .get(&victim_id)
                .map(|v| (v.navigating, v.montado))
                .unwrap_or((false, false));
            if !v_nav || !v_mount {
                let fx = binary_packets::write_create_fx(v_char_index.0 as i16, 14, 0);
                state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &fx);
            }
        }
        // RemoveUserInvisibility — Hunter keeps it only with a camouflage armor at Ocultarse 100
        // (no `Camouflage` armor flag in our obj.dat → hunters lose it like everyone).
        remove_user_invisibility(state, conn_id).await;

        // ── UserDamageToUser (:1104) ──
        let ammo_obj = equipped_obj(state, conn_id, |u| u.equip.municion);
        let (nav, mounted, ship_obj, saddle_obj) = {
            let u = state.users.get(&conn_id);
            let nav = u.map(|u| u.navigating).unwrap_or(false);
            let mounted = u.map(|u| u.montado).unwrap_or(false);
            (
                nav,
                mounted,
                if nav { equipped_obj(state, conn_id, |u| u.barco_slot) } else { None },
                if mounted { obj_by_index(state, state.users.get(&conn_id).map(|u| u.montado_obj).unwrap_or(0)) } else { None },
            )
        };
        let base_damage = ao20::get_user_damage_with_item(&ao20::DamageInputs {
            user_min_hit: min_hit,
            user_max_hit: max_hit,
            fuerza: strength,
            weapon: weapon_obj.as_ref(),
            ammo: ammo_obj.as_ref(),
            vs_npc: false,
            dano_armas: state.game_data.balance.class_mod_dano_armas_e(class),
            dano_proyectiles: state.game_data.balance.class_mod_dano_proyectiles_e(class),
            dano_wrestling: state.game_data.balance.class_mod_dano_wrestling_e(class),
            ship_or_saddle: ship_obj.as_ref().or(saddle_obj.as_ref()),
            navigating: nav,
            mounted,
        });

        let lugar = ao20::lugar_golpe_user();
        let mut defensa: i64 = 0;
        if lugar == ao20::B_CABEZA {
            if let Some(c) = equipped_obj(state, victim_id, |u| u.equip.helmet) {
                defensa += rand_range(c.min_def, c.max_def.max(c.min_def)) as i64;
            }
        } else {
            if let Some(a) = equipped_obj(state, victim_id, |u| u.equip.armor) {
                defensa += rand_range(a.min_def, a.max_def.max(a.min_def)) as i64;
            }
            if let Some(e) = shield_obj.as_ref() {
                defensa += rand_range(e.min_def, e.max_def.max(e.min_def)) as i64;
            }
        }
        // Defensa del barco de la víctima, o de la montura
        {
            let (v_nav, v_mount, v_saddle) = state
                .users
                .get(&victim_id)
                .map(|v| (v.navigating, v.montado, v.montado_obj))
                .unwrap_or((false, false, 0));
            if v_nav {
                if let Some(b) = equipped_obj(state, victim_id, |u| u.barco_slot) {
                    defensa += rand_range(b.min_def, b.max_def.max(b.min_def)) as i64;
                }
            } else if v_mount {
                if let Some(m) = obj_by_index(state, v_saddle) {
                    defensa += rand_range(m.min_def, m.max_def.max(m.min_def)) as i64;
                }
            }
        }
        // AO20 armor_penetration_feature: off → ArmorPen = 0.
        let mut damage = (base_damage - defensa).max(0);
        let mut bonus_damage: i64 = 0;

        let n4_pkt = binary_packets::write_multi_user_hitted_by_user(char_index.0 as i16, lugar as u8, damage as i16);
        state.send_bytes(victim_id, &n4_pkt);
        let n5_pkt = binary_packets::write_multi_user_hitted_user(v_char_index.0 as i16, lugar as u8, damage as i16);
        state.send_bytes(conn_id, &n5_pkt);

        let bs = state.game_data.balance.backstab;
        let weapon_extra = weapon_obj.as_ref().map(|w| w.extra_crit_and_stab_chance).unwrap_or(0.0);
        let v_heading = state.users.get(&victim_id).map(|v| v.heading).unwrap_or(0);
        let back_bonus = ao20::back_hit_bonus(heading, v_heading, ao20::distancia(x, y, vx, vy), &bs);
        let skill_apunalar = attacker_skills[(skill_id::APUNALAR - 1) as usize];
        let skill_wrestling = attacker_skills[(skill_id::WRESTERLING - 1) as usize];

        if ao20::puede_golpe_critico(class, weapon_obj.as_ref()) {
            // Golpe crítico (ignora defensa): Bandido con nudillos
            let chance = (ao20::critical_chance_base(skill_wrestling, &bs, weapon_extra) + back_bonus).clamp(0.0, 100.0);
            if rand_range(1, 100) as f32 <= chance {
                bonus_damage = (damage as f64 * bs.critical_hit_dmg_modifier as f64) as i64;
                state.send_console(conn_id, &format!("Has golpeado críticamente a {} por {}.", victim_name, bonus_damage), font_index::FIGHT);
                state.send_console(victim_id, &format!("{} te ha golpeado críticamente por {}.", attacker_name, bonus_damage), font_index::FIGHT);
                let snd = binary_packets::write_play_wave(10, x as i16, y as i16); // SND_IMPACTO_CRITICO ≈ impacto
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
            }
        } else if ao20::puede_apunalar(class, skill_apunalar, weapon_obj.as_ref()) {
            let chance = (ao20::stabbing_chance_base(class, skill_apunalar, &bs, weapon_extra) + back_bonus).clamp(0.0, 100.0);
            if rand_range(1, 100) as f32 <= chance {
                bonus_damage = (damage as f64 * state.game_data.balance.mod_apunalar[class.index()] as f64) as i64;
                // 210 / 211
                state.send_console(conn_id, &format!("Has apuñalado a {} por {}", victim_name, bonus_damage), font_index::FIGHT);
                state.send_console(victim_id, &format!("{} te ha apuñalado por {}", attacker_name, bonus_damage), font_index::FIGHT);
                super::weapon_visuals::confirmed_hit(state, conn_id, v_char_index.0 as i16, true, false);
                let fx = binary_packets::write_create_fx(v_char_index.0 as i16, 89, 0);
                state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &fx);
                let snd = binary_packets::write_play_wave(10, x as i16, y as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
            }
            // Sube skills en apuñalar (siempre que pudo intentar)
            if let Some(u) = state.users.get_mut(&conn_id) {
                try_level_skill(u, skill_id::APUNALAR as usize);
            }
        }

        // Desequipar de un golpe (Bandido / Ladrón sin arma)
        if ao20::puede_desequipar_de_un_golpe(class, weapon_obj.as_ref()) {
            let skill_for_weapon = attacker_skills[(ao20::skill_required_for_weapon(weapon_obj.as_ref()) - 1) as usize];
            if rand_range(1, 100) <= ao20::prob_desequipar(class, skill_for_weapon) {
                desequipar_objeto_de_un_golpe(state, conn_id, victim_id, lugar, &attacker_name, &victim_name).await;
            }
        }

        let (v_min_hp, v_max_hp) = state
            .users
            .get(&victim_id)
            .map(|v| (v.min_hp, v.max_hp))
            .unwrap_or((0, 0));
        damage = ao20::apply_bonus_damage(damage, bonus_damage, v_min_hp, v_max_hp);

        // DoDamageOrHeal
        super::weapon_visuals::confirmed_hit(state, conn_id, v_char_index.0 as i16, false, weapon_obj.as_ref().map(|w| w.proyectil).unwrap_or(false));
        state.send_chat_over_head_to(
            SendTarget::ToArea { map, x, y },
            &format!("-{}", damage),
            v_char_index.0 as i16,
            65535,
        );
        if let Some(victim) = state.users.get_mut(&victim_id) {
            victim.min_hp = victim.min_hp.saturating_sub(damage as i32);
        }
        let still_alive = state.users.get(&victim_id).map(|v| v.min_hp > 0).unwrap_or(false);
        if still_alive {
            let snd = binary_packets::write_play_wave(10, x as i16, y as i16); // SND_IMPACTO
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
            let fx = binary_packets::write_create_fx(v_char_index.0 as i16, 14, 0); // FX_BLOOD
            state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &fx);
            user_dano_especial(state, conn_id, victim_id, weapon_obj.as_ref(), ammo_obj.as_ref(), &attacker_name, &victim_name).await;
        }

        // VB6: Fire Elemental reacts to PvP
        fire_elemental_react(state, victim_id, &attacker_name);

        // Update victim HP
        send_stats_hp(state, victim_id).await;

        // VB6 13.3: PvP hit reputation update
        // Attack citizen: rep_bandido += 100, rep_noble halved
        // Attack criminal: rep_noble += 5
        // ZONAPELEA: skip all reputation changes when both players are in CombatZone
        if !both_in_arena {
            let victim_is_criminal = state
                .users
                .get(&victim_id)
                .map(|u| u.criminal)
                .unwrap_or(false);
            if victim_is_criminal {
                if let Some(attacker) = state.users.get_mut(&conn_id) {
                    attacker.rep_noble += 5;
                }
            } else {
                if let Some(attacker) = state.users.get_mut(&conn_id) {
                    attacker.rep_bandido += 100;
                    attacker.rep_noble = (attacker.rep_noble as f32 * 0.5) as i32;
                }
            }
            recalc_criminal(state, conn_id);
        }

        // Check death
        let v_hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
        if v_hp <= 0 {
            user_die(state, victim_id, Some(conn_id)).await;
        }
    }
}

/// AO20 `PuedeAtacar` (SistemaCombate.bas:1352) as this server implements it (guild war,
/// seguro, seguro clan, duelo, zona segura, arena/ZONAPELEA, muerto, GM). Shared by melee
/// and ranged. Returns `Some(both_in_arena)` when the attack may proceed.
pub(crate) async fn puede_atacar(state: &mut GameState, conn_id: ConnectionId, victim_id: ConnectionId) -> Option<bool> {
    let (map, x, y, safe_on, a_dead, a_party, a_cursed, a_montado) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.safe_toggle, u.dead, u.party_index, u.cursed, u.montado),
        None => return None,
    };
    // AO20 PuedeAtacar, in its order: dead attacker, dead victim, same group,
    // curse, mount. (Retos, consulta, teams and GM ranks have no counterpart here.)
    if a_dead {
        state.send_console(conn_id, "¡¡Estás muerto!!", font_index::INFO);
        return None;
    }
    let (v_dead, v_party) = match state.users.get(&victim_id) {
        Some(v) => (v.dead, v.party_index),
        None => return None,
    };
    if v_dead {
        state.send_console(conn_id, "No podés atacar a un espiritu.", font_index::INFO);
        return None;
    }
    if a_party > 0 && a_party == v_party {
        state.send_console(conn_id, "No podés atacar a un miembro de tu grupo.", font_index::INFO);
        return None;
    }
    if a_cursed {
        state.send_console(conn_id, "¡Estás maldito! No podes atacar.", font_index::INFO);
        return None;
    }
    if a_montado {
        state.send_console(conn_id, "No podés atacar usando una montura.", font_index::INFO);
        return None;
    }
    {
        // PvP attack — check guild war bypass before safety toggle
        let (attacker_guild, attacker_seguro) = state
            .users
            .get(&conn_id)
            .map(|u| (u.guild_index, u.seguro_clan))
            .unwrap_or((0, false));
        let victim_guild = state
            .users
            .get(&victim_id)
            .map(|u| u.guild_index)
            .unwrap_or(0);

        // Guild war bypasses safety toggle
        let guilds_at_war = attacker_guild > 0
            && victim_guild > 0
            && attacker_guild != victim_guild
            && super::guilds_handler::get_guild_relation(state, attacker_guild, victim_guild)
                == super::guilds_handler::GUILD_REL_WAR;

        if safe_on && !guilds_at_war {
            // VB6 13.3 parity: safety toggle only blocks attacking citizens (non-criminals).
            // Attacking criminals is always allowed.
            let victim_is_criminal = state
                .users
                .get(&victim_id)
                .map(|u| u.criminal)
                .unwrap_or(false);
            if !victim_is_criminal {
                state.send_msg_id(conn_id, 207, "");
                return None;
            }
        }

        // Clan safe check
        if attacker_guild > 0 && attacker_guild == victim_guild && attacker_seguro {
            state.send_console(conn_id, "No puedes atacar a un miembro de tu clan. Usa /SEGUROCLAN para desactivar el seguro.", font_index::INFO);
            return None;
        }

        // VB6: Safe zone check — dueling players bypass safe zone restriction
        let in_duel = state
            .users
            .get(&conn_id)
            .map(|u| u.atacable_por == victim_id)
            .unwrap_or(false)
            && state
                .users
                .get(&victim_id)
                .map(|u| u.atacable_por == conn_id)
                .unwrap_or(false);

        if !in_duel {
            // Zone-aware safe check (Trigger > Zone > Map hierarchy)
            if is_safe_at(state, map, x, y) {
                state.send_msg_id(conn_id, 163, "");
                return None;
            }
            let victim_pos = state
                .users
                .get(&victim_id)
                .map(|v| (v.pos_x, v.pos_y))
                .unwrap_or((0, 0));
            if is_safe_at(state, map, victim_pos.0, victim_pos.1) {
                state.send_msg_id(conn_id, 163, "");
                return None;
            }
        }

        // VB6 13.3: ZONAPELEA — if BOTH players are in CombatZone, allow PvP without criminal penalty.
        // If only one is in CombatZone, block PvP entirely.
        let attacker_in_arena =
            get_map_tile_trigger(state, map, x, y) == crate::data::maps::Trigger::CombatZone;
        let victim_arena_pos = state
            .users
            .get(&victim_id)
            .map(|v| (v.pos_map, v.pos_x, v.pos_y))
            .unwrap_or((0, 0, 0));
        let victim_in_arena = get_map_tile_trigger(
            state,
            victim_arena_pos.0,
            victim_arena_pos.1,
            victim_arena_pos.2,
        ) == crate::data::maps::Trigger::CombatZone;

        if attacker_in_arena != victim_in_arena {
            // One player is in the arena, the other is not — block combat entirely.
            state.send_console(
                conn_id,
                "Ambos jugadores deben estar en la zona de pelea.",
                font_index::INFO,
            );
            return None;
        }
        let both_in_arena = attacker_in_arena && victim_in_arena;

        let victim_data = match state.users.get(&victim_id) {
            Some(v) if v.logged => (
                v.dead,
                v.privileges,
                v.char_name.clone(),
                v.level,
                v.attributes[1], // Victim agility
                v.skills[3],     // SK4 = Tacticas
                v.skills[4],     // SK5 = Defensa
                v.max_hp,
                v.min_hp,
                v.class.clone(),
                v.heading,
                v.char_index,
                v.meditating,
            ),
            _ => return None,
        };
        let (
            v_dead,
            v_privs,
            _victim_name,
            _v_level,
            _v_agility,
            _v_tacticas,
            _v_defensa,
            _v_max_hp,
            _v_min_hp,
            _v_class,
            _v_heading,
            _v_char_index,
            _v_meditating,
        ) = victim_data;

        if v_dead {
            state.send_msg_id(conn_id, 154, "");
            return None;
        }
        if v_privs > 0 {
            state.send_msg_id(conn_id, 155, "");
            return None;
        }

        Some(both_in_arena)
    }
}

//! PvP-specific combat helpers: armor absorption for player vs player.
//! Extracted from combat.rs.

use super::super::common::rand_range;

use crate::game::types::GameState;
use crate::net::ConnectionId;

// =====================================================================
// VB6 13.3: Criminal status recalculation
// =====================================================================

/// VB6 13.3: Recalculate criminal status from 6 reputation fields.
/// L = (-asesino - bandido + burgues - ladrones + noble + plebe) / 6
/// criminal = L < 0
pub(super) fn recalc_criminal(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        let l = (-user.rep_asesino - user.rep_bandido + user.rep_burgues - user.rep_ladrones
            + user.rep_noble
            + user.rep_plebe)
            / 6;
        user.criminal = l < 0;
    }
}

// =====================================================================
// PvP armor absorption (VB6: UserDañoUser)
// =====================================================================

// =====================================================================
// AO20 helpers used by the attack paths (SistemaCombate.bas / Modulo_UsUaRiOs.bas)
// =====================================================================

use crate::data::objects::ObjData;
use crate::game::types::SendTarget;
use crate::protocol::{binary_packets, font_index};

/// The `ObjData` equipped in the slot returned by `slot_of` (1-based inventory slot), if any.
pub(crate) fn equipped_obj(
    state: &GameState,
    conn_id: ConnectionId,
    slot_of: impl Fn(&crate::game::types::UserState) -> usize,
) -> Option<ObjData> {
    let user = state.users.get(&conn_id)?;
    let slot = slot_of(user);
    if slot == 0 || slot > user.inventory.len() {
        return None;
    }
    let obj_index = user.inventory[slot - 1].obj_index;
    if obj_index <= 0 {
        return None;
    }
    state.get_object(obj_index).cloned()
}

pub(crate) fn obj_by_index(state: &GameState, obj_index: i32) -> Option<ObjData> {
    if obj_index <= 0 {
        return None;
    }
    state.get_object(obj_index).cloned()
}

/// AO20 `RemoveUserInvisibility`: drops spell invisibility and stealth, tells the area.
/// Admin invisibility is untouched.
pub(crate) async fn remove_user_invisibility(state: &mut GameState, conn_id: ConnectionId) {
    let (was_invis, was_hidden, ci, map, x, y, navigating) = match state.users.get(&conn_id) {
        Some(u) if !u.admin_invisible => (
            u.invisible,
            u.hidden,
            u.char_index.0 as i16,
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.navigating,
        ),
        _ => return,
    };
    if !was_invis && !was_hidden {
        return;
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.invisible = false;
        u.hidden = false;
        u.counter_invisible = 0;
        u.counter_oculto = 0;
    }
    state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO);
    if !navigating {
        if let Some(user) = state.users.get(&conn_id) {
            let cc = user.build_cc_binary();
            let cd = crate::game::handlers::common::build_cd_binary(user);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cd);
        }
    }
    let nover = binary_packets::write_set_invisible(ci, false, 0);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &nover);
}

/// AO20 `DesequiparObjetoDeUnGolpe` (SistemaCombate.bas:1268): by body part, unequip the
/// victim's helmet / weapon / shield (see `combat_ao20::desequipar_target`) and tell both.
pub(super) async fn desequipar_objeto_de_un_golpe(
    state: &mut GameState,
    attacker_id: ConnectionId,
    victim_id: ConnectionId,
    lugar: i32,
    attacker_name: &str,
    victim_name: &str,
) {
    use crate::data::objects::ObjType;
    let (has_helmet, has_weapon, has_shield, helmet_slot, weapon_slot, shield_slot) =
        match state.users.get(&victim_id) {
            Some(v) => (
                v.equip.helmet > 0,
                v.equip.weapon > 0,
                v.equip.shield > 0,
                v.equip.helmet,
                v.equip.weapon,
                v.equip.shield,
            ),
            None => return,
        };
    let (casco, arma, escudo) =
        crate::game::handlers::combat_ao20::desequipar_target(lugar, has_helmet, has_weapon, has_shield);
    let (slot, obj_type, msg_atk, msg_vic) = if casco {
        (helmet_slot, ObjType::Helmet, "Has logrado desequipar el casco de tu oponente!".to_string(), format!("{} te ha desequipado el casco.", attacker_name))
    } else if arma {
        (weapon_slot, ObjType::Weapon, "Has logrado desarmar a tu oponente!".to_string(), format!("{} te ha desarmado.", attacker_name))
    } else if escudo {
        (shield_slot, ObjType::Shield, format!("Has logrado desequipar el escudo de {}.", victim_name), format!("{} te ha desequipado el escudo.", attacker_name))
    } else {
        state.send_console(attacker_id, "No has logrado desequipar ningun item a tu oponente!", font_index::FIGHT);
        return;
    };
    if slot == 0 {
        return;
    }
    crate::game::handlers::inventory::unequip_slot(state, victim_id, slot - 1, &obj_type);
    if let Some(v) = state.users.get_mut(&victim_id) {
        if slot - 1 < v.inventory.len() {
            v.inventory[slot - 1].equipped = false;
        }
    }
    crate::game::handlers::common::send_inventory_slot(state, victim_id, slot - 1).await;
    // CP so the area sees the piece disappear
    if let Some(v) = state.users.get(&victim_id) {
        let cp = binary_packets::write_character_change(
            v.char_index.0 as i16,
            v.body as i16,
            v.head as i16,
            v.heading as u8,
            v.weapon_anim as i16,
            v.shield_anim as i16,
            v.casco_anim as i16,
            0,
            0,
        );
        let (map, x, y) = (v.pos_map, v.pos_x, v.pos_y);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);
    }
    state.send_console(attacker_id, &msg_atk, font_index::FIGHT);
    state.send_console(victim_id, &msg_vic, font_index::FIGHT);
}

/// AO20 `UserDañoEspecial` (SistemaCombate.bas:1819): on a landed hit the weapon (or its
/// ammo) may poison (30%), incinerate (10%), paralyze (10%) or stun (13%). One effect per hit.
/// There is no `Incinerado` status on our users: that roll is kept only to preserve the
/// order of the chain.
pub(super) async fn user_dano_especial(
    state: &mut GameState,
    attacker_id: ConnectionId,
    victim_id: ConnectionId,
    weapon: Option<&ObjData>,
    ammo: Option<&ObjData>,
    attacker_name: &str,
    victim_name: &str,
) {
    let Some(w) = weapon else { return };
    // Ranged with ammo: the ammo carries the effects; otherwise the weapon itself.
    let obj = if w.proyectil && w.municion > 0 { ammo } else { Some(w) };
    let Some(o) = obj else { return };

    if o.envenena {
        let poisoned = state.users.get(&victim_id).map(|v| v.poisoned).unwrap_or(true);
        if !poisoned && rand_range(1, 100) < 30 {
            if let Some(v) = state.users.get_mut(&victim_id) {
                v.poisoned = true;
                v.counter_poison = 0;
                v.poisoned_by = Some(attacker_id);
                v.poisoned_skill_id = if w.proyectil {
                    crate::game::handlers::skills::skill_id::PROYECTILES
                } else {
                    crate::game::handlers::skills::skill_id::ARMAS
                };
            }
            state.send_console(victim_id, &format!("\u{00A1}{} te ha envenenado!", attacker_name), font_index::FIGHT);
            state.send_console(attacker_id, &format!("\u{00A1}Has envenenado a {}!", victim_name), font_index::FIGHT);
            return;
        }
    }
    if o.incinera && rand_range(1, 100) < 10 {
        return;
    }
    if o.paraliza {
        let (v_paralyzed, vx, vy) = state
            .users
            .get(&victim_id)
            .map(|v| (v.paralyzed, v.pos_x, v.pos_y))
            .unwrap_or((true, 0, 0));
        if !v_paralyzed && rand_range(1, 100) < 10 {
            // Counters.Paralisis = 6 (AO20 seconds) → our 40 ms ticks
            let ticks = 6 * 25;
            if let Some(v) = state.users.get_mut(&victim_id) {
                v.paralyzed = true;
                v.paralysis_walk_warned = false;
                v.counter_paralisis = ticks;
                v.paralyzed_by = Some(attacker_id);
            }
            state.send_bytes(victim_id, &binary_packets::write_paralize_ok(6));
            state.send_bytes(victim_id, &binary_packets::write_pos_update(vx as i16, vy as i16));
            let (ci, map) = state
                .users
                .get(&victim_id)
                .map(|v| (v.char_index.0 as i16, v.pos_map))
                .unwrap_or((0, 0));
            let fx = binary_packets::write_create_fx(ci, 8, 0);
            state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &fx);
            state.send_console(victim_id, &format!("\u{00A1}{} te ha paralizado!", attacker_name), font_index::FIGHT);
            state.send_console(attacker_id, &format!("\u{00A1}Has paralizado a {}!", victim_name), font_index::FIGHT);
            return;
        }
    }
    if o.estupidiza {
        let stunned = state.users.get(&victim_id).map(|v| v.stunned).unwrap_or(true);
        if !stunned && rand_range(1, 100) < 13 {
            if let Some(v) = state.users.get_mut(&victim_id) {
                v.stunned = true;
                v.counter_stun = 3 * 25; // Counters.Estupidez = 3 s
            }
            state.send_bytes(victim_id, &binary_packets::write_silence());
            state.send_console(victim_id, &format!("\u{00A1}{} te ha estupidizado!", attacker_name), font_index::FIGHT);
            state.send_console(attacker_id, &format!("\u{00A1}Has estupidizado a {}!", victim_name), font_index::FIGHT);
        }
    }
}

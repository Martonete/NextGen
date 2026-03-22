//! GM query/info commands: /DONDE, /ESPIAR, /INFO, /MOD, /SMOD, etc.

use tracing::{info};
use crate::net::ConnectionId;
use crate::game::class_race::{PlayerClass};
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::protocol::{font_index, binary_packets};
use super::{warp_user, send_warp_fx, check_user_level, naked_body,
    send_stats_hp, send_stats_mana, send_stats_sta, send_stats_gold, send_stats_exp};
use super::common::MAX_GOLD;

// =============================================================================

/// /INVISIBLE — Toggle admin invisibility (body=0, head=0).
pub(super) async fn handle_slash_invisible(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y, char_index, is_invisible) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.admin_invisible)
        }
        _ => return,
    };

    if is_invisible {
        // Make visible again
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.admin_invisible = false;
            user.invisible = false;
        }
        // Re-broadcast appearance (body/head never changed — still intact)
        let cc = state.users.get(&conn_id).unwrap().build_cc_binary();
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc);
        // NOVER packet (visible)
        let nover = binary_packets::write_set_invisible(char_index.0 as i16, false, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &nover);
        state.send_console(conn_id, "Sos visible.", font_index::INFO);
    } else {
        // Go invisible — keep body/head intact for self-rendering with pulsing alpha
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.admin_invisible = true;
            user.invisible = true;
        }
        // BP — remove character from other players' screens
        let bp = binary_packets::write_character_remove(char_index.0 as i16);
        state.send_data_bytes(SendTarget::ToAreaButIndex { conn_id, map, x, y }, &bp);
        // NOVER packet — tell self we're invisible (permanent GM, duration=0)
        let nover = binary_packets::write_set_invisible(char_index.0 as i16, true, 0);
        state.send_bytes(conn_id, &nover);
        state.send_console(conn_id, "Sos invisible.", font_index::INFO);
    }
}

/// /DONDE nick — Locate user on map. Sends ||735@name@map@x@y.
pub(super) async fn handle_slash_donde(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.privileges));

    match found {
        Some((name, map, x, y, target_priv)) => {
            // Can't locate gods+
            if target_priv >= privilege_level::DIOS {
                let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
                if my_priv < target_priv {
                    state.send_console(conn_id, "No podes localizar a ese usuario.", font_index::INFO);
                    return;
                }
            }
            state.send_msg_id(conn_id, 735, &format!("{}@{}@{}@{}", name, map, x, y));
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /REVIVIR nick|YO — Revive user.
pub(super) async fn handle_slash_revivir(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = if target_upper == "YO" {
        Some(conn_id)
    } else {
        state.users.values()
            .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
            .map(|u| u.conn_id)
    };

    match target_conn {
        Some(tc) => {
            let is_dead = state.users.get(&tc).map(|u| u.dead).unwrap_or(false);
            if !is_dead {
                state.send_console(conn_id, "Ese usuario no esta muerto.", font_index::INFO);
                return;
            }

            // Revive: restore HP, clear dead flag, restore body
            // VB6: uses in-memory OrigChar.Head (not DB), DarCuerpoDesnudo for body
            let (map, x, y, race, max_hp, orig_head, gender) = match state.users.get(&tc) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.race, u.max_hp, u.orig_head, u.gender),
                None => return,
            };

            let new_body = naked_body(race, gender);

            if let Some(user) = state.users.get_mut(&tc) {
                user.dead = false;
                user.min_hp = max_hp;
                user.body = new_body;
                user.head = orig_head;
                if user.admin_invisible {
                    user.old_body = new_body;
                    user.old_head = orig_head;
                }
            }

            // Read final state for CP packet
            let (heading, weapon_anim, shield_anim, casco_anim, char_index) = match state.users.get(&tc) {
                Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim, u.char_index),
                None => return,
            };

            // VB6 GM /REVIVIR: no CFF (no resurrection FX), just ChangeUserChar + SendUserHP
            // Broadcast CP (character model change) — VB6: ChangeUserChar(toMap, ...)
            let cp_pkt = binary_packets::write_character_change(
                char_index.0 as i16, new_body as i16, orig_head as i16, heading as u8,
                weapon_anim as i16, shield_anim as i16, casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp_pkt);

            // Send HP update (VB6: SendUserHP)
            send_stats_hp(state, tc).await;

            // VB6: ||749@GMname — notify target who revived them
            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_msg_id(tc, 749, &admin_name.to_string());

            state.send_console(conn_id, &format!("{} revivido.", target), font_index::INFO);
            info!("[GM] {} revived {}", admin_name, target);
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /ESPIAR nick — Teleport invisibly to user.
pub(super) async fn handle_slash_espiar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y));

    match found {
        Some((map, x, y)) => {
            // Make invisible if not already
            let is_invisible = state.users.get(&conn_id).map(|u| u.admin_invisible).unwrap_or(false);
            if !is_invisible {
                handle_slash_invisible(state, conn_id).await;
            }

            // Warp to target (offset -2 to not be on exact tile)
            let warp_x = (x - 2).max(1);
            let warp_y = (y - 2).max(1);
            warp_user(state, conn_id, map, warp_x, warp_y).await;

            state.send_console(conn_id, &format!("Espiando a {}.", target), font_index::INFO);
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /INV nick — View user's inventory.
pub(super) async fn handle_slash_inv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match found {
        Some(tc) => {
            // Send each inventory slot as console messages
            let inv_data: Vec<(usize, i32, i32, bool)> = state.users.get(&tc)
                .map(|u| u.inventory.iter().enumerate()
                    .filter(|(_, s)| s.obj_index > 0)
                    .map(|(i, s)| (i + 1, s.obj_index, s.amount, s.equipped))
                    .collect())
                .unwrap_or_default();

            let target_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_console(conn_id, &format!("Inventario de {}:", target_name), font_index::INFO);

            let is_empty = inv_data.is_empty();
            for (slot, obj_idx, amount, equipped) in inv_data {
                let name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("Obj#{}", obj_idx));
                let eq = if equipped { " [E]" } else { "" };
                state.send_console(conn_id, &format!("  Slot {}: {} x{}{}", slot, name, amount, eq), font_index::INFO);
            }

            if is_empty {
                state.send_console(conn_id, "  (vacio)", font_index::INFO);
            }
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /BOV nick — View user's bank vault.
pub(super) async fn handle_slash_bov(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match found {
        Some(tc) => {
            let bank_data: Vec<(usize, i32, i32)> = state.users.get(&tc)
                .map(|u| u.bank.iter().enumerate()
                    .filter(|(_, s)| s.obj_index > 0)
                    .map(|(i, s)| (i + 1, s.obj_index, s.amount))
                    .collect())
                .unwrap_or_default();

            let target_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            let bank_gold = state.users.get(&tc).map(|u| u.bank_gold).unwrap_or(0);
            state.send_console(conn_id, &format!("Boveda de {} (Oro: {}):", target_name, bank_gold), font_index::INFO);

            let bank_empty = bank_data.is_empty();
            for (slot, obj_idx, amount) in bank_data {
                let name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("Obj#{}", obj_idx));
                state.send_console(conn_id, &format!("  Slot {}: {} x{}", slot, name, amount), font_index::INFO);
            }

            if bank_empty {
                state.send_console(conn_id, "  (vacia)", font_index::INFO);
            }
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /NICK2IP nick — Get IP of user.
pub(super) async fn handle_slash_nick2ip(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.ip.clone()));

    match found {
        Some((name, ip)) => {
            state.send_console(conn_id, &format!("IP de {}: {}", name, ip), font_index::INFO);
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /IP2NICK ip — Find users with given IP.
pub(super) async fn handle_slash_ip2nick(state: &mut GameState, conn_id: ConnectionId, ip: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let matches: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.ip == ip)
        .map(|u| u.char_name.clone())
        .collect();

    if matches.is_empty() {
        state.send_console(conn_id, &format!("Nadie con IP {}", ip), font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("IP {}: {}", ip, matches.join(", ")), font_index::INFO);
    }
}

/// /MOD <stat> <value> — Modify own stats. Requires Semidios+.
/// /SMOD <name> <stat> <value> — Modify another player's stats. Requires Director+.
pub(super) async fn handle_slash_mod(state: &mut GameState, conn_id: ConnectionId, args: &str, _is_self: bool) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    // VB6: ReadField(1, rData, 32) and ReadField(2, rData, 32) — space-delimited
    let parts: Vec<&str> = args.splitn(2, ' ').collect();
    if parts.len() < 2 { return; }
    let stat = parts[0].to_uppercase();
    let value_str = parts[1];
    let value: i64 = value_str.parse().unwrap_or(0);

    // Apply to self — /MOD only affects the invoker
    let target = conn_id;
    apply_mod_self(state, conn_id, target, &stat, value, value_str).await;
}

/// /SMOD <name> <stat> <value> — Modify another player's stats. Requires Director+.
/// VB6: Only a subset of /MOD subcommands (no AURA, ARMA, ESCU, CASCO, HAM, AGU, ATRI, FX).
pub(super) async fn handle_slash_smod(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIRECTOR => u.char_name.clone(),
        _ => return,
    };

    // VB6: ReadField(1)=name, ReadField(2)=subcommand, ReadField(3)=value — space delimited
    let parts: Vec<&str> = args.splitn(3, ' ').collect();
    if parts.len() < 3 { return; }
    let target_name = parts[0].replace('+', " ");
    let stat = parts[1].to_uppercase();
    let value: i64 = parts.get(2).and_then(|v| v.parse().ok()).unwrap_or(0);

    let target_upper = target_name.to_uppercase();
    // SHAY protection
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" { return; }

    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_msg_id(conn_id, 196, "");
            return;
        }
    };

    apply_mod_other(state, conn_id, target_conn, &stat, value).await;
}

/// /MOD apply — self-modification only. VB6: TCP_HandleData3.bas:1725-1877
/// Supports all subcommands: PART, AURA, FX, ATRI, ORO, EXP, BODY, HEAD,
/// CRI, CIU, LEVEL, CLASE, HAM, AGU, STA, MP, HP, ESCU, CASCO, ARMA.
pub(super) async fn apply_mod_self(state: &mut GameState, conn_id: ConnectionId, target: ConnectionId, stat: &str, value: i64, value_str: &str) {
    let (char_index, map, x, y) = match state.users.get(&target) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    match stat {
        "PART" => {
            if value <= 0 { return; }
            let pkt = binary_packets::write_create_fx(char_index as i16, value as i16, 0);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
        }
        "AURA" => {
            // VB6: UserList(tIndex).Char.AuraA = val(Arg2); SendUserAura
            if let Some(user) = state.users.get_mut(&target) {
                user.aura_a = value as i32;
            }
            if let Some(user) = state.users.get(&target) {
                let au_pkt = binary_packets::write_aura_update(
                    user.char_index.0 as i16,
                    user.aura_a as i16, user.aura_w as i16,
                    user.aura_e as i16, user.aura_r as i16, user.aura_c as i16,
                );
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &au_pkt);
            }
        }
        "FX" => {
            let pkt = binary_packets::write_create_fx(char_index as i16, value as i16, 20);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
        }
        "ATRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.attributes = [value as i32; 5];
            }
            state.send_msg_id(conn_id, 571, &value.to_string());
        }
        "ORO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.gold = value.max(0).min(MAX_GOLD);
            }
            send_stats_gold(state, target).await;
            state.send_msg_id(conn_id, 572, &value.to_string());
        }
        "EXP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.exp = value;
            }
            check_user_level(state, target).await;
            send_stats_exp(state, target).await;
            state.send_msg_id(conn_id, 572, &value.to_string());
        }
        "BODY" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.body = value as i32;
            }
            state.send_msg_id(conn_id, 573, &value.to_string());
            // VB6: ChangeUserChar → CP to map
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, value as i16, u.head as i16, u.heading as u8,
                u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
        }
        "HEAD" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.head = value as i32;
            }
            state.send_msg_id(conn_id, 574, &value.to_string());
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, u.body as i16, value as i16, u.heading as u8,
                u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
        }
        "CRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.criminales_matados = value as i32;
            }
            state.send_msg_id(conn_id, 575, &value.to_string());
        }
        "CIU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.ciudadanos_matados = value as i32;
            }
            state.send_msg_id(conn_id, 576, &value.to_string());
        }
        "LEVEL" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.level = value as i32;
            }
            state.send_msg_id(conn_id, 577, &value.to_string());
        }
        "CLASE" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.class = PlayerClass::from_str_or_default(value_str);
            }
            state.send_msg_id(conn_id, 578, &value_str.to_string());
        }
        "HAM" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_ham = value as i32;
                user.max_ham = value as i32;
            }
            state.send_msg_id(conn_id, 579, &value.to_string());
        }
        "AGU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_agua = value as i32;
                user.max_agua = value as i32;
            }
            state.send_msg_id(conn_id, 580, &value.to_string());
        }
        "STA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_sta = value as i32;
                user.max_sta = value as i32;
            }
            send_stats_sta(state, target).await;
            state.send_msg_id(conn_id, 581, &value.to_string());
        }
        "MP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_mana = value as i32;
                user.max_mana = value as i32;
            }
            send_stats_mana(state, target).await;
            state.send_msg_id(conn_id, 582, &value.to_string());
        }
        "HP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_hp = value as i32;
                user.max_hp = value as i32;
            }
            send_stats_hp(state, target).await;
            state.send_msg_id(conn_id, 583, &value.to_string());
        }
        "ESCU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.shield_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, u.body as i16, u.head as i16, u.heading as u8,
                u.weapon_anim as i16, value as i16, u.casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
        }
        "CASCO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.casco_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, u.body as i16, u.head as i16, u.heading as u8,
                u.weapon_anim as i16, u.shield_anim as i16, value as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
        }
        "ARMA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.weapon_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, u.body as i16, u.head as i16, u.heading as u8,
                value as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
        }
        _ => {
            state.send_msg_id(conn_id, 584, "");
        }
    }
}

/// /SMOD apply — modify another player. VB6: TCP_HandleData3.bas:2051-2163
/// Only supports: PART, ORO, EXP, BODY, HEAD, CRI, CIU, LEVEL, CLASE, STA, MP, HP.
/// All modifications are broadcast to admins via ||591 packets.
pub(super) async fn apply_mod_other(state: &mut GameState, gm_conn: ConnectionId, target: ConnectionId, stat: &str, value: i64) {
    let gm_name = state.users.get(&gm_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let target_name = state.users.get(&target).map(|u| u.char_name.clone()).unwrap_or_default();
    let (char_index, map, x, y) = match state.users.get(&target) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    match stat {
        "PART" => {
            if value <= 0 { return; }
            let pkt = binary_packets::write_create_fx(char_index as i16, value as i16, 0);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
        }
        "ORO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.gold = value.max(0).min(MAX_GOLD);
            }
            send_stats_gold(state, target).await;
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@oro@{}@{}", gm_name, target_name, value));
        }
        "EXP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.exp = value;
            }
            check_user_level(state, target).await;
            send_stats_exp(state, target).await;
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@experiencia@{}@{}", gm_name, target_name, value));
        }
        "BODY" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.body = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, value as i16, u.head as i16, u.heading as u8,
                u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@body@{}@{}", gm_name, target_name, value));
        }
        "HEAD" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.head = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = binary_packets::write_character_change(
                char_index as i16, u.body as i16, value as i16, u.heading as u8,
                u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &cp);
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@head@{}@{}", gm_name, target_name, value));
        }
        "CRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.criminales_matados = value as i32;
            }
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@criminales@{}@{}", gm_name, target_name, value));
        }
        "CIU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.ciudadanos_matados = value as i32;
            }
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@ciudadanos@{}@{}", gm_name, target_name, value));
        }
        "LEVEL" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.level = value as i32;
            }
            let pkt_level = binary_packets::write_level_update(value as u8);
            state.send_bytes(target, &pkt_level);
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@nivel@{}@{}", gm_name, target_name, value));
        }
        "CLASE" => {
            // VB6: class is a string, but we receive it as numeric here
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@clase@{}@{}", gm_name, target_name, value));
        }
        "STA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_sta = value as i32;
                user.max_sta = value as i32;
            }
            send_stats_sta(state, target).await;
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@energia@{}@{}", gm_name, target_name, value));
        }
        "MP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_mana = value as i32;
                user.max_mana = value as i32;
            }
            send_stats_mana(state, target).await;
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@mana@{}@{}", gm_name, target_name, value));
        }
        "HP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_hp = value as i32;
                user.max_hp = value as i32;
            }
            send_stats_hp(state, target).await;
            state.send_msg_id_to(SendTarget::ToAdmins, 591, &format!("{}@vida@{}@{}", gm_name, target_name, value));
        }
        _ => {
            state.send_msg_id(gm_conn, 584, "");
        }
    }
}

/// VB6 ClosestLegalPos: find the nearest walkable tile to (x,y) on map.
/// Searches in expanding rings: exact pos first, then ±1, ±2, etc.
// find_closest_legal_pos — moved to common.rs

/// /DV <name> — Devolver: warp player back to previous map position. VB6 TCP.bas:4190
/// Used for prison release / arena exit. Requires Semidios+.
pub(super) async fn handle_slash_dv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
            return;
        }
    };

    if let Some(u) = state.users.get_mut(&target_id) {
        u.jail_timer = 0;
    }

    // Warp to Ullathorpe (default home)
    warp_user(state, target_id, 1, 58, 45).await;
    send_warp_fx(state, target_id).await;
    state.send_console(target_id, "Un GM te ha devuelto a tu posicion anterior.", font_index::INFO);
    state.send_console(conn_id, &format!("Jugador '{}' devuelto.", target), font_index::INFO);
}

/// /INFO <name> — View detailed player info. VB6 TCP.bas:3716
/// Requires GranDios+. Shows class, level, gold, stats, IP, map, etc.
pub(super) async fn handle_slash_info(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no esta online.", target), font_index::INFO);
            return;
        }
    };

    let info_lines = match state.users.get(&target_id) {
        Some(u) => {
            vec![
                format!("--- Info de {} ---", u.char_name),
                format!("Clase: {} | Raza: {} | Nivel: {}", u.class, u.race, u.level),
                format!("HP: {}/{} | Mana: {}/{} | Sta: {}/{}", u.min_hp, u.max_hp, u.min_mana, u.max_mana, u.min_sta, u.max_sta),
                format!("Fuerza: {} | Agilidad: {} | Inteligencia: {} | Carisma: {} | Constitucion: {}",
                    u.attributes[0], u.attributes[1], u.attributes[2], u.attributes[3], u.attributes[4]),
                format!("Oro: {} | Banco: {} | Exp: {}", u.gold, u.bank_gold, u.exp),
                format!("Mapa: {} ({},{}) | IP: {}", u.pos_map, u.pos_x, u.pos_y, u.ip),
                format!("Criminal: {} | Muerto: {} | Privilegios: {}", u.criminal, u.dead, u.privileges),
                format!("Guild: {} | Pareja: {}", u.guild_index, u.pareja),
            ]
        }
        None => return,
    };

    for line in info_lines {
        state.send_console(conn_id, &format!("{}", line), font_index::INFO);
    }
}

/// /EDIT <name>@<levels> — Give player N level-ups. VB6 TCP.bas:3692
/// Requires GranDios+. Sets exp=ELU repeatedly to trigger level_up.
pub(super) async fn handle_slash_edit(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /EDIT nombre@niveles", font_index::INFO);
        return;
    }

    let target_name = parts[0].trim();
    let levels: i32 = parts[1].trim().parse().unwrap_or(0);
    if levels < 1 || levels > 50 {
        state.send_console(conn_id, "Niveles debe ser entre 1 y 50.", font_index::INFO);
        return;
    }

    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target_name), font_index::INFO);
            return;
        }
    };

    // VB6: Set exp = ELU for each level, then call CheckUserLevel
    for _ in 0..levels {
        let level = match state.users.get(&target_id) {
            Some(u) => u.level,
            None => return,
        };
        if level >= 50 { break; } // Max level cap

        // Get ELU for current level and set exp to trigger level-up
        let elu = state.exp_for_level(level);
        if elu <= 0 { break; }
        if let Some(u) = state.users.get_mut(&target_id) {
            u.exp = elu;
        }
        check_user_level(state, target_id).await;
    }

    send_stats_exp(state, target_id).await;
    let final_level = state.users.get(&target_id).map(|u| u.level).unwrap_or(0);
    state.send_console(conn_id, &format!("Jugador '{}' ahora es nivel {}.", target_name, final_level), font_index::INFO);
    state.send_console(target_id, "Un GM te ha subido de nivel!", font_index::INFO);
}

/// /LASTIP <name> — Show last IP of a user. Requires ADMIN+.
pub(super) async fn handle_slash_lastip(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.ip.clone()));

    match found {
        Some((name, ip)) => {
            state.send_console(conn_id, &format!("Ultima IP de {}: {}", name, ip), font_index::INFO);
        }
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
        }
    }
}

/// /CONSULTA <name> — Toggle en_consulta flag on target user (GM consultation mode). Requires CONSEJERO+.
/// When ON, NPCs ignore the user.
pub(super) async fn handle_slash_consulta(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
            return;
        }
    };

    let new_val = !state.users.get(&target_id).map(|u| u.en_consulta).unwrap_or(false);
    if let Some(user) = state.users.get_mut(&target_id) {
        user.en_consulta = new_val;
    }

    let status = if new_val { "en consulta" } else { "fuera de consulta" };
    let target_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_id, &format!("Estas {}.", status), font_index::INFO);
    state.send_console(conn_id, &format!("{} ahora esta {}.", target_name, status), font_index::INFO);

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} set {} to {}", gm_name, target_name, status);
}

/// /ONLINEREAL — List online Royal Army members. Requires CONSEJERO+.
pub(super) async fn handle_slash_onlinereal(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let members: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.armada_real)
        .map(|u| u.char_name.clone())
        .collect();

    if members.is_empty() {
        state.send_console(conn_id, "No hay miembros de la Armada Real online.", font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("Armada Real online ({}):", members.len()), font_index::INFO);
        state.send_console(conn_id, &members.join(", "), font_index::INFO);
    }
}

/// /ONLINECAOS — List online Chaos Legion members. Requires CONSEJERO+.
pub(super) async fn handle_slash_onlinecaos(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let members: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.fuerzas_caos)
        .map(|u| u.char_name.clone())
        .collect();

    if members.is_empty() {
        state.send_console(conn_id, "No hay miembros de la Legion del Caos online.", font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("Legion del Caos online ({}):", members.len()), font_index::INFO);
        state.send_console(conn_id, &members.join(", "), font_index::INFO);
    }
}

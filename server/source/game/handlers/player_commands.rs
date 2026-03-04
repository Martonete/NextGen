//! Remaining player slash commands: /desc, /comerciar, /boveda, /daroro,
//! /depositar, /retirar, /fmsg, /hora, /curar, /transform, /pmsg, etc.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::db::charfile;
use crate::data::objects::ObjType;
use super::common::*;
use crate::game::types::InventorySlot;
use super::{
    warp_user, revive_user, send_inventory_slot, send_full_inventory,
    iniciar_comercio_npc, iniciar_comercio_usuario, iniciar_banco, enviar_banco_inv,
    naked_body, send_to_party,
};

// GM / Admin command handlers (TCP_HandleData3.bas — GM section)
// =====================================================================
// =====================================================================
// Remaining player slash commands
// =====================================================================

/// /DESC <text> — Set character description. Saved to charfile.
pub(super) async fn handle_slash_desc(state: &mut GameState, conn_id: ConnectionId, desc: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    if desc.len() > 200 {
        state.send_console(conn_id, "Descripcion muy larga (max 200 caracteres).", font_index::INFO).await;
        return;
    }

    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", name));
    let chr = charpath.to_str().unwrap_or("");
    let _ = crate::config::write_var(chr, "CHAR", "Desc", desc);

    state.send_console(conn_id, "Descripcion actualizada.", font_index::INFO).await;
}

/// /COMERCIAR — Trade with target player (VB6: comManda). Requires mutual confirmation.
pub(super) async fn handle_slash_comerciar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, trading, target_user, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.trading, u.target_user, u.char_name.clone()),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO).await;
        return;
    }
    if trading {
        state.send_console(conn_id, "Ya estas comerciando.", font_index::INFO).await;
        return;
    }
    if target_user == 0 {
        state.send_console(conn_id, "Primero selecciona un jugador.", font_index::INFO).await;
        return;
    }

    // Check target is valid
    let target_ok = state.users.get(&target_user).map(|u| u.logged && !u.dead && !u.trading).unwrap_or(false);
    if !target_ok {
        state.send_console(conn_id, "El jugador no esta disponible.", font_index::INFO).await;
        return;
    }

    // VB6 mutual confirmation: check if target already requested trade with us
    let target_wants_us = state.users.get(&target_user)
        .map(|u| u.trade_partner == Some(conn_id) && !u.trading)
        .unwrap_or(false);

    if target_wants_us {
        // Both players want to trade with each other — initiate trade
        iniciar_comercio_usuario(state, conn_id, target_user).await;
    } else {
        // Mark our trade intention and notify target
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.trade_partner = Some(target_user);
        }
        let target_name = state.users.get(&target_user).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(target_user, &format!("{} quiere comerciar contigo. Usa /COMERCIAR para aceptar.", char_name), font_index::INFO).await;
        state.send_console(conn_id, &format!("Le has propuesto comerciar a {}.", target_name), font_index::INFO).await;
    }
}

/// /BOVEDA — Open bank vault. VB6: requires Banquero NPC target, distance <= 5, not dead.
pub(super) async fn handle_slash_boveda(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If Muerto = 1 Then ||3
    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: distance > 5 → ||13
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => { state.send_msg_id(conn_id, 9, "").await; return; }
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 5 || (u_y - npc_y).abs() > 5 {
        state.send_msg_id(conn_id, 13, "").await;
        return;
    }

    // VB6: NPCtype must be Banquero (4)
    if npc_type != crate::data::npcs::NpcType::Banker {
        return;
    }

    // VB6: IniciarDeposito — send bank inventory then INITBANCO
    enviar_banco_inv(state, conn_id).await;
    send_stats_gold(state, conn_id).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let pkt = binary_packets::write_bank_init(bank_gold as i32);
    state.send_bytes(conn_id, &pkt).await;
}

/// /DARORO <name>@<amount> — Give gold to another player. Min 10000.
pub(super) async fn handle_slash_daroro(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let target_name = read_field(1, args, '@');
    let amount_str = read_field(2, args, '@');
    let amount: i64 = amount_str.parse().unwrap_or(0);

    if amount < 10000 {
        state.send_console(conn_id, "Minimo 10.000 de oro.", font_index::INFO).await;
        return;
    }

    let (my_gold, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.char_name.clone()),
        _ => return,
    };

    if my_gold < amount {
        state.send_console(conn_id, "No tenes suficiente oro.", font_index::INFO).await;
        return;
    }

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO).await;
            return;
        }
    };

    // Transfer
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
    }
    if let Some(target) = state.users.get_mut(&target_conn) {
        target.gold += amount;
    }

    send_stats_gold(state, conn_id).await;
    send_stats_gold(state, target_conn).await;

    let target_real = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("Le diste {} de oro a {}.", amount, target_real), font_index::INFO).await;
    state.send_console(target_conn, &format!("{} te dio {} de oro.", my_name, amount), font_index::INFO).await;

    info!("[GOLD] {} gave {} gold to {}", my_name, amount, target_real);
}

/// /DEPOSITAR <amount> — Deposit gold at bank (slash command shortcut).
pub(super) async fn handle_slash_depositar(state: &mut GameState, conn_id: ConnectionId, amount: i64) {
    if amount <= 0 { return; }

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < amount {
        state.send_console(conn_id, "No tenes suficiente oro.", font_index::INFO).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
        user.bank_gold += amount;
    }
    send_stats_gold(state, conn_id).await;

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let bg_pkt = binary_packets::write_update_bank_gold(bank_gold as i32);
    state.send_bytes(conn_id, &bg_pkt).await;
    state.send_console(conn_id, &format!("Depositaste {} de oro.", amount), font_index::INFO).await;
}

/// /RETIRAR <amount> — Withdraw gold from bank (slash command shortcut).
pub(super) async fn handle_slash_retirar_oro(state: &mut GameState, conn_id: ConnectionId, amount: i64) {
    if amount <= 0 { return; }

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    if bank_gold < amount {
        state.send_console(conn_id, "No tenes suficiente oro en la boveda.", font_index::INFO).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold += amount;
        user.bank_gold -= amount;
    }
    send_stats_gold(state, conn_id).await;

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let bg_pkt = binary_packets::write_update_bank_gold(bank_gold as i32);
    state.send_bytes(conn_id, &bg_pkt).await;
    state.send_console(conn_id, &format!("Retiraste {} de oro.", amount), font_index::INFO).await;
}

/// /FMSG <msg> — Faction message (to all same-faction members).
pub(super) async fn handle_slash_fmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (name, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO).await;
        return;
    }

    let msg = format!("[Faccion] {}> {}", name, text);

    // Send to all users in the same faction
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && ((armada && u.armada_real) || (caos && u.fuerzas_caos)))
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::GUILD).await;
    }
}

/// /HORA — Show server time. GMs broadcast to all.
pub(super) async fn handle_slash_hora(state: &mut GameState, conn_id: ConnectionId) {
    let privileges = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);

    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let hours = (now / 3600) % 24;
    let minutes = (now / 60) % 60;
    let seconds = now % 60;
    let time_str = format!("{:02}:{:02}:{:02}", hours, minutes, seconds);

    if privileges > privilege_level::USER {
        // GMs broadcast to all
        state.send_console_to(SendTarget::ToAll, &format!("Hora del servidor: {}", time_str), font_index::INFO).await;
    } else {
        state.send_console(conn_id, &format!("Hora del servidor: {}", time_str), font_index::INFO).await;
    }
}

/// /NICK <name> — Check if a character exists online/offline (player command, NOT GM /CHANGENICK).
pub(super) async fn handle_slash_nick_check(state: &mut GameState, conn_id: ConnectionId, name: &str) {
    let target_upper = name.to_uppercase();

    if let Some(&_t_conn) = state.online_names.get(&target_upper) {
        state.send_console(conn_id, &format!("{} esta ONLINE.", name), font_index::INFO).await;
    } else {
        // Check if charfile exists
        let base = state.base_path.clone();
        let charpath = base.join("charfile").join(format!("{}.chr", name));
        if charpath.exists() {
            state.send_console(conn_id, &format!("{} existe pero esta OFFLINE.", name), font_index::INFO).await;
        } else {
            state.send_console(conn_id, &format!("{} no existe.", name), font_index::INFO).await;
        }
    }
}

/// /ADVERTENCIAS — View own warnings/penalties.
pub(super) async fn handle_slash_advertencias(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", name));
    let chr = charpath.to_str().unwrap_or("");

    let cant: i32 = crate::config::get_var(chr, "PENAS", "Cant").parse().unwrap_or(0);
    if cant == 0 {
        state.send_console(conn_id, "No tenes advertencias.", font_index::INFO).await;
        return;
    }

    state.send_console(conn_id, &format!("Tenes {} advertencias:", cant), font_index::INFO).await;
    for i in 1..=cant {
        let p = crate::config::get_var(chr, "PENAS", &format!("P{}", i));
        state.send_console(conn_id, &format!("{}: {}", i, p), font_index::INFO).await;
    }
}

/// /CURAR — Heal at Revividor NPC. VB6: requires Revividor NPC, distance <= 10, alive.
/// Removes poison, heals to full HP.
pub(super) async fn handle_slash_curar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: NPCtype must be Revividor (1) AND user must be alive
    let npc_type = state.get_npc(target_npc).map(|n| n.npc_type);
    if npc_type != Some(crate::data::npcs::NpcType::Reviver) || dead {
        return;
    }

    // VB6: distance > 10 → ||12
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_msg_id(conn_id, 12, "").await;
        return;
    }

    // VB6: Remove poison, heal to full HP, send ||398
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.poisoned = false;
        user.min_hp = user.max_hp;
    }
    send_stats_hp(state, conn_id).await;
    state.send_msg_id(conn_id, 398, "").await;
}

/// /DEMONIO or /ANGEL — VB6: requires CJerarquia=1, toggles transform.
/// Demon body=289 (criminal), Angel body=288 (citizen). FX=1 (FXWARP), Sound=SND_TRANSF.
pub(super) async fn handle_slash_transform(state: &mut GameState, conn_id: ConnectionId, cmd: &str) {
    let (dead, navigating, criminal, transformed, c_jerarquia) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.navigating, u.criminal, u.transformed, u.jerarquia_dios),
        _ => return,
    };

    // VB6: If Navegando=1 Or Muerto=1 Then ||397
    if navigating || dead {
        state.send_msg_id(conn_id, 397, "").await;
        return;
    }

    let (ci, map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if transformed {
        // VB6: Revert transformation — DarCuerpoDesnudo, reset head, FX + sound
        let (race, char_name, gender) = match state.users.get(&conn_id) {
            Some(u) => (u.race.clone(), u.char_name.clone(), u.gender),
            None => return,
        };
        let gender_str = gender.to_string();
        let orig_head = charfile::load_charfile(&state.pool, &char_name).await
            .map(|c| c.head).unwrap_or(0);
        let new_body = naked_body(&race, &gender_str);

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.body = new_body;
            user.head = orig_head;
            user.transformed = false;
        }

        // Broadcast appearance + FX
        let (heading, weapon, shield, casco) = match state.users.get(&conn_id) {
            Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
            None => return,
        };
        let cp = binary_packets::write_character_change(ci as i16, new_body as i16, orig_head as i16, heading as u8, weapon as i16, shield as i16, casco as i16, 0, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;
        let fx = binary_packets::write_create_fx(ci as i16, 1, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx).await;
        let snd = binary_packets::write_play_wave(3, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;

    } else if cmd == "/DEMONIO" && c_jerarquia >= 1 && criminal {
        // VB6: Transform to demon — body 289, head 0
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.body = 289;
            user.head = 0;
            user.transformed = true;
        }
        let (heading, weapon, shield, casco) = match state.users.get(&conn_id) {
            Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
            None => return,
        };
        let cp = binary_packets::write_character_change(ci as i16, 289, 0, heading as u8, weapon as i16, shield as i16, casco as i16, 0, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;
        let fx = binary_packets::write_create_fx(ci as i16, 1, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx).await;
        let snd = binary_packets::write_play_wave(3, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;

    } else if cmd == "/ANGEL" && c_jerarquia >= 1 && !criminal {
        // VB6: Transform to angel — body 288, head 0
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.body = 288;
            user.head = 0;
            user.transformed = true;
        }
        let (heading, weapon, shield, casco) = match state.users.get(&conn_id) {
            Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
            None => return,
        };
        let cp = binary_packets::write_character_change(ci as i16, 288, 0, heading as u8, weapon as i16, shield as i16, casco as i16, 0, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;
        let fx = binary_packets::write_create_fx(ci as i16, 1, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx).await;
        let snd = binary_packets::write_play_wave(3, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
    }
}

/// /PMSG <msg> — Party message to all party members.
pub(super) async fn handle_slash_pmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (party_idx, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.char_name.clone()),
        _ => {
            return;
        }
    };

    let msg = format!("[Party] {}> {}", name, text);
    let members: Vec<ConnectionId> = state.parties.get(party_idx as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();
    for &member_conn in &members {
        state.send_guild_chat_to(SendTarget::ToIndex(member_conn), &msg).await;
    }
}

// =====================================================================
// Missing VB6 player commands — Parity audit
// =====================================================================

/// /ONLINEGM — List online GMs. VB6 TCP.bas:3794
/// Hides Dios+ from non-Dios users. Sends N| packet with green color.
pub(super) async fn handle_slash_onlinegm(state: &mut GameState, conn_id: ConnectionId) {
    let my_priv = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.privileges,
        _ => return,
    };

    let mut names = Vec::new();
    for u in state.users.values() {
        if !u.logged || u.privileges <= privilege_level::USER { continue; }
        // VB6: Hide Dios+ from non-Dios users
        if u.privileges >= privilege_level::DIOS && my_priv < privilege_level::DIOS {
            continue;
        }
        names.push(u.char_name.clone());
    }

    if names.is_empty() {
        state.send_console(conn_id, "No hay GMs online.", font_index::INFO).await;
    } else {
        // VB6 sends via N| packet (green text, one name per line)
        state.send_console(conn_id, &format!("GMs online: {}", names.join(", ")), font_index::SERVER).await;
    }
}

/// /ONLINEMAP — List online players on same map. VB6 TCP.bas:3811
/// Hides Dios+ from non-Dios users. Sends ||750@names.
pub(super) async fn handle_slash_onlinemap(state: &mut GameState, conn_id: ConnectionId) {
    let (my_priv, my_map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.privileges, u.pos_map),
        _ => return,
    };

    let mut names = Vec::new();
    for u in state.users.values() {
        if !u.logged || u.pos_map != my_map { continue; }
        // VB6: Hide Dios+ from non-Dios users
        if u.privileges >= privilege_level::DIOS && my_priv < privilege_level::DIOS {
            continue;
        }
        names.push(u.char_name.clone());
    }

    let list = names.join(", ");
    state.send_msg_id(conn_id, 750, &list).await;
}

/// /ITEMNOBLE <type> — Exchange noble items. VB6 TCP_HandleData3.bas:560
/// Types: DIADEMA(1), ESPADA(2), ARMADURA(3), ANILLO(4)
/// Each requires specific items in inventory, gives a noble item in return.
pub(super) async fn handle_slash_itemnoble(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    if !user.es_noble {
        state.send_console(conn_id, "Debes ser noble para usar este comando.", font_index::INFO).await;
        return;
    }

    let tipo = args.trim().to_uppercase();

    // Noble item exchange table (VB6: ItemsNoble.dat)
    // Format: (required_items: [(obj_idx, qty)], reward_obj_idx)
    let (required, reward): (&[(i32, i32)], i32) = match tipo.as_str() {
        "DIADEMA" | "1" => (&[(848, 1), (849, 1), (850, 1)], 851),  // Noble diadem
        "ESPADA" | "2" => (&[(852, 1), (853, 1), (854, 1)], 855),   // Noble sword
        "ARMADURA" | "3" => (&[(856, 1), (857, 1), (858, 1)], 859), // Noble armor
        "ANILLO" | "4" => (&[(860, 1), (861, 1), (862, 1)], 863),   // Noble ring
        _ => {
            state.send_console(conn_id, "Uso: /ITEMNOBLE DIADEMA|ESPADA|ARMADURA|ANILLO", font_index::INFO).await;
            return;
        }
    };

    // Check all required items are in inventory
    for &(obj_idx, qty) in required {
        let has = state.users.get(&conn_id)
            .map(|u| u.inventory.iter().filter(|s| s.obj_index == obj_idx).map(|s| s.amount).sum::<i32>())
            .unwrap_or(0);
        if has < qty {
            state.send_console(conn_id, "No tenes los items necesarios.", font_index::INFO).await;
            return;
        }
    }

    // Remove required items
    for &(obj_idx, mut qty_needed) in required {
        if let Some(user) = state.users.get_mut(&conn_id) {
            for slot in user.inventory.iter_mut() {
                if slot.obj_index == obj_idx && qty_needed > 0 {
                    let take = slot.amount.min(qty_needed);
                    slot.amount -= take;
                    qty_needed -= take;
                    if slot.amount <= 0 {
                        slot.obj_index = 0;
                        slot.amount = 0;
                    }
                }
            }
        }
    }

    // Add reward item
    let empty_slot = state.users.get(&conn_id)
        .and_then(|u| u.inventory.iter().position(|s| s.obj_index == 0));

    if let Some(slot_idx) = empty_slot {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.inventory[slot_idx] = InventorySlot { obj_index: reward, amount: 1, equipped: false };
        }
        send_full_inventory(state, conn_id).await;
        state.send_console(conn_id, "Has obtenido tu item noble!", font_index::INFO).await;
    } else {
        state.send_console(conn_id, "No tenes espacio en el inventario.", font_index::INFO).await;
    }
}

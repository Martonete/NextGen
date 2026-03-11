//! Miscellaneous slash command handlers: mount, dismount, pet, messaging,
//! citizenship, travel, training, centinela, navigation, voting, marriage.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::data::objects::ObjType;
use crate::db::guilds;
use super::common::*;
use super::{
    warp_user, send_full_inventory,
    remove_pet_from_owner,
};

// =====================================================================
// Missing slash commands
// =====================================================================

/// /MONTAR — Mount pet.
pub(super) async fn handle_slash_montar(state: &mut GameState, conn_id: ConnectionId) {
    let has_mount = state.users.get(&conn_id).map(|u| u.nro_mascotas > 0).unwrap_or(false);
    if !has_mount {
        state.send_console(conn_id, "No tienes una montura.", font_index::INFO).await;
        return;
    }

    let already_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if already_mounted {
        state.send_console(conn_id, "Ya estas montado.", font_index::INFO).await;
        return;
    }

    // Simple mount: save body and change to mount body
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // VB6: Check first pet's NPC number and assign mount body
    let pet_idx = state.users.get(&conn_id).map(|u| u.mascotas_index[0]).unwrap_or(0);
    let npc_num = state.get_npc(pet_idx).map(|n| n.npc_number).unwrap_or(0);
    let mount_body = match npc_num {
        156 => 331, // Horse 1
        157 => 330, // Horse 2
        158 => 352, // Horse 3
        181 => 358, // Dragon/Special 1
        182 => 359, // Dragon/Special 2
        _ => 296,   // Generic mount fallback
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = true;
        user.montado_body = user.body;
        user.body = mount_body;
        user.weapon_anim = super::common::NINGUN_ARMA;
        user.shield_anim = super::common::NINGUN_ESCUDO;
        user.casco_anim = super::common::NINGUN_CASCO;
    }

    let cp = {
        let user = state.users.get(&conn_id).unwrap();
        binary_packets::write_character_change(
            user.char_index.0 as i16, user.body as i16, user.head as i16, user.heading as u8,
            super::common::NINGUN_ARMA as i16, super::common::NINGUN_ESCUDO as i16,
            super::common::NINGUN_CASCO as i16, 0, 0,
        )
    };
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;

    // Send mount state packet
    let char_index = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
    let usm_pkt = binary_packets::write_user_mount(char_index as i16, true);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &usm_pkt).await;

    state.send_console(conn_id, "Te has montado.", font_index::INFO).await;
}

/// /DESMONTAR — Dismount.
pub(super) async fn handle_slash_desmontar(state: &mut GameState, conn_id: ConnectionId) {
    let is_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if !is_mounted {
        state.send_console(conn_id, "No estas montado.", font_index::INFO).await;
        return;
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = false;
        user.levitando = false;
        user.body = user.montado_body;
        user.head = user.orig_head; // Restore head (flying mounts hide it)
    }

    // Restore equipped weapon/shield/helmet appearance
    let (weapon_anim, shield_anim, casco_anim) = get_equipped_anims(state, conn_id);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.weapon_anim = weapon_anim;
        user.shield_anim = shield_anim;
        user.casco_anim = casco_anim;
    }

    let cp = {
        let user = state.users.get(&conn_id).unwrap();
        binary_packets::write_character_change(
            user.char_index.0 as i16, user.body as i16, user.head as i16, user.heading as u8,
            user.weapon_anim as i16, user.shield_anim as i16, user.casco_anim as i16, 0, 0,
        )
    };
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;

    // Send mount state packet
    let char_index = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
    let usm_pkt = binary_packets::write_user_mount(char_index as i16, false);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &usm_pkt).await;

    state.send_console(conn_id, "Te has desmontado.", font_index::INFO).await;
}

// get_equipped_anims — moved to common.rs

/// /QUITARMASCOTA — Remove pet.
pub(super) async fn handle_slash_quitarmascota(state: &mut GameState, conn_id: ConnectionId) {
    let nro = state.users.get(&conn_id).map(|u| u.nro_mascotas).unwrap_or(0);
    if nro == 0 {
        state.send_console(conn_id, "No tienes mascotas.", font_index::INFO).await;
        return;
    }

    // Remove all pets
    if let Some(user) = state.users.get_mut(&conn_id) {
        for i in 0..3 {
            if user.mascotas_index[i] > 0 {
                // Kill the NPC
                let idx = user.mascotas_index[i];
                if let Some(npc) = state.npcs.get_mut(idx).and_then(|n| n.as_mut()) {
                    npc.min_hp = 0;
                }
            }
            user.mascotas_index[i] = 0;
            user.mascotas_type[i] = 0;
        }
        user.nro_mascotas = 0;
    }

    state.send_console(conn_id, "Mascota removida.", font_index::INFO).await;
}

/// /MSJ — Toggle private messages.
pub(super) async fn handle_slash_msj(state: &mut GameState, conn_id: ConnectionId) {
    let new_state = if let Some(user) = state.users.get_mut(&conn_id) {
        user.msj_privados = !user.msj_privados;
        user.msj_privados
    } else {
        return;
    };

    if new_state {
        state.send_console(conn_id, "Mensajes privados activados.", font_index::INFO).await;
    } else {
        state.send_console(conn_id, "Mensajes privados desactivados.", font_index::INFO).await;
    }
}

/// /CIUDADANIA — Set citizenship. VB6: requires Ciudadania NPC (type 13), distance <= 3.
/// Maps: 130→Inthak, 25→Thir/Ruvendel.
pub(super) async fn handle_slash_ciudadania(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.pos_map),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: distance > 3 → ||10
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 3 || (u_y - npc_y).abs() > 3 {
        state.send_msg_id(conn_id, 10, "").await;
        return;
    }

    // VB6: NPCtype must be Ciudadania (13)
    if npc_type != crate::data::npcs::NpcType::Citizenship { return; }

    // VB6: Set home based on map (130=Inthak, 25=Thir)
    let city = match map {
        130 => "Inthak",
        25 => "Thir",
        _ => {
            state.send_console(conn_id, "No estas en una ciudad valida.", font_index::INFO).await;
            return;
        }
    };

    let current_home = state.users.get(&conn_id).map(|u| u.hogar.clone()).unwrap_or_default();
    if current_home == city { return; } // VB6: If already same home, exit

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.hogar = city.to_string();
    }

    // VB6: ||318@<home>
    state.send_msg_id(conn_id, 318, &format!("{}", city)).await;
}

/// /VIAJAR — Travel to city via Traveler NPC.
/// VB6: TCP_HandleData3.bas lines 760-846
pub(super) async fn handle_slash_viajar(state: &mut GameState, conn_id: ConnectionId, city: &str) {
    let city_upper = city.trim().to_uppercase();

    // Validate city name (VB6 line 763)
    let valid = ["TANARIS", "ANVILMAR", "KAHLIMDOR", "THIR", "INTHAK", "JHUMBEL", "RUVENDEL", "HELKA"];
    if !valid.contains(&city_upper.as_str()) {
        state.send_console(conn_id, "Ciudad desconocida. Ciudades: Tanaris, Anvilmar, Kahlimdor, Thir, Inthak, Jhumbel, Ruvendel, Helka", font_index::INFO).await;
        return;
    }

    // Must have traveler NPC targeted (VB6 NpcType=12)
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.gold, u.target_npc_idx, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    let (dead, level, gold, target_npc, _map, _ux, _uy) = user_data;

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // Check NPC is a Traveler (type 12)
    let npc_ok = state.get_npc(target_npc).map(|n| n.npc_type == crate::data::npcs::NpcType::Traveler).unwrap_or(false);
    if !npc_ok { return; }

    // Gold cost: <30 = 1000, >=30 = 5000 (VB6 lines 778-788)
    let cost = if level < 30 { 1000i64 } else { 5000 };
    if gold < cost {
        let cost_str = if level < 30 { "1.000" } else { "5.000" };
        state.send_msg_id(conn_id, 215, &format!("{}", cost_str)).await;
        return;
    }

    // Inthak requires level 30+ (VB6 line 812)
    if city_upper == "INTHAK" && level < 30 {
        state.send_msg_id(conn_id, 542, "").await;
        return;
    }

    // VB6 exact destinations (TCP_HandleData3.bas lines 790-838)
    let (dest_map, dest_x, dest_y) = match city_upper.as_str() {
        "TANARIS" => (28, 54, 35),
        "ANVILMAR" => (29, 46, 85),
        "KAHLIMDOR" => (27, 50, 48),
        "THIR" => (25, 74, 45),
        "INTHAK" => (130, 50, 57),
        "JHUMBEL" => {
            // Random spawn in map 69 (VB6 lines 820-832)
            let roll = rand_simple_u32() % 5;
            match roll {
                0 => (69, 35 + (rand_simple_u32() % 8) as i32, 16 + (rand_simple_u32() % 9) as i32),
                1 => (69, 42 + (rand_simple_u32() % 6) as i32, 40 + (rand_simple_u32() % 9) as i32),
                2 => (69, 54 + (rand_simple_u32() % 14) as i32, 71 + (rand_simple_u32() % 6) as i32),
                3 => (69, 30 + (rand_simple_u32() % 8) as i32, 79 + (rand_simple_u32() % 7) as i32),
                _ => (69, 19 + (rand_simple_u32() % 6) as i32, 31 + (rand_simple_u32() % 4) as i32),
            }
        }
        "RUVENDEL" => (26, 51, 52),
        "HELKA" => (136, 52, 55),
        _ => return,
    };

    // Deduct gold (VB6 lines 840-844)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= cost;
    }
    send_stats_gold(state, conn_id).await;
    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;

    // VB6: WarpUserChar with FX=True sends warp sound + warp FX (FXWARP=1, SND_WARP=3)
    if let Some(user) = state.users.get(&conn_id) {
        if !user.admin_invisible {
            let char_idx = user.char_index.0;
            let map = user.pos_map;
            let x = user.pos_x;
            let y = user.pos_y;
            let snd_pkt = binary_packets::write_play_wave(3, x as u8, y as u8); // SND_WARP = 3
            let fx_pkt = binary_packets::write_create_fx(char_idx as i16, 1, 0); // FXWARP = 1
            // Send to area (others see it) AND directly to self (ensure self always gets it)
            state.send_sound_to_area(map, x, y, &snd_pkt).await;
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_pkt).await;
            // Also send directly to ensure self receives it (area detection may miss self right after warp)
            state.send_bytes(conn_id, &snd_pkt).await;
            state.send_bytes(conn_id, &fx_pkt).await;
        }
    }
}

/// /ENTRENAR — Open creature training list from trainer NPC.
/// VB6: requires Entrenador NPC (type 3), distance <= 10, not dead.
pub(super) async fn handle_slash_entrenar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If Muerto Then ||3
    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: distance > 10 → ||10
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_msg_id(conn_id, 10, "").await;
        return;
    }

    // VB6: NPCtype must be Entrenador (3)
    if npc_type != crate::data::npcs::NpcType::Trainer { return; }

    // VB6: EnviarListaCriaturas — sends LSTCRI<count>,<name1>,<name2>,...
    let npc_number = match state.get_npc(target_npc) {
        Some(npc) => npc.npc_number,
        None => return,
    };
    let npc_data = match state.game_data.npcs.get(npc_number) {
        Some(nd) => nd,
        None => return,
    };
    let mut creatures = format!("{},", npc_data.nro_criaturas);
    for c in &npc_data.criaturas {
        creatures.push_str(&c.npc_name);
        creatures.push(',');
    }
    let pkt = binary_packets::write_trainer_creature_list(&creatures);
    state.send_bytes(conn_id, &pkt).await;
}

/// /CENTINELA — Anti-AFK response (delegates to improved centinela handler).
pub(super) async fn handle_slash_centinela(state: &mut GameState, conn_id: ConnectionId, code: &str) {
    super::parity_gm::handle_centinela_improved(state, conn_id, code).await;
}

/// /IR — Premium travel.
pub(super) async fn handle_slash_ir(state: &mut GameState, conn_id: ConnectionId, destination: &str) {
    // Check premium status (not fully implemented — just accept for now)
    let dest_upper = destination.trim().to_uppercase();

    let (dest_map, dest_x, dest_y) = match dest_upper.as_str() {
        "INTHAK" => (1, 50, 50),
        "THIR" => (6, 50, 50),
        "RUVENDEL" => (11, 50, 50),
        _ => {
            state.send_console(conn_id, "Destino desconocido.", font_index::INFO).await;
            return;
        }
    };

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;
}

/// /VOTAR — Vote in poll.
pub(super) async fn handle_slash_votar(state: &mut GameState, conn_id: ConnectionId) {
    if !state.poll_active {
        state.send_console(conn_id, "No hay votacion activa.", font_index::INFO).await;
        return;
    }
    // Send poll options
    let mut msg = String::new();
    for i in 0..5 {
        msg.push_str(&state.poll_options[i]);
        msg.push(',');
    }
    let pkt = binary_packets::write_select_data(&msg);
    state.send_bytes(conn_id, &pkt).await;
}

/// /RESULTADOS — Poll results.
pub(super) async fn handle_slash_resultados(state: &mut GameState, conn_id: ConnectionId) {
    let total: i32 = state.poll_votes.iter().sum();
    let mut msg = String::from("Resultados de la votacion:");
    for i in 0..5 {
        if !state.poll_options[i].is_empty() {
            let pct = if total > 0 { (state.poll_votes[i] * 100) / total } else { 0 };
            msg.push_str(&format!(" {}: {} ({}%)", state.poll_options[i], state.poll_votes[i], pct));
        }
    }
    state.send_console(conn_id, &msg, font_index::INFO).await;
}


/// /CIRUJIA — Surgery (race change). VB6: requires cirujano NPC, distance <= 3.
pub(super) async fn handle_slash_cirujia(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    if target_npc == 0 { return; }

    // Check distance <= 3
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 3 || (u_y - npc_y).abs() > 3 {
        state.send_msg_id(conn_id, 158, "").await;
        return;
    }

    // VB6: NPCtype must be cirujano (19)
    if npc_type != crate::data::npcs::NpcType::Surgeon { return; }

    // VB6: sends CIRUJA<raza>,<genero>
    let (raza, genero) = match state.users.get(&conn_id) {
        Some(u) => (u.race, u.gender),
        None => return,
    };
    let raza_num = raza.index() as i32 + 1; // 1-based: Humano=1, Elfo=2, ElfoOscuro=3, Enano=4, Gnomo=5
    let pkt = binary_packets::write_cosmetic_surgery(raza_num as u8, genero as u8);
    state.send_bytes(conn_id, &pkt).await;
}




// ini_get, ini_write, user_has_items — moved to common.rs

/// FWO — Query house owner and price from Casas.dat.
pub(super) async fn handle_fwo(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa = read_field(1, payload, ',');

    let casas_path = state.base_path.join("dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }
    let precio = ini_get(&casas_path, &section, "Precio");
    let fecha = ini_get(&casas_path, &section, "Fecha");

    let data = format!("{},{},{}", dueno, precio, fecha);
    let pkt = binary_packets::write_auction_bid(&data);
    state.send_bytes(conn_id, &pkt).await;
}

/// CUC — Buy a house.
pub(super) async fn handle_cuc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa: i32 = read_field(1, payload, ',').parse().unwrap_or(0);

    let casas_path = state.base_path.join("dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }

    if dueno != "N/A" {
        state.send_msg_id(conn_id, 243, "").await;
        return;
    }

    let precio: i64 = ini_get(&casas_path, &section, "Precio").parse().unwrap_or(0);

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < precio {
        state.send_msg_id(conn_id, 215, &format!("{}", precio)).await;
        return;
    }

    if num_casa <= 0 {
        return;
    }

    // Key obj_index = 1093 + num_casa
    let key_index = 1093 + num_casa;
    if !add_item_to_user_inventory(state, conn_id, key_index, 1) {
        state.send_msg_id(conn_id, 108, "").await;
        return;
    }

    // Save owner to Casas.dat
    let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    ini_write(&casas_path, &section, "Dueno", &char_name);
    ini_write(&casas_path, &section, "Fecha", &chrono_like_date());

    // Broadcast to all
    state.send_msg_id_to(SendTarget::ToAll, 244, &format!("{}@{}", char_name, num_casa)).await;

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= precio;
    }
    send_stats_gold(state, conn_id).await;
    send_full_inventory(state, conn_id).await;
}

/// CNM — Rename pet/creature.
pub(super) async fn handle_cnm(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let nick = read_field(1, payload, ',');

    let pet_idx = state.users.get(&conn_id)
        .and_then(|u| if u.nro_mascotas > 0 { Some(u.mascotas_index[0]) } else { None })
        .unwrap_or(0);

    if pet_idx > 0 {
        if let Some(Some(npc)) = state.npcs.get_mut(pet_idx) {
            npc.name = nick.clone();
            state.send_console(conn_id, &format!("Mascota renombrada a: {}", nick), font_index::INFO).await;
        }
    } else {
        state.send_console(conn_id, "No tienes mascotas.", font_index::INFO).await;
    }
}


/// NANVAME — Clan name validated (notify admins).
pub(super) async fn handle_nanvame(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id_to(SendTarget::ToAdmins, 498, &format!("{}", name)).await;
}

/// NANVAMX — Clan name invalid (notify admins).
pub(super) async fn handle_nanvamx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id_to(SendTarget::ToAdmins, 499, &format!("{}", name)).await;
}

/// PCGF — Forward party/clan GUI data to target user.
pub(super) async fn handle_pcgf(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let peso = read_field(2, payload, ',');
    let target_idx: ConnectionId = read_field(3, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        let data = format!("{},{},{}", proceso, peso, sender_name);
        let pkt = binary_packets::write_cosmetic_pcgn(&data);
        state.send_bytes(target_idx, &pkt).await;
    }
}

/// PCWC — Forward party/clan window command to target user.
pub(super) async fn handle_pcwc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        let data = format!("{},{}", proceso, sender_name);
        let pkt = binary_packets::write_cosmetic_pcss(&data);
        state.send_bytes(target_idx, &pkt).await;
    }
}

/// PCCC — Forward party/clan caption to target user.
pub(super) async fn handle_pccc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let caption = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        let data = format!("{},{}", caption, sender_name);
        let pkt = binary_packets::write_cosmetic_pccc(&data);
        state.send_bytes(target_idx, &pkt).await;
    }
}

/// /VOTO — Vote for guild leader candidate.
pub(super) async fn handle_slash_voto(state: &mut GameState, conn_id: ConnectionId, candidate: &str) {
    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx <= 0 {
        state.send_console(conn_id, "No perteneces a ningun clan.", font_index::INFO).await;
        return;
    }

    // Simplified: just acknowledge the vote (full guild elections not implemented yet)
    state.send_msg_id(conn_id, 439, "").await;
}


// =====================================================================
// Marriage system — VB6 TCP_HandleData3.bas
// =====================================================================

/// /CASAR <name> — Marry another player. VB6 TCP_HandleData3.bas:1195
/// Both must be online, neither married, distance <= 3 tiles.
pub(super) async fn handle_slash_casar(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (my_name, my_map, my_x, my_y, my_pareja, my_dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.pareja.clone(), u.dead),
        _ => return,
    };

    if my_dead {
        state.send_msg_id(conn_id, 3, "").await; // Can't do this while dead
        return;
    }

    if !my_pareja.is_empty() {
        state.send_console(conn_id, &format!("Ya estas casado/a con {}.", my_pareja), font_index::INFO).await;
        return;
    }

    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_msg_id(conn_id, 196, "").await; // User not found
            return;
        }
    };

    if target_id == conn_id {
        state.send_console(conn_id, "No podes casarte con vos mismo.", font_index::INFO).await;
        return;
    }

    let (t_pareja, t_map, t_x, t_y, t_dead, t_name) = match state.users.get(&target_id) {
        Some(u) if u.logged => (u.pareja.clone(), u.pos_map, u.pos_x, u.pos_y, u.dead, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 196, "").await;
            return;
        }
    };

    if t_dead {
        state.send_console(conn_id, "El jugador esta muerto.", font_index::INFO).await;
        return;
    }

    if !t_pareja.is_empty() {
        state.send_console(conn_id, &format!("{} ya esta casado/a.", t_name), font_index::INFO).await;
        return;
    }

    // VB6: Distance check <= 3
    if my_map != t_map || (my_x - t_x).abs() > 3 || (my_y - t_y).abs() > 3 {
        state.send_console(conn_id, "Debes estar cerca del jugador (3 tiles).", font_index::INFO).await;
        return;
    }

    // Marry both
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.pareja = t_name.clone();
    }
    if let Some(u) = state.users.get_mut(&target_id) {
        u.pareja = my_name.clone();
    }

    // Broadcast marriage announcement
    state.send_msg_id_to(SendTarget::ToAll, 526, &format!("{}@{}", my_name, t_name)).await;

    state.send_console(conn_id, &format!("Te has casado con {}!", t_name), font_index::INFO).await;
    state.send_console(target_id, &format!("Te has casado con {}!", my_name), font_index::INFO).await;
}

/// /DIVORCIARSE — Divorce from spouse. VB6 TCP_HandleData3.bas:1262
pub(super) async fn handle_slash_divorciarse(state: &mut GameState, conn_id: ConnectionId) {
    let (my_name, my_pareja) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.pareja.clone()),
        _ => return,
    };

    if my_pareja.is_empty() {
        state.send_console(conn_id, "No estas casado/a.", font_index::INFO).await;
        return;
    }

    // Clear our marriage
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.pareja.clear();
    }

    // Clear spouse's marriage (if online)
    if let Some(spouse_id) = state.find_user_by_name(&my_pareja) {
        if let Some(u) = state.users.get_mut(&spouse_id) {
            u.pareja.clear();
        }
        state.send_console(spouse_id, &format!("{} se ha divorciado de ti.", my_name), font_index::INFO).await;
    }

    // Broadcast divorce
    state.send_msg_id_to(SendTarget::ToAll, 527, &format!("{}@{}", my_name, my_pareja)).await;

    state.send_console(conn_id, &format!("Te has divorciado de {}.", my_pareja), font_index::INFO).await;
}

// =====================================================================
// Gran Poder system — VB6 modGranPoder
// =====================================================================


// =====================================================================
// Integration tests — full client login flow
// =====================================================================


//! Inventory use-item handlers (potions, food, keys, scrolls, etc.).
//! Split from inventory.rs for file size management.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::class_race::{PlayerClass, PlayerRace};
use crate::game::types::{GameState, SendTarget, MAX_INVENTORY_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::{font_index, fields::read_field};
use crate::protocol::binary_packets;
use crate::data::objects::{ObjData, ObjType};
use crate::game::handlers::common::*;
use crate::game::constants::*;
use crate::game::handlers::{
    send_inventory_slot, send_full_inventory, build_anm_packet,
    warp_user, revive_user, naked_body, user_die,
    iniciar_comercio_npc, iniciar_banco,
};
use crate::game::handlers::skills::skill_id;
use super::equip::unequip_slot;

/// USA<slot> — Use item from inventory.
/// QSA<slot>,<visible> — Use item via double-click on inventory picture.
/// VB6: picInv_DblClick sends QSA<slot>,<True|False>.
/// If InvenVisible = "FALSO", it's a hack attempt (using items with inv hidden).
pub(crate) async fn handle_use_item_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // VB6: HandleUseItem exits early if Meditando
    let is_meditating = state.users.get(&conn_id).map(|u| u.meditating).unwrap_or(false);
    if is_meditating {
        return;
    }

    let payload = strip_opcode(data, 3);
    let slot_str = read_field(1, payload, ',');
    let visible_str = read_field(2, payload, ',');

    // Anti-cheat: if inventory window is hidden, it's a hack
    if visible_str.eq_ignore_ascii_case("falso") || visible_str.eq_ignore_ascii_case("false") {
        info!("[CHEAT] QSA with hidden inventory from #{}", conn_id);
        return;
    }

    let max_slots = state.users.get(&conn_id).map(|u| u.current_inventory_slots).unwrap_or(MAX_INVENTORY_SLOTS);
    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= max_slots => s,
        _ => return,
    };
    let idx = slot - 1;

    let (obj_index, _amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let inv_slot = &u.inventory[idx];
            if inv_slot.obj_index == 0 { return; }
            (inv_slot.obj_index, inv_slot.amount)
        }
        _ => return,
    };

    // VB6: Using a projectile weapon sends WorkRequestTarget(Proyectiles) to enter target mode
    let is_projectile = state.get_object(obj_index).map(|o| o.proyectil).unwrap_or(false);
    if is_projectile {
        let is_dead = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);
        if is_dead {
            state.send_msg_id(conn_id, 5, "");
            return;
        }
        let is_equipped = state.users.get(&conn_id)
            .map(|u| u.inventory[idx].equipped).unwrap_or(false);
        if !is_equipped {
            state.send_console(conn_id, "Antes de usar la herramienta deberías equipártela.", font_index::INFO);
            return;
        }
        // VB6: WriteMultiMessage(UserIndex, eMessages.WorkRequestTarget, eSkill.Proyectiles)
        let pkt = binary_packets::write_work_request_target(skill_id::PROYECTILES as u8);
        state.send_bytes(conn_id, &pkt);
        return;
    }

    // Anti-cheat: PuedoClickear — checks interval_click AND sets both
    // interval_click=6 and interval_poteo=8 (cross-locking, matches VB6)
    if !puede_clickear(state, conn_id) { return; }

    // Delegate to inner use-item with from_click=true so it skips
    // puede_potear() (already set by puede_clickear above)
    let usa_data = format!("USA{}", slot);
    handle_use_item_inner(state, conn_id, &usa_data, true).await;
}

pub(crate) async fn handle_use_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    handle_use_item_inner(state, conn_id, data, false).await;
}

/// Inner use-item logic. `from_click` = true when called from QSA (double-click),
/// which means puede_clickear() already set both interval_click and interval_poteo,
/// so we skip the puede_potear() check to avoid double-blocking.
pub(crate) async fn handle_use_item_inner(state: &mut GameState, conn_id: ConnectionId, data: &str, from_click: bool) {
    // VB6: HandleUseItem exits early if Meditando
    let is_meditating = state.users.get(&conn_id).map(|u| u.meditating).unwrap_or(false);
    if is_meditating {
        return;
    }

    let slot_str = strip_opcode(data, 3);
    let max_slots = state.users.get(&conn_id).map(|u| u.current_inventory_slots).unwrap_or(MAX_INVENTORY_SLOTS);
    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= max_slots => s,
        _ => return,
    };
    let idx = slot - 1;

    let (obj_index, amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let inv = &u.inventory[idx];
            (inv.obj_index, inv.amount)
        }
        _ => return,
    };

    if obj_index == 0 || amount <= 0 {
        return;
    }

    // Death check — only ResurrectPotion can be used while dead
    let is_dead = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);

    let obj_data = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if is_dead && obj_data.obj_type != ObjType::ResurrectPotion && obj_data.obj_type != ObjType::Boat {
        state.send_msg_id(conn_id, 5, ""); // VB6 TEXTO5: muerto, no puede usar items
        return;
    }

    match obj_data.obj_type {
        ObjType::UseOnce | ObjType::Potion => {
            // Anti-cheat: check potion cooldown
            // When from_click=true, puede_clickear() already set both cooldowns
            if !from_click && !puede_potear(state, conn_id) {
                return;
            }

            // Black potion (TipoPocion=6) — instant death (VB6: Pocion Negra)
            // Only affects regular players, not GMs
            if obj_data.tipo_pocion == 6 {
                let is_gm = state.users.get(&conn_id).map(|u| u.privileges > 0).unwrap_or(false);
                if !is_gm {
                    state.send_console(conn_id, "Sientes un gran mareo y pierdes el conocimiento.", font_index::FIGHT);
                    // Consume item first
                    if let Some(user) = state.users.get_mut(&conn_id) {
                        let new_amt = user.inventory[idx].amount - 1;
                        if new_amt <= 0 {
                            user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                        } else {
                            user.inventory[idx].amount = new_amt;
                        }
                    }
                    send_inventory_slot(state, conn_id, idx).await;
                    // Kill the user
                    user_die(state, conn_id, None).await;
                    return;
                }
            }

            // Apply potion/food effect
            apply_consumable(state, conn_id, &obj_data);

            // VB6 sound differs by type:
            // - otUseOnce (food/apples): SOUND_COMIDA=7
            // - otPociones: SND_BEBER=46
            let snd_id = if obj_data.obj_type == ObjType::UseOnce { 7 } else { 46 };
            let (snd_map, snd_x, snd_y) = state.users.get(&conn_id)
                .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0, 0, 0));
            let pkt_wave = binary_packets::write_play_wave(snd_id as u8, snd_x as i16, snd_y as i16);
            state.send_data_bytes(SendTarget::ToArea { map: snd_map, x: snd_x, y: snd_y }, &pkt_wave);

            // Consume one
            if let Some(user) = state.users.get_mut(&conn_id) {
                let new_amt = user.inventory[idx].amount - 1;
                if new_amt <= 0 {
                    user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                } else {
                    user.inventory[idx].amount = new_amt;
                }
            }

            // Send updated slot and stats
            send_inventory_slot(state, conn_id, idx).await;
            send_stats_hp(state, conn_id).await;
            send_stats_mana(state, conn_id).await;
            send_stats_sta(state, conn_id).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Drink => {
            // Drinks restore thirst (min_agua), not stamina
            if let Some(user) = state.users.get_mut(&conn_id) {
                let new_agua = (user.min_agua + obj_data.min_agua).min(user.max_agua);
                user.min_agua = new_agua;
                let new_amt = user.inventory[idx].amount - 1;
                if new_amt <= 0 {
                    user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                } else {
                    user.inventory[idx].amount = new_amt;
                }
            }
            // VB6: SND_BEBER (46)
            let (snd_map, snd_x, snd_y) = state.users.get(&conn_id)
                .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0, 0, 0));
            let pkt_wave = binary_packets::write_play_wave(46, snd_x as i16, snd_y as i16);
            state.send_data_bytes(SendTarget::ToArea { map: snd_map, x: snd_x, y: snd_y }, &pkt_wave);
            send_inventory_slot(state, conn_id, idx).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Key => {
            // Keys are used on doors — they're not "used" from inventory directly
            // The door interaction happens via LC/RC on a door tile
            state.send_console(conn_id, "Esta llave sirve para abrir una puerta", font_index::INFO);
        }
        ObjType::Boat => {
            // VB6: InvUsuario.bas Case eOBJType.otBarcos + Trabajo.bas DoNavega
            let (is_navigating, user_map, user_x, user_y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.navigating, u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };

            // VB6 mount check: LegalPos(adjacent, PuedeAgua=True) checks water + not blocked + no user/NPC
            // VB6 dismount: NO land check — always allowed (player may get stuck on water)
            if !is_navigating {
                // Mount — must have water tile adjacent (VB6: 4 cardinal LegalPos checks)
                let has_water_nearby = (1..=4).any(|h| {
                    let (dx, dy) = world::heading_to_offset(h);
                    let nx = user_x + dx;
                    let ny = user_y + dy;
                    // VB6 LegalPos(map, x, y, PuedeAgua=True): not blocked AND has water AND no user/NPC
                    !state.is_tile_blocked(user_map, nx, ny)
                        && state.hay_agua(user_map, nx, ny)
                        && state.world.grids.get(&user_map)
                            .map(|g| g.is_tile_free(nx, ny)).unwrap_or(false)
                });
                if !has_water_nearby {
                    state.send_msg_id(conn_id, 106, ""); // TEXTO106
                    return;
                }
            }

            // Auto-dismount from mount if mounted (before boarding boat)
            let is_mounted_for_boat = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
            if is_mounted_for_boat && !is_navigating {
                // Dismount mount — restore body, clear mount state
                if let Some(u) = state.users.get_mut(&conn_id) {
                    let saved_body = u.montado_body;
                    let saved_head = u.orig_head;
                    u.montado = false;
                    u.levitando = false;
                    u.body = saved_body;
                    u.head = saved_head;
                }
                let (weap_a, shield_a, casco_a) = get_equipped_anims(state, conn_id);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.weapon_anim = weap_a;
                    u.shield_anim = shield_a;
                    u.casco_anim = casco_a;
                    // Restore auras from equipped items
                    for slot_idx in 0..MAX_INVENTORY_SLOTS {
                        if !u.inventory[slot_idx].equipped { continue; }
                        let oi = u.inventory[slot_idx].obj_index;
                        if oi < 1 { continue; }
                        if let Some(obj) = state.game_data.objects.get((oi - 1) as usize) {
                            if obj.crea_aura > 0 {
                                match obj.obj_type {
                                    ObjType::Armor => u.aura_a = obj.crea_aura,
                                    ObjType::Weapon => u.aura_w = obj.crea_aura,
                                    ObjType::Shield => u.aura_e = obj.crea_aura,
                                    ObjType::Helmet => u.aura_c = obj.crea_aura,
                                    ObjType::Tool => u.aura_r = obj.crea_aura,
                                    _ => {}
                                }
                            }
                        }
                    }
                }
                // Notify: mount off
                let mnt_ci = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
                let pkt_usm_off = binary_packets::write_user_mount(mnt_ci as i16, false);
                state.send_data_bytes(SendTarget::ToArea { map: user_map, x: user_x, y: user_y }, &pkt_usm_off);
                let pkt_mvol_off = binary_packets::write_levitate(mnt_ci as i16, false);
                state.send_data_bytes(SendTarget::ToArea { map: user_map, x: user_x, y: user_y }, &pkt_mvol_off);
                let (pkt_cd, pkt_au) = {
                    let u = state.users.get(&conn_id).unwrap();
                    (crate::game::handlers::common::build_cd_binary(u), crate::game::handlers::common::build_aura_binary(u))
                };
                state.send_data_bytes(SendTarget::ToArea { map: user_map, x: user_x, y: user_y }, &pkt_cd);
                state.send_data_bytes(SendTarget::ToArea { map: user_map, x: user_x, y: user_y }, &pkt_au);
            }

            // VB6 DoNavega: If hidden (Oculto), clear hiding. Only send SetInvisible(false)
            // if not also invisible from spell (invisible keeps you hidden on clients).
            let (was_hidden, is_invisible, char_index_val, map_for_nover) = match state.users.get(&conn_id) {
                Some(u) => (u.hidden, u.invisible, u.char_index.0, u.pos_map),
                None => return,
            };
            if was_hidden {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.hidden = false;
                    u.counter_oculto = 0;
                }
                // Only send SetInvisible(false) if spell invisibility is NOT active
                if !is_invisible {
                    let pkt_invis = binary_packets::write_set_invisible(char_index_val as i16, false, 0);
                    state.send_data_bytes(SendTarget::ToMap(map_for_nover), &pkt_invis);
                    state.send_console(conn_id, "¡Has vuelto a ser visible!", font_index::INFO);
                }
            }

            if is_navigating {
                // === DISMOUNT (VB6 DoNavega else branch) ===
                // No land check — VB6 allows dismounting anywhere
                let equip_info = state.users.get(&conn_id).map(|u| {
                    let get_inv_obj = |slot: usize| -> i32 {
                        if slot >= 1 && slot <= u.inventory.len() { u.inventory[slot - 1].obj_index } else { 0 }
                    };
                    (
                        get_inv_obj(u.equip.armor),
                        get_inv_obj(u.equip.weapon),
                        get_inv_obj(u.equip.shield),
                        get_inv_obj(u.equip.helmet),
                        u.old_head, u.dead, u.race, u.gender,
                    )
                });
                if let Some((armor_obj, weapon_obj, shield_obj, helmet_obj, saved_head, dead, race, gender)) = equip_info {
                    let armor_body = state.get_object(armor_obj).map(|o| o.num_ropaje).unwrap_or(0);
                    let weapon_anim = state.get_object(weapon_obj).map(|o| o.weapon_anim).unwrap_or(0);
                    let shield_anim = state.get_object(shield_obj).map(|o| o.shield_anim).unwrap_or(0);
                    let casco_anim = state.get_object(helmet_obj).map(|o| o.casco_anim).unwrap_or(0);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.navigating = false;
                        u.barco_slot = 0;
                        if !dead {
                            u.head = saved_head;
                            u.body = if armor_body > 0 { armor_body } else { naked_body(race, gender) };
                            u.weapon_anim = weapon_anim;
                            u.shield_anim = shield_anim;
                            u.casco_anim = casco_anim;
                        } else {
                            // VB6: dead dismount → ghost body/head, no equipment
                            u.body = DEAD_BODY_NEUTRAL;
                            u.head = DEAD_HEAD_NEUTRAL;
                            u.weapon_anim = crate::game::handlers::common::NINGUN_ARMA;
                            u.shield_anim = crate::game::handlers::common::NINGUN_ESCUDO;
                            u.casco_anim = crate::game::handlers::common::NINGUN_CASCO;
                        }
                    }
                }
            } else {
                // === MOUNT (VB6 DoNavega if branch) ===
                let ropaje = obj_data.num_ropaje;
                let is_dead_mount = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.old_head = u.head;
                    u.head = 0;
                    u.weapon_anim = crate::game::handlers::common::NINGUN_ARMA;
                    u.shield_anim = crate::game::handlers::common::NINGUN_ESCUDO;
                    u.casco_anim = crate::game::handlers::common::NINGUN_CASCO;
                    u.navigating = true;
                    u.barco_slot = idx + 1; // VB6 BarcoSlot (1-based)
                    if is_dead_mount {
                        // VB6: dead user gets ghost boat body (iFragataFantasmal = 87)
                        u.body = 87;
                    } else if ropaje > 0 {
                        u.body = ropaje;
                    }
                }
            }

            // VB6 DoNavega packets (order matters):
            // 1. ChangeUserChar → CP packet to area (including self)
            // 2. NAVEG to self
            // 3. NVG<charindex>,<flag> to ALL players
            let (cp_bytes, nav_ci, nav_flag, map, bx, by) = match state.users.get(&conn_id) {
                Some(u) => {
                    let nav_flag = u.navigating;
                    let cp = binary_packets::write_character_change(
                        u.char_index.0 as i16, u.body as i16, u.head as i16, u.heading as u8,
                        u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
                    );
                    (cp, u.char_index.0, nav_flag, u.pos_map, u.pos_x, u.pos_y)
                }
                None => return,
            };
            // CP to area (VB6 SendToUserArea = includes self)
            state.send_data_bytes(SendTarget::ToArea { map, x: bx, y: by }, &cp_bytes);
            // NAVEG to self (toggle client navigation state)
            let pkt_nav = binary_packets::write_navigate_toggle();
            state.send_bytes(conn_id, &pkt_nav);
            // NVG broadcast to all players
            let pkt_nvg = binary_packets::write_navigate_broadcast(nav_ci as i16, nav_flag);
            state.send_data_bytes(SendTarget::ToAll, &pkt_nvg);
        }
        ObjType::Instrument => {
            // VB6: Play music instrument — broadcast TW<Snd1> to area
            let wav = obj_data.snd1; // VB6 uses Snd1 field for instrument sound ID
            let (map, x, y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };
            if wav > 0 {
                let pkt_wave = binary_packets::write_play_wave(wav as u8, x as i16, y as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_wave);
            }
            state.send_console(conn_id, "Tocas una melodia", font_index::INFO);
        }
        ObjType::Scroll => {
            // Learn spell from scroll
            let spell_id = obj_data.hechizo_index;
            if spell_id <= 0 { return; }

            // VB6: Check if user already knows this spell
            let already_known = state.users.get(&conn_id)
                .map(|u| u.spells.iter().any(|&s| s == spell_id))
                .unwrap_or(false);
            if already_known {
                state.send_msg_id(conn_id, 182, ""); // TEXTO182: Ya tenes ese hechizo
                return;
            }

            // Find empty spell slot
            let slot = match state.users.get(&conn_id) {
                Some(u) => u.spells.iter().position(|&s| s == 0),
                None => return,
            };

            if let Some(slot_idx) = slot {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.spells[slot_idx] = spell_id;
                    let new_amt = u.inventory[idx].amount - 1;
                    if new_amt <= 0 {
                        u.inventory[idx].obj_index = 0;
        u.inventory[idx].amount = 0;
        u.inventory[idx].equipped = false;
                    } else {
                        u.inventory[idx].amount = new_amt;
                    }
                }
                send_inventory_slot(state, conn_id, idx).await;
                let spell_name = state.get_spell(spell_id)
                    .map(|s| s.nombre.clone())
                    .unwrap_or_default();
                // Send SHS to update the spell slot on client
                let shs_slot = slot_idx + 1; // 1-based
                let pkt_shs = binary_packets::write_change_spell_slot(shs_slot as u8, spell_id as i16, &spell_name);
                state.send_bytes(conn_id, &pkt_shs);
                state.send_msg_id(conn_id, 832, &spell_name); // TEXTO832
            } else {
                state.send_msg_id(conn_id, 181, ""); // TEXTO181
            }
        }
        ObjType::EmptyBottle => {
            // Fill at water source — simplified, just inform
            state.send_msg_id(conn_id, 103, ""); // TEXTO103: No hay agua allí
        }
        ObjType::FullBottle => {
            // Drink from bottle → restore thirst, swap to empty bottle variant
            if let Some(user) = state.users.get_mut(&conn_id) {
                let new_agua = (user.min_agua + obj_data.min_agua).min(user.max_agua);
                user.min_agua = new_agua;
                // Swap to empty variant (IndexAbierta stores the empty bottle obj index)
                let empty_index = obj_data.index_abierta;
                if empty_index > 0 {
                    user.inventory[idx].obj_index = empty_index;
                    // Amount stays the same (1 full bottle → 1 empty bottle)
                } else {
                    // No empty variant, just consume
                    let new_amt = user.inventory[idx].amount - 1;
                    if new_amt <= 0 {
                        user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                    } else {
                        user.inventory[idx].amount = new_amt;
                    }
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Money => {
            // Gold pile: add to gold, remove from inventory
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.gold += amount as i64;
                user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_stats_gold(state, conn_id).await;
        }
        ObjType::ResurrectPotion => {
            // Resurrection potion — can only use while dead
            if !is_dead {
                state.send_msg_id(conn_id, 117, ""); // TEXTO117: Debes estar muerto para utilizar esta poción
                return;
            }
            // Consume the item first
            if let Some(user) = state.users.get_mut(&conn_id) {
                let new_amt = user.inventory[idx].amount - 1;
                if new_amt <= 0 {
                    user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                } else {
                    user.inventory[idx].amount = new_amt;
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            // Use shared revive logic (restores body, head, sends CFF + CP)
            revive_user(state, conn_id).await;
            state.send_msg_id(conn_id, 119, ""); // TEXTO119: Te has resucitado
        }
        ObjType::Mount => {
            // Mount/dismount — land mounts use montado (NOT navigating, which is for boats)
            let ropaje = obj_data.num_ropaje;
            let is_flying = obj_data.es_voladora;

            // Pre-checks
            let (is_dead, is_navigating, is_mounted) = state.users.get(&conn_id)
                .map(|u| (u.dead, u.navigating, u.montado))
                .unwrap_or((false, false, false));
            if is_dead {
                return;
            }

            // Auto-dismount from boat if navigating
            if is_navigating {
                let equip_info = state.users.get(&conn_id).map(|u| {
                    let get_inv_obj = |slot: usize| -> i32 {
                        if slot >= 1 && slot <= u.inventory.len() { u.inventory[slot - 1].obj_index } else { 0 }
                    };
                    (
                        get_inv_obj(u.equip.armor),
                        get_inv_obj(u.equip.weapon),
                        get_inv_obj(u.equip.shield),
                        get_inv_obj(u.equip.helmet),
                        u.old_head, u.race, u.gender,
                    )
                });
                if let Some((armor_obj, weapon_obj, shield_obj, helmet_obj, saved_head, race, gender)) = equip_info {
                    let armor_body = state.get_object(armor_obj).map(|o| o.num_ropaje).unwrap_or(0);
                    let weapon_anim = state.get_object(weapon_obj).map(|o| o.weapon_anim).unwrap_or(0);
                    let shield_anim = state.get_object(shield_obj).map(|o| o.shield_anim).unwrap_or(0);
                    let casco_anim = state.get_object(helmet_obj).map(|o| o.casco_anim).unwrap_or(0);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.navigating = false;
                        u.barco_slot = 0;
                        u.head = saved_head;
                        u.body = if armor_body > 0 { armor_body } else { naked_body(race, gender) };
                        u.weapon_anim = weapon_anim;
                        u.shield_anim = shield_anim;
                        u.casco_anim = casco_anim;
                    }
                }
                // Notify clients: navigation off
                let (nav_ci, nav_map, nav_x, nav_y) = match state.users.get(&conn_id) {
                    Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
                    None => return,
                };
                let cp_nav = {
                    let u = state.users.get(&conn_id).unwrap();
                    binary_packets::write_character_change(
                        u.char_index.0 as i16, u.body as i16, u.head as i16, u.heading as u8,
                        u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
                    )
                };
                state.send_data_bytes(SendTarget::ToArea { map: nav_map, x: nav_x, y: nav_y }, &cp_nav);
                let pkt_nav = binary_packets::write_navigate_toggle();
                state.send_bytes(conn_id, &pkt_nav);
                let pkt_nvg_off = binary_packets::write_navigate_broadcast(nav_ci as i16, false);
                state.send_data_bytes(SendTarget::ToAll, &pkt_nvg_off);
            }

            let (map, x, y, char_index) = match state.users.get(&conn_id) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index),
                None => return,
            };

            if is_mounted {
                // Dismount — check tile is not water (can't dismount in water)
                if state.hay_agua(map, x, y) {
                    state.send_console(conn_id, "No puedes desmontar aqui.", font_index::INFO);
                    return;
                }

                if let Some(u) = state.users.get_mut(&conn_id) {
                    let saved_body = u.montado_body;
                    let saved_head = u.orig_head;
                    u.montado = false;
                    u.levitando = false;
                    u.body = saved_body;
                    u.head = saved_head; // Restore head (flying mounts hide it)
                }

                // Restore equipped weapon/shield/helmet appearance + auras
                let (weapon_anim, shield_anim, casco_anim) = get_equipped_anims(state, conn_id);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.weapon_anim = weapon_anim;
                    u.shield_anim = shield_anim;
                    u.casco_anim = casco_anim;
                    // Restore auras from equipped items
                    for slot_idx in 0..MAX_INVENTORY_SLOTS {
                        if !u.inventory[slot_idx].equipped { continue; }
                        let oi = u.inventory[slot_idx].obj_index;
                        if oi < 1 { continue; }
                        if let Some(obj) = state.game_data.objects.get((oi - 1) as usize) {
                            if obj.crea_aura > 0 {
                                match obj.obj_type {
                                    ObjType::Armor => u.aura_a = obj.crea_aura,
                                    ObjType::Weapon => u.aura_w = obj.crea_aura,
                                    ObjType::Shield => u.aura_e = obj.crea_aura,
                                    ObjType::Helmet => u.aura_c = obj.crea_aura,
                                    ObjType::Tool => u.aura_r = obj.crea_aura,
                                    _ => {}
                                }
                            }
                        }
                    }
                }
            } else {
                // Mount up — VB6: keep helmet visible, hide weapon/shield only
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.montado = true;
                    u.montado_body = u.body;
                    if ropaje > 0 {
                        u.body = ropaje;
                    }
                    u.weapon_anim = crate::game::handlers::common::NINGUN_ARMA;
                    u.shield_anim = crate::game::handlers::common::NINGUN_ESCUDO;
                    // Clear all auras while mounted (equipment is hidden)
                    u.aura_a = 0;
                    u.aura_w = 0;
                    u.aura_e = 0;
                    u.aura_c = 0;
                    u.aura_r = 0;
                    // Flying mounts hide head AND helmet (mount sprite replaces everything)
                    // Normal mounts: keep helmet visible (VB6 line 1409)
                    if is_flying {
                        u.levitando = true;
                        u.head = 0;
                        u.casco_anim = crate::game::handlers::common::NINGUN_CASCO;
                    }
                }
            }

            // Send appearance update (CP)
            let cp = {
                let user = match state.users.get(&conn_id) {
                    Some(u) => u,
                    None => return,
                };
                binary_packets::write_character_change(
                    user.char_index.0 as i16, user.body as i16, user.head as i16, user.heading as u8,
                    user.weapon_anim as i16, user.shield_anim as i16, user.casco_anim as i16, 0, 0,
                )
            };
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);

            // Send mount state (USM)
            let mounted_now = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
            let pkt_usm = binary_packets::write_user_mount(char_index.0 as i16, mounted_now);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_usm);

            // Send flying state (MVOL) if flying mount
            if is_flying {
                let lev = state.users.get(&conn_id).map(|u| u.levitando).unwrap_or(false);
                let pkt_mvol = binary_packets::write_levitate(char_index.0 as i16, lev);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_mvol);
            }

            // Send [CD (levitando + auras) and AU| (aura update)
            let (pkt_cd, pkt_au) = {
                let user = match state.users.get(&conn_id) { Some(u) => u, None => return };
                (crate::game::handlers::common::build_cd_binary(user), crate::game::handlers::common::build_aura_binary(user))
            };
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cd);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_au);
        }
        ObjType::ScrollItem => {
            // Scroll system removed — no-op
        }
        ObjType::Sack => {
        }
        ObjType::RenounceHorde => {
            // VB6: Renounce Chaos faction
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx > 0 {
                state.send_msg_id(conn_id, 302, ""); // Must leave guild first
                return;
            }
            let is_caos = state.users.get(&conn_id)
                .map(|u| u.criminal || u.fuerzas_caos)
                .unwrap_or(false);
            if !is_caos {
                state.send_msg_id(conn_id, 239, ""); // Not in chaos faction
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.criminal = false;
                user.fuerzas_caos = false;
                let new_amt = user.inventory[idx].amount - 1;
                if new_amt <= 0 {
                    user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                } else {
                    user.inventory[idx].amount = new_amt;
                }
            }
            state.send_msg_id(conn_id, 355, ""); // Faction changed
            send_inventory_slot(state, conn_id, idx).await;
            // Send updated status via CharacterCreate (includes clan tag)
            if let Some(u) = state.users.get(&conn_id) {
                let pkt_cc = u.build_cc_binary();
                let (map, x, y) = (u.pos_map, u.pos_x, u.pos_y);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cc);
            }
        }
        ObjType::RenounceRoyal => {
            // VB6: Renounce Royal faction
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx > 0 {
                state.send_msg_id(conn_id, 302, "");
                return;
            }
            let is_armada = state.users.get(&conn_id)
                .map(|u| !u.criminal || u.armada_real)
                .unwrap_or(false);
            if !is_armada {
                state.send_msg_id(conn_id, 239, "");
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.criminal = true;
                user.armada_real = false;
                let new_amt = user.inventory[idx].amount - 1;
                if new_amt <= 0 {
                    user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
                } else {
                    user.inventory[idx].amount = new_amt;
                }
            }
            state.send_msg_id(conn_id, 355, "");
            send_inventory_slot(state, conn_id, idx).await;
            // Send updated status via CharacterCreate (includes clan tag)
            if let Some(u) = state.users.get(&conn_id) {
                let pkt_cc = u.build_cc_binary();
                let (map, x, y) = (u.pos_map, u.pos_x, u.pos_y);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cc);
            }
        }
        ObjType::Weapon => {
            // VB6: Case otWeapon — projectile weapons trigger target selection
            if obj_data.proyectil {
                let is_equipped = state.users.get(&conn_id)
                    .map(|u| u.inventory[idx].equipped).unwrap_or(false);
                if !is_equipped {
                    state.send_console(conn_id, "Antes de usar la herramienta deberías equipártela.", font_index::INFO);
                    return;
                }
                let sta = state.users.get(&conn_id).map(|u| u.min_sta).unwrap_or(0);
                if sta <= 0 {
                    state.send_msg_id(conn_id, 17, ""); // Too tired
                    return;
                }
                // VB6: WriteMultiMessage(UserIndex, eMessages.WorkRequestTarget, eSkill.Proyectiles)
                let pkt = binary_packets::write_work_request_target(skill_id::PROYECTILES as u8);
                state.send_bytes(conn_id, &pkt);
            }
            // Non-projectile weapons: no action on "use" (VB6 handles fogata etc. but that's separate)
        }
        _ => {
            // Unhandled item types — inform user
        }
    }
}

/// Apply a consumable item's effects (potions, food).
/// VB6 TipoPocion: 1=agility, 2=strength, 3=HP, 4=mana, 5=cure poison, 6=remo (paralysis removal)
pub(crate) fn apply_consumable(state: &mut GameState, conn_id: ConnectionId, obj: &crate::data::objects::ObjData) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        let amount = if obj.max_modificador > obj.min_modificador {
            rand_range(obj.min_modificador, obj.max_modificador)
        } else {
            obj.min_modificador
        };

        match obj.tipo_pocion {
            1 => {
                // Agility potion — boost Agilidad (capped at 35), store backup for expiry
                if !user.tomo_pocion {
                    user.attributes_backup = user.attributes;
                }
                let new_agi = (user.attributes[1] + amount).min(40).min(2 * user.attributes_backup[1]);
                user.attributes[1] = new_agi;
                user.tomo_pocion = true;
                user.duracion_efecto = obj.duracion_efecto / 40; // ms → ticks (40ms each)
            }
            2 => {
                // Strength potion — boost Fuerza (capped at 35)
                if !user.tomo_pocion {
                    user.attributes_backup = user.attributes;
                }
                let new_str = (user.attributes[0] + amount).min(40).min(2 * user.attributes_backup[0]);
                user.attributes[0] = new_str;
                user.tomo_pocion = true;
                user.duracion_efecto = obj.duracion_efecto / 40;
            }
            3 => {
                // Red potion — HP restoration
                if amount > 0 {
                    let new_hp = (user.min_hp + amount).min(user.max_hp);
                    user.min_hp = new_hp;
                }
            }
            4 => {
                // Blue potion — Mana restoration
                // VB6: MinMAN + Porcentaje(MaxMAN, 4) + ELV \ 2 + 40 / ELV
                let level = user.level.max(1) as f64;
                let mana_restore = ((user.max_mana as f64 * 4.0) / 100.0)
                    + (level / 2.0_f64).floor()
                    + (40.0_f64 / level).floor();
                let mana_restore = (mana_restore as i32).max(1);
                let new_mana = (user.min_mana + mana_restore).min(user.max_mana);
                user.min_mana = new_mana;
            }
            5 => {
                // Purple potion — Cure poison
                user.poisoned = false;
            }
            6 => {
                // Black potion — handled above (instant death for non-GMs)
            }
            _ => {
                // Generic consumable (ObjType::UseOnce food items, etc.)
                // HP restoration
                if amount > 0 {
                    let new_hp = (user.min_hp + amount).min(user.max_hp);
                    user.min_hp = new_hp;
                }
            }
        }

        // Food/hunger restoration (applies to all subtypes)
        if obj.min_ham > 0 {
            let new_ham = (user.min_ham + obj.min_ham).min(user.max_ham);
            user.min_ham = new_ham;
        }

        // Thirst restoration (applies to all subtypes)
        if obj.min_agua > 0 {
            let new_agua = (user.min_agua + obj.min_agua).min(user.max_agua);
            user.min_agua = new_agua;
        }

        // Cure poison flag (for UseOnce items that have CuraVeneno=1 but no TipoPocion)
        if obj.cura_veneno && obj.tipo_pocion != 5 {
            user.poisoned = false;
        }
    }
}

//! Player passive tick handlers: meditation, HP/mana/stamina regen, hunger/thirst,
//! poison, buff expiry, cooldowns, auto-save.

use tracing::{info, error};
use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget, UserState};
use crate::db::charfile;
use crate::protocol::{binary_packets, font_index};
use crate::game::handlers::common::*;
use crate::game::handlers::world;
use crate::game::handlers::{
    user_die, revive_user, warp_user,
};

// =====================================================================
// Meditation FX constants (VB6: Declares.bas)
// =====================================================================
const FXMEDITARCHICO: i16 = 4;       // level < 13
const FXMEDITARMEDIANO: i16 = 5;     // level 13-24
const FXMEDITARGRANDE: i16 = 6;      // level 25-34
const FXMEDITARXGRANDE: i16 = 16;    // level 35-41
const FXMEDITARXXGRANDE: i16 = 34;   // level >= 42

// =====================================================================
// Passive regeneration / drain system
// =====================================================================
// VB6 13.3 intervals (in seconds, since tick_player_passive runs every 1s).
// VB6 original values are in ticks (~100ms each): IntervaloHambre=6500, IntervaloSed=6000,
// IntervaloVeneno=500, SanaIntervaloSinDescansar=1600, SanaIntervaloDescansar=100,
// StaminaIntervaloSinDescansar=10, StaminaIntervaloDescansar=5.
// Converted: VB6_ticks / 10 = seconds.
const HUNGER_INTERVAL: i32 = 650;  // VB6: 6500 ticks = ~650 seconds (~10.8 min)
const THIRST_INTERVAL: i32 = 600;  // VB6: 6000 ticks = ~600 seconds (~10 min)
const HUNGER_DRAIN: i32 = 10;      // VB6: 10 per interval
const THIRST_DRAIN: i32 = 10;      // VB6: 10 per interval
const STAMINA_INTERVAL: i32 = 1;   // VB6: 10 ticks = ~1 second (standing)
const STAMINA_INTERVAL_REST: i32 = 1; // VB6: 5 ticks = ~0.5s (resting, we use 1s min)
const HP_REGEN_INTERVAL: i32 = 160; // VB6: SanaIntervaloSinDescansar=1600 ticks (~160s)
const HP_REGEN_INTERVAL_REST: i32 = 10; // VB6: SanaIntervaloDescansar=100 ticks (~10s)
const POISON_INTERVAL: i32 = 50;    // VB6: IntervaloVeneno=500 ticks (~50s)
const COLD_LAVA_INTERVAL: i32 = 2;  // VB6: IntervaloFrio=15 ticks (~2s at 1s tick)

pub(crate) fn meditation_fx_for_level(level: i32) -> i16 {
    if level < 13 { FXMEDITARCHICO }
    else if level < 25 { FXMEDITARMEDIANO }
    else if level < 35 { FXMEDITARGRANDE }
    else if level < 42 { FXMEDITARXGRANDE }
    else { FXMEDITARXXGRANDE }
}

/// ME — Toggle meditation on/off.
pub(crate) async fn handle_meditate(state: &mut GameState, conn_id: ConnectionId) {
    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    let meditating = user.meditating;
    let char_index = user.char_index;
    let map = user.pos_map;
    let x = user.pos_x;
    let y = user.pos_y;
    let level = user.level;

    if meditating {
        // Stop meditation — clear FX
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = false;
        }
        state.send_msg_id(conn_id, 205, ""); // Dejas de meditar
        state.send_bytes(conn_id, &binary_packets::write_meditate_toggle());
        let fx_clear = binary_packets::write_create_fx(char_index.0 as i16, 0, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_clear);
    } else {
        // Check if mana is already full
        if user.min_mana >= user.max_mana {
            state.send_msg_id(conn_id, 393, ""); // Mana restaurado
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = true;
        }

        // VB6: meditation FX scales by level (5 tiers), 999 loops = forever
        let med_fx = meditation_fx_for_level(level);
        let fx_pkt = binary_packets::write_create_fx(char_index.0 as i16, med_fx, 999);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_pkt);

        state.send_msg_id(conn_id, 394, ""); // Comenzas a meditar
    }
}

/// Called every 1 second — handles all passive regen/drain for logged users.
pub async fn tick_player_passive(state: &mut GameState) {
    // Collect logged user connections
    let user_ids: Vec<ConnectionId> = state.users.keys().copied().collect();

    for conn_id in user_ids {
        let user_data = match state.users.get(&conn_id) {
            Some(u) if u.logged && !u.dead => Some((
                u.poisoned,
                u.meditating,
                u.min_hp, u.max_hp,
                u.min_mana, u.max_mana,
                u.min_sta, u.max_sta,
                u.min_agua, u.max_agua,
                u.min_ham, u.max_ham,
                u.counter_hunger, u.counter_thirst,
                u.counter_stamina, u.counter_poison,
                u.skills[6], // SK7 = Meditar skill
                u.privileges,
                u.resting,
                u.mimetizado,
                u.invisible,
                u.pos_map, u.pos_x, u.pos_y,
                u.equip.armor,
                u.char_index.0,
            )),
            _ => None,
        };

        let (poisoned, meditating, min_hp, max_hp, min_mana, max_mana,
             min_sta, max_sta, min_agua, _max_agua, min_ham, _max_ham,
             cnt_hunger, cnt_thirst, cnt_sta, cnt_poison,
             meditate_skill, privileges, resting, _mimetizado, _invisible,
             pos_map, pos_x, pos_y, equip_armor, _char_idx) = match user_data {
            Some(d) => d,
            None => continue,
        };

        // Only non-GM users have hunger/thirst
        let is_player = privileges == 0;

        // --- Hunger drain ---
        if is_player && min_ham > 0 {
            if cnt_hunger >= HUNGER_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hunger = 0;
                    u.min_ham = (u.min_ham - HUNGER_DRAIN).max(0);
                }
                send_hunger_thirst(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hunger += 1;
                }
            }
        }

        // --- Thirst drain ---
        if is_player && min_agua > 0 {
            if cnt_thirst >= THIRST_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_thirst = 0;
                    u.min_agua = (u.min_agua - THIRST_DRAIN).max(0);
                }
                send_hunger_thirst(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_thirst += 1;
                }
            }
        }

        // --- Stamina regeneration (VB6: RecStamina) ---
        // VB6: regen = RandomNumber(1, Porcentaje(MaxSta, 5)) = 1 to 5% of max STA
        // Blocked when hungry or thirsty, blocked when naked (desnudo = no armor)
        let desnudo = equip_armor == 0;
        let sta_interval = if resting { STAMINA_INTERVAL_REST } else { STAMINA_INTERVAL };
        if min_sta < max_sta && min_ham > 0 && min_agua > 0 && !desnudo {
            if cnt_sta >= sta_interval {
                let five_pct = ((max_sta as f64 * 5.0) / 100.0).max(1.0) as i32;
                let regen = rand_range(1, five_pct);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stamina = 0;
                    u.min_sta = (u.min_sta + regen).min(u.max_sta);
                }
                send_stats_sta(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stamina += 1;
                }
            }
        }

        // --- Poison damage ---
        if poisoned {
            if cnt_poison >= POISON_INTERVAL {
                let dmg = rand_range(1, 5);
                let new_hp = min_hp - dmg;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_poison = 0;
                    u.min_hp = new_hp;
                }
                // VB6: "Estás envenenado, si no te curas morirás." (FONTTYPE_VENENO)
                state.send_console(conn_id, "Estás envenenado, si no te curas morirás.", font_index::VENENO);
                send_stats_hp(state, conn_id).await;

                // VB6: FXSANGRE (blood FX 14) on poison tick if not meditating/navigating
                if !meditating {
                    if let Some(u) = state.users.get(&conn_id) {
                        if !u.navigating {
                            let fx_pkt = binary_packets::write_create_fx(
                                u.char_index.0 as i16, 14, 0, // FXSANGRE = 14
                            );
                            state.send_data_bytes(
                                SendTarget::ToArea { map: u.pos_map, x: u.pos_x, y: u.pos_y },
                                &fx_pkt,
                            );
                        }
                    }
                }

                if new_hp <= 0 {
                    user_die(state, conn_id, None).await;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_poison += 1;
                }
            }
        }

        // --- Cold damage (VB6: EfectoFrio) ---
        // VB6: Only when naked (no armor). On snow terrain: 5% MaxHP damage. Elsewhere: 5% MaxSTA drain.
        if is_player && desnudo {
            let cnt_frio = state.users.get(&conn_id).map(|u| u.counter_frio).unwrap_or(0);
            if cnt_frio >= COLD_LAVA_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_frio = 0;
                }
                // Check terrain type from map info
                let is_snow = state.game_data.maps.get(pos_map as usize)
                    .and_then(|m| m.as_ref())
                    .map(|m| m.info.terreno.eq_ignore_ascii_case("NIEVE"))
                    .unwrap_or(false);
                if is_snow {
                    let dmg = ((max_hp as f64 * 5.0) / 100.0) as i32;
                    let new_hp = min_hp - dmg;
                    state.send_console(conn_id, "¡¡Estás muriendo de frío, abrigate o morirás!!", font_index::INFO);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.min_hp = new_hp;
                    }
                    send_stats_hp(state, conn_id).await;
                    if new_hp <= 0 {
                        state.send_console(conn_id, "¡¡Has muerto de frío!!", font_index::INFO);
                        user_die(state, conn_id, None).await;
                    }
                } else {
                    // Non-snow: stamina drain
                    let sta_dmg = ((max_sta as f64 * 5.0) / 100.0) as i32;
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.min_sta = (u.min_sta - sta_dmg).max(0);
                    }
                    send_stats_sta(state, conn_id).await;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_frio += 1;
                }
            }
        }

        // --- Lava damage (VB6: EfectoLava) ---
        // VB6: If standing on lava tile (graphic[0] in 5837-5852), 5% MaxHP damage.
        if is_player {
            let on_lava = state.game_data.maps.get(pos_map as usize)
                .and_then(|m| m.as_ref())
                .and_then(|m| {
                    if pos_x > 0 && pos_x <= 100 && pos_y > 0 && pos_y <= 100 {
                        Some(m.tiles[(pos_y - 1) as usize][(pos_x - 1) as usize].graphic[0])
                    } else { None }
                })
                .map(|g| g >= 5837 && g <= 5852)
                .unwrap_or(false);
            if on_lava {
                let cnt_lava = state.users.get(&conn_id).map(|u| u.counter_lava).unwrap_or(0);
                if cnt_lava >= COLD_LAVA_INTERVAL {
                    let dmg = ((max_hp as f64 * 5.0) / 100.0) as i32;
                    let new_hp = min_hp - dmg;
                    state.send_console(conn_id, "¡¡Quitate de la lava, te estás quemando!!", font_index::INFO);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.counter_lava = 0;
                        u.min_hp = new_hp;
                    }
                    send_stats_hp(state, conn_id).await;
                    if new_hp <= 0 {
                        state.send_console(conn_id, "¡¡Has muerto quemado!!", font_index::INFO);
                        user_die(state, conn_id, None).await;
                    }
                } else {
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.counter_lava += 1;
                    }
                }
            }
        }

        // Mimetismo is handled in tick_intervals (40ms tick) like invisibility/hide.

        // --- HP Regeneration (VB6: Sanar) ---
        // VB6: regen = RandomNumber(2, Porcentaje(MaxSta, 5)) — note: uses MaxSta not MaxHp (VB6 bug we replicate)
        // Interval: SanaIntervaloSinDescansar=1600 ticks (~160s), SanaIntervaloDescansar=100 ticks (~10s)
        // Blocked when hungry or thirsty, only for non-GMs
        let hp_interval = if resting { HP_REGEN_INTERVAL_REST } else { HP_REGEN_INTERVAL };
        if is_player && min_hp > 0 && min_hp < max_hp && min_ham > 0 && min_agua > 0 {
            let hp_counter = state.users.get(&conn_id).map(|u| u.counter_hp_regen).unwrap_or(0);
            if hp_counter >= hp_interval {
                let five_pct = ((max_sta as f64 * 5.0) / 100.0).max(2.0) as i32;
                let regen = rand_range(2, five_pct);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hp_regen = 0;
                    u.min_hp = (u.min_hp + regen).min(u.max_hp);
                }
                state.send_console(conn_id, "Has sanado.", font_index::INFO);
                send_stats_hp(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hp_regen += 1;
                }
            }
        }

        // --- Blindness countdown ---
        let blind_counter = state.users.get(&conn_id).map(|u| (u.blind, u.counter_blind)).unwrap_or((false, 0));
        if blind_counter.0 {
            if blind_counter.1 > 0 {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_blind -= 1;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.blind = false;
                    u.counter_blind = 0;
                }
                state.send_console(conn_id, "Ya puedes ver.", font_index::INFO);
            }
        }

        // --- Stun countdown ---
        let stun_data = state.users.get(&conn_id).map(|u| (u.stunned, u.counter_stun)).unwrap_or((false, 0));
        if stun_data.0 {
            if stun_data.1 > 0 {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stun -= 1;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.stunned = false;
                    u.counter_stun = 0;
                }
                state.send_console(conn_id, "Has recuperado la lucidez.", font_index::INFO);
            }
        }

        // --- Buff duration (DuracionPociones) ---
        let duracion = state.users.get(&conn_id).map(|u| u.duracion_efecto).unwrap_or(0);
        if duracion > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.duracion_efecto -= 1;
                if u.duracion_efecto <= 0 {
                    // Buff expired — restore attributes from backup
                    u.tomo_pocion = false;
                    u.attributes = u.attributes_backup;
                }
            }
        }

        // --- Remo potion cooldown ---
        let remo = state.users.get(&conn_id).map(|u| u.counter_remo).unwrap_or(0);
        if remo > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.counter_remo -= 1;
            }
        }

        // --- Silence timer (mute countdown) ---
        let silence = state.users.get(&conn_id).map(|u| (u.silenced, u.silence_timer)).unwrap_or((false, 0));
        if silence.0 && silence.1 > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.silence_timer -= 1;
                if u.silence_timer <= 0 {
                    u.silenced = false;
                    u.silence_timer = 0;
                }
            }
            let unmuted = state.users.get(&conn_id).map(|u| !u.silenced).unwrap_or(false);
            if unmuted {
                state.send_msg_id(conn_id, 946, "");
            }
        }

        // --- Jail timer (prison countdown) ---
        let jail = state.users.get(&conn_id).map(|u| u.jail_timer).unwrap_or(0);
        if jail > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.jail_timer -= 1;
            }
            let released = state.users.get(&conn_id).map(|u| u.jail_timer <= 0).unwrap_or(false);
            if released {
                // Release from jail — warp to Libertad (map 28, 50, 50)
                state.send_msg_id(conn_id, 444, "");
                warp_user(state, conn_id, 28, 50, 50).await;
            }
        }

        // --- Delayed resurrection countdown ---
        let seg_revivir = state.users.get(&conn_id).map(|u| u.segundos_para_revivir).unwrap_or(0);
        if seg_revivir > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.segundos_para_revivir -= 1;
            }
            let ready = state.users.get(&conn_id).map(|u| u.segundos_para_revivir <= 0).unwrap_or(false);
            if ready {
                revive_user(state, conn_id).await;
            }
        }

        // --- Resurrection cooldown ---
        let time_rev = state.users.get(&conn_id).map(|u| u.time_revivir).unwrap_or(0);
        if time_rev > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.time_revivir -= 1;
            }
        }

        // Anti-cheat intervals now decremented in tick_intervals (40ms tick)

        // --- Meditation (mana regen) — VB6: Trabajo.bas DoMeditar ---
        if meditating && min_mana < max_mana {
            // VB6: Skill-based "1 in N" chance per tick (lower N = better)
            let suerte = match meditate_skill {
                0..=10 => 35,
                11..=20 => 30,
                21..=30 => 28,
                31..=40 => 24,
                41..=50 => 22,
                51..=60 => 20,
                61..=70 => 18,
                71..=80 => 15,
                81..=90 => 10,
                91..=99 => 7,
                _ => 5, // skill 100
            };

            if rand_range(1, suerte) == 1 {
                // VB6: cant = Porcentaje(MaxMAN, PorcentajeRecuperoMana)
                // PorcentajeRecuperoMana is typically 5 from Balance.dat
                let regen = ((max_mana as f64 * 5.0) / 100.0) as i32;
                let regen = regen.max(1);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.min_mana = (u.min_mana + regen).min(u.max_mana);
                    if u.min_mana >= u.max_mana {
                        u.meditating = false;
                    }
                }
                send_stats_mana(state, conn_id).await;

                // Stop meditation when full — clear FX + notify
                let stopped = state.users.get(&conn_id).map(|u| !u.meditating).unwrap_or(false);
                if stopped {
                    state.send_msg_id(conn_id, 829, ""); // Has terminado de meditar
                    state.send_bytes(conn_id, &binary_packets::write_meditate_toggle());
                    // Clear FX for area
                    if let Some(u) = state.users.get(&conn_id) {
                        let fx_clear = binary_packets::write_create_fx(u.char_index.0 as i16, 0, 0);
                        state.send_data_bytes(
                            SendTarget::ToArea { map: u.pos_map, x: u.pos_x, y: u.pos_y },
                            &fx_clear,
                        );
                    }
                }
            }
        }
    }

    // --- Auto-save all characters every 60 seconds ---
    state.auto_save_counter -= 1;
    if state.auto_save_counter <= 0 {
        state.auto_save_counter = 60;
        auto_save_all_users(state).await;
    }
}

/// Save all logged-in users to DB (periodic auto-save).
/// Also called from main.rs on graceful shutdown.
pub async fn auto_save_all_users(state: &GameState) {
    let pool = state.pool.clone();
    let mut saved = 0;
    for (_conn_id, user) in state.users.iter() {
        if !user.logged || user.char_name.is_empty() { continue; }
        let data = build_char_save_data(user);
        if charfile::save_charfile(&pool, &user.char_name, &data).await.is_ok() {
            saved += 1;
        }
    }
    if saved > 0 {
        tracing::debug!("[SAVE] Auto-saved {} characters", saved);
    }
}

/// Build CharSaveData from a UserState (used by auto-save and disconnect save).
pub fn build_char_save_data(user: &UserState) -> charfile::CharSaveData {
    let inv: Vec<(i32, i32, bool)> = user.inventory.iter()
        .map(|s| (s.obj_index, s.amount, s.equipped))
        .collect();
    let bank: Vec<(i32, i32)> = user.bank.iter()
        .map(|s| (s.obj_index, s.amount))
        .collect();
    charfile::CharSaveData {
        // VB6: When navigating, save the REAL head (old_head), not 0.
        // The boat body is transient — on login we reconstruct it from BarcoSlot.
        head: if user.navigating { user.old_head } else { user.head },
        body: user.body,
        heading: user.heading,
        weapon: user.equip.weapon as i32,
        shield: user.equip.shield as i32,
        helmet: user.equip.helmet as i32,
        gold: user.gold,
        bank_gold: user.bank_gold,
        exp: user.exp,
        level: user.level,
        map: user.pos_map,
        x: user.pos_x,
        y: user.pos_y,
        min_hp: user.min_hp,
        max_hp: user.max_hp,
        min_mana: user.min_mana,
        max_mana: user.max_mana,
        min_sta: user.min_sta,
        max_sta: user.max_sta,
        max_hit: user.max_hit,
        min_hit: user.min_hit,
        max_agua: user.max_agua,
        min_agua: user.min_agua,
        max_ham: user.max_ham,
        min_ham: user.min_ham,
        dead: user.dead,
        poisoned: user.poisoned,
        criminal: user.criminal,
        paralyzed: user.paralyzed,
        hidden: user.hidden,
        navigating: user.navigating,
        barco_slot: user.barco_slot,
        montado: user.montado,
        levitando: user.levitando,
        montado_body: user.montado_body,
        privileges: user.saved_privileges,
        attributes: user.attributes,
        skills: user.skills,
        spells: user.spells,
        inventory: inv,
        bank,
        weapon_eqp_slot: user.equip.weapon,
        armour_eqp_slot: user.equip.armor,
        shield_eqp_slot: user.equip.shield,
        helmet_eqp_slot: user.equip.helmet,
        municion_eqp_slot: user.equip.municion,
        reputation: user.reputation,
        guild_index: user.guild_index,
        criminales_matados: user.criminales_matados,
        ciudadanos_matados: user.ciudadanos_matados,
        ejercito_real: user.armada_real,
        ejercito_caos: user.fuerzas_caos,
        skill_pts_libres: user.skill_pts_libres,
        recompensas_real: user.recompensas_real,
        recompensas_caos: user.recompensas_caos,
        reenlistadas: user.reenlistadas,
        description: user.desc.clone(),
        pet_count: user.nro_mascotas,
        pet_types: (0..3).filter_map(|i| {
            if user.mascotas_type[i] > 0 { Some(user.mascotas_type[i]) } else { None }
        }).collect(),
        // VB6 13.3 fields
        exp_skills: user.exp_skills,
        usuarios_matados: user.usuarios_matados,
        npcs_muertos: user.npcs_muertos,
        rep_asesino: user.rep_asesino,
        rep_bandido: user.rep_bandido,
        rep_burgues: user.rep_burgues,
        rep_ladrones: user.rep_ladrones,
        rep_noble: user.rep_noble,
        rep_plebe: user.rep_plebe,
        recibio_armadura_real: user.recibio_armadura_real,
        recibio_armadura_caos: user.recibio_armadura_caos,
        recibio_exp_real: user.recibio_exp_real,
        recibio_exp_caos: user.recibio_exp_caos,
        nivel_ingreso: user.nivel_ingreso,
        fecha_ingreso: user.fecha_ingreso.clone(),
        matados_ingreso: user.matados_ingreso,
        next_recompensa: user.next_recompensa,
        email: user.email.clone(),
        counter_pena: user.counter_pena,
        skills_asignados: user.skills_asignados,
        last_map: user.last_map,
        uptime: user.uptime,
        mochila_eqp_slot: user.backpack_slot,
        anillo_eqp_slot: user.equip.ring,
        pareja: user.pareja.clone(),
    }
}

// =====================================================================
// World cleanup system (ModuloLimpieza.bas)
// =====================================================================


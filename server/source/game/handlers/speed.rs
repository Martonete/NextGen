//! AO20 movement speed (`Modulo_UsUaRiOs.bas ActualizarVelocidadDeUsuario`,
//! `MODULO_NPCs.bas UpdateNpcSpeed`). The server owns the multiplier; the client only
//! scales its scroll by it, so every event that changes it must call these.

use crate::data::objects::ObjType;
use crate::game::types::{GameState, SendTarget};
use crate::net::ConnectionId;
use crate::protocol::binary_packets;

/// AO20 Declares.bas
pub const VELOCIDAD_NORMAL: f32 = 1.0;
pub const VELOCIDAD_MUERTO: f32 = 1.4;

/// Pure AO20 formula (ActualizarVelocidadDeUsuario without the sends).
/// `ship_velocidad` / `saddle_velocidad` / `armor_velocidad` are the `Velocidad` of the
/// equipped item or 1.0; `jinete_level_speed` is 1.0 (no rider levels here);
/// `velocidad_hechizada` is 0 when no speed spell is active.
pub fn calcular_velocidad(
    dead: bool,
    navigating_or_swimming: bool,
    ship_velocidad: f32,
    mounted: bool,
    saddle_velocidad: f32,
    jinete_level_speed: f32,
    velocidad_hechizada: f32,
    armor_velocidad: f32,
) -> f32 {
    if dead {
        // Los muertos no tienen modificadores de velocidad
        return VELOCIDAD_MUERTO;
    }
    let mut modificador_item = 1.0f32;
    let mut modificador_hechizo = 1.0f32;
    let mut jinete = 1.0f32;
    if navigating_or_swimming && ship_velocidad > 0.0 {
        modificador_item = ship_velocidad;
    }
    if mounted && saddle_velocidad > 0.0 {
        modificador_item = saddle_velocidad;
        jinete = jinete_level_speed;
    }
    if velocidad_hechizada > 0.0 {
        modificador_hechizo = velocidad_hechizada;
    }
    if armor_velocidad != 1.0 && armor_velocidad > 0.0 {
        modificador_item *= armor_velocidad;
    }
    // `Modifiers.MovementSpeed` (EffectsOverTime) does not exist here → factor 1.
    VELOCIDAD_NORMAL * modificador_item * jinete * modificador_hechizo
}

fn obj_velocidad(state: &GameState, obj_index: i32) -> f32 {
    if obj_index <= 0 {
        return 1.0;
    }
    state.get_object(obj_index).map(|o| o.velocidad).unwrap_or(1.0)
}

/// Recompute the user's speed and, if logged, tell the area (`SpeedingACT`) and the user
/// (`VelocidadToggle`). Returns the new value.
pub fn actualizar_velocidad_de_usuario(state: &mut GameState, conn_id: ConnectionId) -> f32 {
    let Some(u) = state.users.get(&conn_id) else { return VELOCIDAD_NORMAL };
    let dead = u.dead;
    let navigating = u.navigating;
    let mounted = u.montado;
    let ship_obj = if u.barco_slot > 0 && u.barco_slot <= u.inventory.len() {
        u.inventory[u.barco_slot - 1].obj_index
    } else {
        0
    };
    let saddle_obj = u.montado_obj;
    let armor_obj = if u.equip.armor > 0 && u.equip.armor <= u.inventory.len() {
        u.inventory[u.equip.armor - 1].obj_index
    } else {
        0
    };
    let (map, x, y, char_index, logged) = (u.pos_map, u.pos_x, u.pos_y, u.char_index.0 as i16, u.logged);

    // Only boats/suits count as ships; a stale barco_slot pointing at something else is ignored.
    let ship_velocidad = if state
        .get_object(ship_obj)
        .map(|o| o.obj_type == ObjType::Boat)
        .unwrap_or(false)
    {
        obj_velocidad(state, ship_obj)
    } else {
        1.0
    };
    let velocidad = calcular_velocidad(
        dead,
        navigating,
        ship_velocidad,
        mounted,
        obj_velocidad(state, saddle_obj),
        1.0,
        0.0,
        obj_velocidad(state, armor_obj),
    );

    if let Some(u) = state.users.get_mut(&conn_id) {
        u.speeding = velocidad;
    }
    if logged {
        state.send_data_bytes(
            SendTarget::ToArea { map, x, y },
            &binary_packets::write_speeding_act(char_index, velocidad),
        );
        state.send_bytes(conn_id, &binary_packets::write_velocidad_toggle(velocidad));
    }
    velocidad
}

/// AO20 UpdateNpcSpeed: `210 / IntervaloMovimiento`. Our NPCs step once per AI tick, so
/// the move interval is the AI interval — the client glides the whole tile across it.
pub fn npc_speeding(npc_ai_ms: u64) -> f32 {
    let interval = if npc_ai_ms == 0 { 380.0 } else { npc_ai_ms as f32 };
    210.0 / interval
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dead_users_ignore_every_modifier() {
        assert_eq!(calcular_velocidad(true, true, 1.5, true, 2.0, 1.45, 3.0, 0.5), VELOCIDAD_MUERTO);
    }

    #[test]
    fn ship_mount_spell_and_armor_multiply() {
        assert!((calcular_velocidad(false, false, 1.0, false, 1.0, 1.0, 0.0, 1.0) - 1.0).abs() < 1e-6);
        assert!((calcular_velocidad(false, true, 1.3, false, 1.0, 1.0, 0.0, 1.0) - 1.3).abs() < 1e-6);
        // Mount replaces the ship modifier and applies the rider level.
        assert!((calcular_velocidad(false, true, 1.3, true, 1.2, 1.05, 0.0, 1.0) - 1.26).abs() < 1e-6);
        assert!((calcular_velocidad(false, false, 1.0, false, 1.0, 1.0, 0.8, 1.0) - 0.8).abs() < 1e-6);
        assert!((calcular_velocidad(false, false, 1.0, false, 1.0, 1.0, 0.0, 0.9) - 0.9).abs() < 1e-6);
    }

    #[test]
    fn npc_speed_glides_the_move_interval() {
        assert!((npc_speeding(1300) - 210.0 / 1300.0).abs() < 1e-6);
        assert!((npc_speeding(0) - 210.0 / 380.0).abs() < 1e-6);
    }
}

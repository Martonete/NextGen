//! Cosmetic confirmed weapon hits. Never changes combat rolls or timing.
use crate::game::types::{GameState, SendTarget};
use crate::net::ConnectionId;
use crate::protocol::binary_packets;

fn style(name: &str, projectile: bool) -> i16 {
    if projectile { return 204; }
    let name = name.to_lowercase();
    if ["daga", "puñal", "punal", "estilete"].iter().any(|s| name.contains(s)) { 203 }
    else if ["hacha", "martillo", "maza", "garrote", "baston", "bastón"].iter().any(|s| name.contains(s)) { 202 }
    else if ["espada", "sable", "katana", "cimitarra"].iter().any(|s| name.contains(s)) { 201 }
    else { 206 }
}

pub(super) fn confirmed_hit(state: &mut GameState, attacker: ConnectionId, target: i16,
    critical: bool, ranged: bool) {
    let weapon = super::combat::get_weapon_info(state, attacker);
    let fx = state.get_object(weapon.obj_index)
        .map(|o| style(&o.name, ranged || o.proyectil)).unwrap_or(if ranged { 204 } else { 206 });
    let Some(user) = state.users.get(&attacker) else { return; };
    let (map, x, y, heading, source) = (user.pos_map, user.pos_x, user.pos_y,
        user.heading.clamp(1, 4) as i16, user.char_index.0 as i16);
    // fxLoops is a local visual payload: direction 1..4, bit 3 = confirmed critical.
    let packet = binary_packets::write_create_fx(target, fx, heading | if critical { 8 } else { 0 });
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &packet);
    if fx == 204 && !critical {
        let release = binary_packets::write_create_fx(source, 205, heading);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &release);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn weapon_visual_classification() {
        for (name, projectile, expected) in [("Espada Vikinga",false,201),
            ("Hacha de Guerra",false,202), ("Martillo",false,202),
            ("Daga +4",false,203), ("Arco Compuesto",true,204), ("",false,206)] {
            assert_eq!(style(name, projectile), expected);
        }
        let packet = binary_packets::write_create_fx(12, 201, 3 | 8);
        assert_eq!(packet, vec![43, 12, 0, 201, 0, 11, 0]);
    }
}

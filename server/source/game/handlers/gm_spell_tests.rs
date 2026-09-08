use super::*;

#[tokio::test]
async fn gm_spell_access_keeps_player_restrictions() {
    let base = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
    let config = crate::config::ServerConfig::load(&base).unwrap();
    let data = crate::data::GameData {
        experience: vec![100; 50], maps: vec![],
        objects: crate::data::objects::load_objects(&base).unwrap(),
        spells: crate::data::spells::load_spells(&base).unwrap(),
        npcs: crate::data::npcs::load_npcs(&base).unwrap(),
        balance: Default::default(), crafting: Default::default(),
    };
    let pool = sqlx::postgres::PgPoolOptions::new()
        .connect_lazy("postgres://test:test@127.0.0.1:1/test").unwrap();
    let bans = crate::db::bans::BanList { banned_hds: Default::default(), banned_ips: Default::default() };
    let mut state = GameState::new(config, base, data, pool, bans);
    for id in 1..=2 {
        let mut u = crate::game::types::UserState::new(id, "test".into());
        u.logged = true;
        u.privileges = if id == 1 { 1 } else { 0 };
        u.class = PlayerClass::Mago;
        u.min_hp = 1; u.max_hp = 100;
        u.min_mana = 0; u.max_mana = 0; u.min_sta = 0;
        u.min_ham = 0; u.min_agua = 0;
        u.skills[1] = 0;
        u.target_user = id;
        u.target_x = u.pos_x; u.target_y = u.pos_y;
        state.users.insert(id, u);
    }
    // Use a harmless healing spell with every equipment/resource gate enabled.
    let spell = &mut state.game_data.spells[0];
    spell.tipo = crate::data::spells::SpellType::Properties;
    spell.sube_hp = 1; spell.min_hp = 10; spell.max_hp = 10;
    spell.min_skill = 100; spell.need_staff = 10;
    spell.mana_requerido = 1200; spell.sta_requerido = 100;
    super::super::gm_items::handle_slash_hechizo(&mut state, 1, "1").await;
    assert_eq!(state.users[&1].spells[0], 1);
    super::super::gm_items::handle_slash_hechizo(&mut state, 2, "1").await;
    assert_eq!(state.users[&2].spells[0], 0, "Player cannot use GM command");
    state.online_names.insert("OTHER".into(), 2);
    super::super::gm_items::handle_slash_hechizo(&mut state, 1, "OTHER 1").await;
    assert_eq!(state.users[&2].spells[0], 0, "Low GM cannot grant others spells");
    for id in 1..=2 {
        state.users.get_mut(&id).unwrap().spells[0] = 1;
        handle_cast_spell(&mut state, id, 1).await;
        do_cast_spell(&mut state, id).await;
    }
    assert!(state.users[&1].min_hp > 1, "GM casts without weapon, skill, staff or mana");
    assert_eq!(state.users[&1].min_mana, 0);
    assert_eq!(state.users[&2].min_hp, 1, "Player still blocked");
    // Learning from an actual scroll also works for a starving, non-magical GM.
    let scroll = state.game_data.objects.iter().find(|o|
        o.obj_type == crate::data::objects::ObjType::Scroll && o.hechizo_index > 1
            && state.get_spell(o.hechizo_index).is_some()).unwrap();
    let obj_id = scroll.index as i32;
    let learned = scroll.hechizo_index;
    for id in 1..=2 {
        let u = state.users.get_mut(&id).unwrap();
        u.inventory[0].obj_index = obj_id; u.inventory[0].amount = 1;
        super::super::inventory::handle_use_item(&mut state, id, 1).await;
    }
    assert!(state.users[&1].spells.contains(&learned));
    assert!(!state.users[&2].spells.contains(&learned));
}

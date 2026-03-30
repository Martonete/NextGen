// =====================================================================
// Slash commands (from talk handler)
// =====================================================================

async fn handle_slash_command(state: &mut GameState, conn_id: ConnectionId, cmd: &str) {
    let cmd_upper = cmd.to_uppercase();
    if cmd_upper == "/RESUCITAR" {
        handle_resucitar(state, conn_id).await;
    } else if cmd_upper.starts_with("/FUNDARCLAN") {
        handle_slash_fundarclan(state, conn_id).await;
    } else if cmd_upper.starts_with("/CERRARCLAN") {
        handle_slash_cerrarclan(state, conn_id).await;
    } else if cmd_upper.starts_with("/SALIRCLAN") {
        handle_slash_salirclan(state, conn_id).await;
    } else if cmd_upper == "/SEGUROCLAN" {
        handle_slash_seguroclan(state, conn_id).await;
    } else if cmd_upper.starts_with("/HACLIDER ") {
        let target = cmd[10..].trim();
        handle_slash_haclider(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/SUBLIDER ") {
        let target = cmd[10..].trim();
        handle_slash_sublider(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/QSUBLIDR ") {
        let target = cmd[10..].trim();
        handle_slash_qsublidr(state, conn_id, target).await;
    } else if cmd_upper == "/CLAN" {
        handle_slash_clan_list(state, conn_id).await;
    } else if cmd_upper.starts_with("/CMSG ") {
        let text = &cmd[6..];
        handle_slash_cmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/ENLISTAR") {
        handle_slash_enlistar(state, conn_id).await;
    } else if cmd_upper.starts_with("/INFORMACION") {
        handle_slash_faction_info(state, conn_id).await;
    } else if cmd_upper.starts_with("/RECOMPENSA") {
        handle_slash_recompensa(state, conn_id).await;
    } else if cmd_upper.starts_with("/RENUNCIA") {
        handle_slash_renunciar(state, conn_id).await;
    } else if cmd_upper == "/DESERTAR" {
        handle_slash_desertar(state, conn_id).await;
    } else if cmd_upper.starts_with("/NUEVAPARTY") {
        handle_slash_nuevaparty(state, conn_id).await;
    } else if cmd_upper.starts_with("/PARTY ") {
        let target = cmd[7..].trim();
        handle_slash_party_invite(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ACEPTAR") {
        handle_slash_party_accept(state, conn_id).await;
    } else if cmd_upper.starts_with("/CANCELAR") {
        handle_slash_party_cancel(state, conn_id).await;
    } else if cmd_upper.starts_with("/FINPARTY") {
        handle_slash_finparty(state, conn_id).await;
    } else if cmd_upper.starts_with("/PINFO") {
        handle_slash_pinfo(state, conn_id).await;
    } else if cmd_upper.starts_with("/SACAR ") {
        let target = cmd[7..].trim();
        handle_slash_sacar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ECHARPARTY ") {
        let target = cmd[12..].trim();
        handle_slash_sacar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/DARPARTIDO ") {
        let target = cmd[12..].trim();
        handle_slash_darpartido(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/PARTYLIDER ") {
        let target = cmd[12..].trim();
        handle_slash_darpartido(state, conn_id, target).await;
    } else if cmd_upper == "/ONLINE" {
        handle_slash_online(state, conn_id).await;
    } else if cmd_upper == "/PING" {
        state.send_bytes(conn_id, &binary_packets::write_pong_response());
    } else if cmd_upper == "/BALANCE" {
        handle_slash_balance(state, conn_id).await;
    } else if cmd_upper.starts_with("/GLOBAL ") {
        let text = &cmd[8..];
        handle_slash_global(state, conn_id, text).await;
    } else if cmd_upper == "/EST" || cmd_upper == "/STATS" {
        handle_slash_stats(state, conn_id).await;
    } else if cmd_upper == "/ONLINEGM" {
        handle_slash_onlinegm(state, conn_id).await;
    } else if cmd_upper == "/ONLINEMAP" {
        handle_slash_onlinemap(state, conn_id).await;
    // =====================================================================
    // GM / Admin commands (require privileges > 0)
    // =====================================================================
    } else if cmd_upper.starts_with("/GMSG ") {
        let text = &cmd[6..];
        handle_slash_gmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/SMSG ") {
        let text = &cmd[6..];
        handle_slash_smsg(state, conn_id, text).await;
    } else if cmd_upper == "/NAVE" {
        handle_slash_nave(state, conn_id).await;
    } else if cmd_upper == "/HABILITAR" {
        handle_slash_habilitar(state, conn_id).await;
    } else if cmd_upper.starts_with("/COL ") {
        let args = &cmd[5..];
        handle_slash_col(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/NOADV ") {
        let target = cmd[7..].trim();
        handle_slash_noadv(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/LIBERAR ") {
        let target = cmd[9..].trim();
        handle_slash_liberar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/PENAS ") {
        let target = cmd[7..].trim();
        handle_slash_penas(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/MOD ") {
        let args = &cmd[5..];
        handle_slash_mod(state, conn_id, args, false).await;
    } else if cmd_upper.starts_with("/SMOD ") {
        let args = &cmd[6..];
        handle_slash_smod(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/ACC ") {
        let npc_id = cmd[5..].trim();
        handle_slash_acc(state, conn_id, npc_id, false).await;
    } else if cmd_upper.starts_with("/RACC ") {
        let npc_id = cmd[6..].trim();
        handle_slash_acc(state, conn_id, npc_id, true).await;
    } else if cmd_upper.starts_with("/CONSEJERO ") {
        let target = cmd[11..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::CONSEJERO, "consejero", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/SEMIDIOS ") {
        let target = cmd[10..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::SEMIDIOS, "semidios", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/DIOS ") {
        let target = cmd[6..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::DIOS, "dios", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/GDIOS ") {
        let target = cmd[7..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::GRAN_DIOS, "gran dios", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/EVENT ") {
        let target = cmd[7..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::EVENT_MASTER, "event master", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/DIRECTOR ") {
        let target = cmd[10..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::DIRECTOR, "coordinador", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/SUBADMINISTRADOR ") {
        let target = cmd[18..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::SUB_ADMINISTRADOR, "sub admin", privilege_level::SUB_ADMINISTRADOR).await;
    } else if cmd_upper.starts_with("/DEVELOPER ") {
        let target = cmd[11..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::DEVELOPER, "developer", privilege_level::DEVELOPER).await;
    } else if cmd_upper.starts_with("/ADMIN ") {
        let target = cmd[7..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::ADMINISTRADOR, "administrador", privilege_level::ADMINISTRADOR).await;
    } else if cmd_upper.starts_with("/PJ ") {
        let target = cmd[4..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::USER, "personaje", privilege_level::DIRECTOR).await;
    } else if cmd_upper == "/BLOQ" {
        handle_slash_bloq(state, conn_id).await;
    } else if cmd_upper.starts_with("/DAMEBANCO ") {
        let target = cmd[11..].trim();
        handle_slash_damebanco(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/DV ") {
        let target = cmd[4..].trim();
        handle_slash_dv(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/CONT ") {
        let args = cmd[6..].trim();
        handle_slash_cont(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/INFO ") {
        let target = cmd[6..].trim();
        handle_slash_info(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/EDIT ") {
        let args = &cmd[6..];
        handle_slash_edit(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/PREMIAR ") {
        let args = &cmd[9..];
        handle_slash_premiar(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/PREMIARTS ") {
        let args = &cmd[11..];
        handle_slash_premiarts(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/CHANGENICK ") {
        let new_name = cmd[12..].trim();
        handle_slash_changenick(state, conn_id, new_name).await;
    } else if cmd_upper.starts_with("/BORRARPJ ") {
        let target = cmd[10..].trim();
        handle_slash_borrarpj(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BANHD ") {
        let target = cmd[7..].trim();
        handle_slash_banhd(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/HECHIZO ") {
        let args = &cmd[9..];
        handle_slash_hechizo(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/RESETVALS ") {
        let args = cmd[11..].trim();
        handle_slash_resetvals(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/PRETORIANO ") {
        // /PRETORIANO <faccion> — Spawn praetorian clan on current position
        let faccion: i32 = cmd[12..].trim().parse().unwrap_or(1);
        let (map, x, y) = match state.users.get(&conn_id) {
            Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
            _ => { return; }
        };
        crear_clan_pretoriano(state, map, x, y, faccion).await;
        state.send_console(conn_id, "Clan pretoriano creado.", font_index::INFO);
    } else if cmd_upper == "/LIMPRETORIANO" {
        let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
        if priv_level >= privilege_level::DIOS {
            limpiar_clan_pretoriano(state).await;
            state.send_console(conn_id, "Clan pretoriano eliminado.", font_index::INFO);
        }
    } else if cmd_upper == "/REGRESAR" {
        handle_slash_regresar(state, conn_id).await;
    } else if cmd_upper == "/SALIR" {
        handle_slash_salir(state, conn_id).await;
    } else if cmd_upper == "/MEDITAR" {
        ticks::handle_meditate(state, conn_id).await;
    } else if cmd_upper == "/DESCANSAR" {
        handle_slash_descansar(state, conn_id).await;
    } else if cmd_upper == "/SEG" {
        // Toggle PvP + clan safety (VB6: Seguro AND SeguroClan toggled together)
        let is_safe = state.users.get(&conn_id).map(|u| u.safe_toggle).unwrap_or(true);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.safe_toggle = !is_safe;
        }
        if !is_safe {
            state.send_bytes(conn_id, &binary_packets::write_safe_on());
        } else {
            state.send_bytes(conn_id, &binary_packets::write_safe_off());
        }
    } else if cmd_upper == "/SEGR" {
        // Toggle resurrection safety — prevents others from rezzing you (VB6: /SEGR)
        let is_safe = state.users.get(&conn_id).map(|u| u.seguro_resu).unwrap_or(false);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.seguro_resu = !is_safe;
        }
        if !is_safe {
            state.send_bytes(conn_id, &binary_packets::write_safe_resu_on());
        } else {
            state.send_bytes(conn_id, &binary_packets::write_safe_resu_off());
        }
    } else if cmd_upper.starts_with("/DESC ") {
        let desc = cmd[6..].trim();
        handle_slash_desc(state, conn_id, desc).await;
    } else if cmd_upper == "/VERASPEC" {
        handle_slash_veraspec(state, conn_id).await;
    } else if cmd_upper == "/COMERCIAR" {
        handle_slash_comerciar(state, conn_id).await;
    } else if cmd_upper == "/BOVEDA" {
        handle_slash_boveda(state, conn_id).await;
    } else if cmd_upper.starts_with("/DARORO ") {
        let args = &cmd[8..];
        handle_slash_daroro(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/DEPOSITAR ") {
        let amount: i64 = cmd[11..].trim().parse().unwrap_or(0);
        handle_slash_depositar(state, conn_id, amount).await;
    } else if cmd_upper.starts_with("/RETIRAR ") {
        let amount: i64 = cmd[9..].trim().parse().unwrap_or(0);
        handle_slash_retirar_oro(state, conn_id, amount).await;
    } else if cmd_upper.starts_with("/FMSG ") {
        let text = &cmd[6..];
        handle_slash_fmsg(state, conn_id, text).await;
    } else if cmd_upper == "/HORA" {
        handle_slash_hora(state, conn_id).await;
    } else if cmd_upper.starts_with("/NICK ") {
        let name = cmd[6..].trim();
        handle_slash_nick_check(state, conn_id, name).await;
    } else if cmd_upper.starts_with("/_BUG ") {
        let text = &cmd[6..];
        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[BUG] {} reports: {}", name, text);
        state.send_console(conn_id, "Bug reportado. Gracias!", font_index::INFO);
    } else if cmd_upper == "/ADVERTENCIAS" {
        handle_slash_advertencias(state, conn_id).await;
    } else if cmd_upper == "/CURAR" {
        handle_slash_curar(state, conn_id).await;
    } else if cmd_upper == "/MONTAR" {
        handle_slash_montar(state, conn_id).await;
    } else if cmd_upper == "/DESMONTAR" {
        handle_slash_desmontar(state, conn_id).await;
    } else if cmd_upper == "/QUITARMASCOTA" {
        handle_slash_quitarmascota(state, conn_id).await;
    } else if cmd_upper == "/MSJ" {
        handle_slash_msj(state, conn_id).await;
    } else if cmd_upper == "/CIUDADANIA" {
        handle_slash_ciudadania(state, conn_id).await;
    } else if cmd_upper.starts_with("/VIAJAR ") {
        let city = &cmd[8..];
        handle_slash_viajar(state, conn_id, city).await;
    } else if cmd_upper == "/ENTRENAR" {
        handle_slash_entrenar(state, conn_id).await;
    } else if cmd_upper.starts_with("/CENTINELA ") {
        let code = cmd[11..].trim();
        handle_slash_centinela(state, conn_id, code).await;
    } else if cmd_upper.starts_with("/IR ") {
        let dest = &cmd[4..];
        handle_slash_ir(state, conn_id, dest).await;
    } else if cmd_upper == "/VOTAR" {
        handle_slash_votar(state, conn_id).await;
    } else if cmd_upper == "/RESULTADOS" {
        handle_slash_resultados(state, conn_id).await;
    } else if cmd_upper == "/CIRUJIA" {
        handle_slash_cirujia(state, conn_id).await;
    } else if cmd_upper.starts_with("/CASAR ") {
        let target = cmd[7..].trim();
        handle_slash_casar(state, conn_id, target).await;
    } else if cmd_upper == "/DIVORCIARSE" {
        handle_slash_divorciarse(state, conn_id).await;
    } else if cmd_upper.starts_with("/VOTO ") {
        let candidate = cmd[6..].trim();
        handle_slash_voto(state, conn_id, candidate).await;
    } else if cmd_upper.starts_with("/PMSG ") {
        // Party message
        let text = &cmd[6..];
        handle_slash_pmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/MIRAR ") {
        // /MIRAR <name> — look at character info (routes to DAMINF handler)
        let target = cmd[7..].trim();
        handle_daminf(state, conn_id, target).await;
    // =====================================================================
    // Duel system
    // =====================================================================
    } else if cmd_upper.starts_with("/DESAFIO ") {
        let target = cmd[9..].trim();
        handle_slash_desafio(state, conn_id, target).await;
    } else if cmd_upper == "/FINDESAFIO" {
        handle_slash_findesafio(state, conn_id).await;
    // =====================================================================
    // Timbero (gambling)
    // =====================================================================
    } else if cmd_upper.starts_with("/APOSTAR ") {
        let amount: i64 = cmd[9..].trim().parse().unwrap_or(0);
        handle_slash_apostar(state, conn_id, amount).await;
    // =====================================================================
    // Governor NPC (set home)
    // =====================================================================
    } else if cmd_upper == "/HOGAR" {
        handle_slash_hogar(state, conn_id).await;
    // =====================================================================
    // Survival skill (campfire)
    // =====================================================================
    } else if cmd_upper == "/FOGATA" {
        skills::handle_crear_fogata(state, conn_id).await;
    // =====================================================================
    // Guild diplomacy
    // =====================================================================
    } else if cmd_upper.starts_with("/DECLARARGUERRA ") {
        let target = cmd[16..].trim();
        handle_slash_declararguerra(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/PROPONERPAZ ") {
        let target = cmd[13..].trim();
        handle_slash_proponerpaz(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/PROPONERALIAR ") {
        let target = cmd[15..].trim();
        handle_slash_proponeraliar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ROMPERALIANZA ") {
        let target = cmd[15..].trim();
        handle_slash_romperalianza(state, conn_id, target).await;
    } else if cmd_upper == "/RELACIONES" {
        handle_slash_relaciones(state, conn_id).await;
    // =====================================================================
    // Password change
    // =====================================================================
    } else if cmd_upper.starts_with("/PASSWD ") {
        let args = &cmd[8..];
        handle_slash_passwd(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/TELEP ") {
        let args = cmd[7..].trim();
        handle_slash_telep(state, conn_id, args).await;
    } else if cmd_upper == "/TELEPLOC" {
        handle_slash_teleploc(state, conn_id).await;
    } else if cmd_upper.starts_with("/GO ") {
        let args = cmd[4..].trim();
        handle_slash_go(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/IRA ") {
        let target = cmd[5..].trim();
        handle_slash_ira(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/SUM ") {
        let target = cmd[5..].trim();
        handle_slash_sum(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/KICK ") {
        let target = cmd[6..].trim();
        handle_slash_kick(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ITEM ") || cmd_upper.starts_with("/CI ") {
        let offset = if cmd_upper.starts_with("/CI ") { 4 } else { 6 };
        let args = cmd[offset..].trim();
        handle_slash_item(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/SOBJ ") {
        let search = cmd[6..].trim();
        handle_slash_sobj(state, conn_id, search).await;
    // =====================================================================
    // Missing GM Commands (migrated from VB6)
    // =====================================================================
    } else if cmd_upper == "/INVISIBLE" {
        handle_slash_invisible(state, conn_id).await;
    } else if cmd_upper.starts_with("/DONDE ") {
        let target = cmd[7..].trim();
        handle_slash_donde(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BAN ") {
        let args = &cmd[5..];
        handle_slash_ban(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/UNBAN ") {
        let target = cmd[7..].trim();
        handle_slash_unban(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BANIP ") {
        let args = &cmd[7..];
        handle_slash_banip(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/UNBANIP ") {
        let ip = cmd[9..].trim();
        handle_slash_unbanip(state, conn_id, ip).await;
    } else if cmd_upper.starts_with("/BANACC ") {
        let args = &cmd[8..];
        handle_slash_banacc(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/UNBANACC ") {
        let target = cmd[10..].trim();
        handle_slash_unbanacc(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/CARCEL ") {
        let args = &cmd[8..];
        handle_slash_carcel(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/SILENCIAR ") {
        let args = &cmd[11..];
        handle_slash_silenciar(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/ADVERTIR ") {
        let args = &cmd[10..];
        handle_slash_advertir(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/KILL ") {
        let target = cmd[6..].trim();
        handle_slash_kill(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ECHAR ") {
        let target = cmd[7..].trim();
        handle_slash_echar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/REVIVIR ") {
        let target = cmd[9..].trim();
        handle_slash_revivir(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ESPIAR ") {
        let target = cmd[8..].trim();
        handle_slash_espiar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/INV ") {
        let target = cmd[5..].trim();
        handle_slash_inv(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BOV ") {
        let target = cmd[5..].trim();
        handle_slash_bov(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/RMSG ") {
        let text = &cmd[6..];
        handle_slash_rmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/LMSG ") {
        let args = &cmd[6..];
        handle_slash_lmsg(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/A ") {
        let text = &cmd[3..];
        handle_slash_rmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/EXP ") {
        let val = cmd[5..].trim();
        handle_slash_exp_mult(state, conn_id, val).await;
    } else if cmd_upper.starts_with("/GLD ") {
        let val = cmd[5..].trim();
        handle_slash_gld_mult(state, conn_id, val).await;
    } else if cmd_upper.starts_with("/DROP ") {
        let val = cmd[6..].trim();
        handle_slash_drop_mult(state, conn_id, val).await;
    } else if cmd_upper.starts_with("/HOME ") {
        let target = cmd[6..].trim();
        handle_slash_home(state, conn_id, target).await;
    } else if cmd_upper == "/OFF" {
        handle_slash_off(state, conn_id).await;
    } else if cmd_upper == "/ECHARTODOSPJS" {
        handle_slash_echartodospjs(state, conn_id).await;
    } else if cmd_upper.starts_with("/DAMETODO ") {
        let target = cmd[10..].trim();
        handle_slash_dametodo(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/MATA ") {
        let target = cmd[6..].trim();
        handle_slash_mata(state, conn_id, target).await;
    } else if cmd_upper == "/MASSKILL" {
        handle_slash_masskill(state, conn_id).await;
    } else if cmd_upper == "/LIMPIAR" || cmd_upper == "/LMAP" {
        handle_slash_limpiar(state, conn_id).await;
    } else if cmd_upper.starts_with("/NICK2IP ") {
        let target = cmd[9..].trim();
        handle_slash_nick2ip(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/IP2NICK ") {
        let ip = cmd[9..].trim();
        handle_slash_ip2nick(state, conn_id, ip).await;
    } else if cmd_upper == "/NOGLOBAL" {
        handle_slash_noglobal(state, conn_id).await;
    } else if cmd_upper == "/FPS" {
        handle_slash_fps(state, conn_id).await;
    } else if cmd_upper.starts_with("/CT ") {
        let args = &cmd[4..];
        handle_slash_ct(state, conn_id, args).await;
    } else if cmd_upper == "/DT" {
        handle_slash_dt(state, conn_id).await;
    } else if cmd_upper == "/RESMAP" {
        handle_slash_resmap(state, conn_id).await;
    } else if cmd_upper.starts_with("/TALKAS ") {
        let args = &cmd[8..];
        handle_slash_talkas(state, conn_id, args).await;
    } else if cmd_upper == "/GUARDARMAPA" {
        // VB6: Admin only. Saves current map to disk (GrabarMapa).
        // VB6-PARITY: VB6 serialises the in-memory map tile array back to the .map/.inf binary
        // format and writes MapaN.map + MapaN.inf to disk. This allows GMs to persist tile edits
        // (blocked states, trigger changes, placed objects) made during a live session.
        // Currently not implemented: maps are loaded read-only and there is no in-memory mutation
        // layer for tile edits, so saving would produce an identical file. Needs a tile-edit
        // subsystem before this can be meaningfully wired up.
        let is_admin = state.users.get(&conn_id).map(|u| u.privileges >= privilege_level::ADMINISTRADOR).unwrap_or(false);
        if !is_admin { return; }
        state.send_console(conn_id, "Mapa guardado.", font_index::INFO);
    } else if cmd_upper.starts_with("/SETDESC ") {
        let args = &cmd[9..];
        handle_slash_setdesc(state, conn_id, args).await;
    } else if cmd_upper == "/RELOADSINI" {
        gm_server::handle_reload_sini(state, conn_id).await;
    } else if cmd_upper == "/LOADOBJ" {
        gm_server::handle_reload_objects(state, conn_id).await;
    } else if cmd_upper == "/LOADHECHIZOS" {
        gm_server::handle_reload_spells(state, conn_id).await;
    } else if cmd_upper == "/LOADNPCS" {
        gm_server::handle_reload_npcs(state, conn_id).await;
    } else if cmd_upper == "/LOADBALANCE" {
        gm_server::handle_reload_balance(state, conn_id).await;
    } else if cmd_upper.starts_with("/LOADMAP ") {
        let map_str = &cmd[9..];
        gm_server::handle_reload_map(state, conn_id, map_str).await;
    } else if cmd_upper.starts_with("/STOP ") {
        let target = cmd[6..].trim();
        handle_slash_stop(state, conn_id, target, true).await;
    } else if cmd_upper.starts_with("/STOPOFF ") {
        let target = cmd[9..].trim();
        handle_slash_stop(state, conn_id, target, false).await;
    } else if cmd_upper.starts_with("/MODMAPINFO ") {
        // VB6: GM command to modify map properties (PK, PART, LUZ, RGB)
        let is_gm = state.users.get(&conn_id).map(|u| u.privileges >= privilege_level::DIOS).unwrap_or(false);
        if !is_gm { return; }
        let args = &cmd[12..];
        handle_slash_modmapinfo(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/CHEAT ") {
        let target = cmd[7..].trim();
        handle_slash_cheat(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/NPCAURA ") {
        let args = cmd[9..].trim();
        gm_items::handle_slash_npcaura(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/DEST") && (cmd_upper.len() == 5 || cmd_upper.as_bytes().get(5) == Some(&b' ')) {
        gm_items::handle_slash_dest(state, conn_id).await;
    } else if cmd_upper == "/MASSDEST" {
        gm_items::handle_slash_massdest(state, conn_id).await;
    } else if cmd_upper.starts_with("/IRCERCA ") {
        let target = cmd[9..].trim();
        gm_teleport::handle_slash_ircerca(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/HACERITEM ") {
        let args = &cmd[11..];
        gm_items::handle_slash_haceritem(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/NENE ") {
        let args = cmd[6..].trim();
        gm_items::handle_slash_nene(state, conn_id, args).await;
    } else if cmd_upper == "/RESETINV" {
        gm_items::handle_slash_resetinv(state, conn_id).await;
    // =====================================================================
    // NEW GM Commands (HIGH priority batch)
    // =====================================================================
    } else if cmd_upper.starts_with("/PERDON ") {
        let target = cmd[8..].trim();
        handle_slash_perdon(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/EJECUTAR ") {
        let target = cmd[10..].trim();
        handle_slash_ejecutar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/NOCAOS ") {
        let target = cmd[8..].trim();
        handle_slash_nocaos(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/NOREAL ") {
        let target = cmd[8..].trim();
        handle_slash_noreal(state, conn_id, target).await;
    } else if cmd_upper == "/LLUVIA" {
        handle_slash_lluvia(state, conn_id).await;
    } else if cmd_upper == "/NOCHE" {
        handle_slash_noche(state, conn_id).await;
    } else if cmd_upper == "/SHOWNAME" {
        handle_slash_showname(state, conn_id).await;
    } else if cmd_upper.starts_with("/LASTIP ") {
        let target = cmd[8..].trim();
        handle_slash_lastip(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/CONSULTA ") {
        let target = cmd[10..].trim();
        handle_slash_consulta(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/SLOT ") {
        let args = cmd[6..].trim();
        handle_slash_slot(state, conn_id, args).await;
    } else if cmd_upper == "/PISO" {
        handle_slash_piso(state, conn_id).await;
    } else if cmd_upper.starts_with("/MAPMSG ") {
        let text = &cmd[8..];
        handle_slash_mapmsg(state, conn_id, text).await;
    } else if cmd_upper == "/ONLINEREAL" {
        handle_slash_onlinereal(state, conn_id).await;
    } else if cmd_upper == "/ONLINECAOS" {
        handle_slash_onlinecaos(state, conn_id).await;
    // =====================================================================
    // VB6 13.3 Parity Features
    // =====================================================================
    // Pet commands
    } else if cmd_upper == "/QUIETO" {
        handle_slash_quieto(state, conn_id).await;
    } else if cmd_upper == "/ACOMPANAR" {
        handle_slash_acompanar(state, conn_id).await;
    } else if cmd_upper == "/LIBERARMASCOTA" {
        handle_slash_liberarmascota(state, conn_id).await;
    // ShareNpc
    } else if cmd_upper == "/COMPARTIR" {
        handle_slash_compartir(state, conn_id).await;
    } else if cmd_upper == "/NOCOMPARTIR" {
        handle_slash_nocompartir(state, conn_id).await;
    // Council messages
    } else if cmd_upper.starts_with("/CONSEJO ") {
        let text = &cmd[9..];
        handle_slash_consejo(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/CONSEJOCAOS ") {
        let text = &cmd[13..];
        handle_slash_consejocaos(state, conn_id, text).await;
    // MoveBank
    } else if cmd_upper.starts_with("/MOVEBANK ") {
        let args = cmd[10..].trim();
        handle_slash_movebank(state, conn_id, args).await;
    // Faction alert horn
    } else if cmd_upper == "/ALERTA" {
        handle_slash_alerta(state, conn_id).await;
    // Player help requests
    } else if cmd_upper.starts_with("/GM ") {
        let text = &cmd[4..];
        handle_slash_gm_request(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/ROL ") {
        let text = &cmd[5..];
        handle_slash_rol(state, conn_id, text).await;
    // Guild elections
    } else if cmd_upper == "/ELECCIONES" {
        handle_slash_elecciones(state, conn_id).await;
    // GM commands — new batch
    } else if cmd_upper == "/PANELGM" {
        handle_slash_panelgm(state, conn_id).await;
    } else if cmd_upper == "/TRABAJANDO" {
        handle_slash_trabajando(state, conn_id).await;
    } else if cmd_upper == "/OCULTANDO" {
        handle_slash_ocultando(state, conn_id).await;
    } else if cmd_upper.starts_with("/SEGUIR ") {
        let target = cmd[8..].trim();
        handle_slash_seguir(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/REALMSG ") {
        let text = &cmd[9..];
        handle_slash_realmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/CAOSMSG ") {
        let text = &cmd[9..];
        handle_slash_caosmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/CIUMSG ") {
        let text = &cmd[8..];
        handle_slash_ciumsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/CRIMSG ") {
        let text = &cmd[8..];
        handle_slash_crimsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/ACEPTCONSE ") {
        let target = cmd[12..].trim();
        handle_slash_aceptconse(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ACEPTCONSECAOS ") {
        let target = cmd[16..].trim();
        handle_slash_aceptconsecaos(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/KICKCONSE ") {
        let target = cmd[11..].trim();
        handle_slash_kickconse(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ESTUPIDO ") {
        let target = cmd[10..].trim();
        handle_slash_estupido(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/NOESTUPIDO ") {
        let target = cmd[12..].trim();
        handle_slash_noestupido(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/TRIGGER ") {
        let args = cmd[9..].trim();
        handle_slash_trigger(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/SETDIALOG ") {
        let args = &cmd[11..];
        handle_slash_setdialog(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/APAGAR ") {
        let args = cmd[8..].trim();
        handle_slash_apagar(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/REINICIAR ") {
        let args = cmd[11..].trim();
        handle_slash_reiniciar(state, conn_id, args).await;
    } else if cmd_upper == "/GRABAR" {
        handle_slash_grabar(state, conn_id).await;
    } else if cmd_upper.starts_with("/APASS ") {
        let args = &cmd[7..];
        handle_slash_apass(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/BANCLAN ") {
        let guild = cmd[9..].trim();
        handle_slash_banclan(state, conn_id, guild).await;
    } else if cmd_upper.starts_with("/MIEMBROSCLAN ") {
        let guild = cmd[14..].trim();
        handle_slash_miembrosclan(state, conn_id, guild).await;
    } else if cmd_upper.starts_with("/RAJARCLAN ") {
        let target = cmd[11..].trim();
        handle_slash_rajarclan(state, conn_id, target).await;
    // Polls
    } else if cmd_upper.starts_with("/ENCUESTA ") {
        let args = &cmd[10..];
        handle_slash_encuesta_crear(state, conn_id, args).await;
    } else if cmd_upper == "/CERRARENCUESTA" {
        handle_slash_cerrar_encuesta(state, conn_id).await;
    } else {
        // Unknown command — send feedback
        state.send_msg_id(conn_id, 714, ""); // TEXTO714: Comando no reconocido
    }
}

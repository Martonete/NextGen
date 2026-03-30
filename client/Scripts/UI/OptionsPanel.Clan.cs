using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// OptionsPanel partial: Clan tab (guild management UI).
/// VB6: frmGuildLeader, frmGuildMember, frmGuildBrief
/// </summary>
public partial class OptionsPanel
{

    private VBoxContainer BuildClanTab()
    {
        var vbox = RpgTheme.CreateColumn();
        _clanContent = RpgTheme.CreateColumn();
        _clanContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddChild(_clanContent);

        // Initial placeholder
        _clanContent.AddChild(RpgTheme.CreateInfoLabel("Cargando informacion del clan...", 12));

        return vbox;
    }

    /// <summary>
    /// Called from Main.cs when server responds with guild info.
    /// Rebuilds clan tab content based on GuildInfoType.
    /// </summary>
    public void UpdateClanContent()
    {
        if (_clanContent == null || _state == null) return;

        // Clear existing content
        foreach (var child in _clanContent.GetChildren())
            child.QueueFree();

        string guildName = _state.UserGuildName;
        string infoType = _state.GuildInfoType;

        if (infoType == "Leader")
            BuildClanLeaderContent();
        else if (infoType == "Member")
            BuildClanMemberContent();
        else if (infoType == "Details")
            BuildClanDetailsContent();
        else
            BuildClanNoGuildContent();
    }

    // ── No Guild View (VB6: frmGuildMember without a guild) ──

    private void BuildClanNoGuildContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(RpgTheme.CreateTitleLabel("Sin Clan", 15));

        _clanContent.AddChild(RpgTheme.CreateInfoLabel("No perteneces a ningun clan.", 12));

        _clanContent.AddChild(RpgTheme.CreateSpacer(8));

        // Found clan button
        var foundBtn = RpgTheme.CreateRpgButton("Fundar Clan", false, 14);
        foundBtn.CustomMinimumSize = new Vector2(200, 36);
        foundBtn.Pressed += () =>
        {
            if (_tcp != null)
                _tcp.SendPacket(ClientPackets.WriteTalk("/FUNDARCLAN"));
        };
        _clanContent.AddChild(foundBtn);

        _clanContent.AddChild(RpgTheme.CreateSpacer(8));

        // Guild list
        _clanContent.AddChild(RpgTheme.CreateTitleLabel("Clanes Disponibles", 15));

        // Search filter
        var searchRow = RpgTheme.CreateRow();
        searchRow.AddChild(RpgTheme.CreateInfoLabel("Buscar:", 12));
        var searchEdit = RpgTheme.CreateRpgInput("Filtrar clanes...", 200);
        searchRow.AddChild(searchEdit);
        _clanContent.AddChild(searchRow);

        var guildList = RpgTheme.CreateRpgItemList(0, 160);
        _clanContent.AddChild(guildList);

        // Parse guild list data
        var guilds = new List<string>();
        var listData = _state.GuildListData;
        if (!string.IsNullOrEmpty(listData))
        {
            var parts = listData.Split(',');
            for (int i = 1; i < parts.Length; i++)
            {
                guilds.Add(parts[i]);
                var fields = parts[i].Split('-');
                string display = fields.Length >= 3 ? $"{fields[0]}  [{fields[1]}]  Nv.{fields[2]}" : parts[i];
                guildList.AddItem(display);
            }
        }

        // Filter functionality
        searchEdit.TextChanged += filter =>
        {
            guildList.Clear();
            for (int i = 0; i < guilds.Count; i++)
            {
                if (string.IsNullOrEmpty(filter) || guilds[i].Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    var fields = guilds[i].Split('-');
                    string display = fields.Length >= 3 ? $"{fields[0]}  [{fields[1]}]  Nv.{fields[2]}" : guilds[i];
                    guildList.AddItem(display);
                }
            }
        };

        // Details button
        var detailsBtn = RpgTheme.CreateRpgButton("Ver Detalles", false, 12);
        detailsBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = guildList.GetSelectedItems();
            if (sel.Length == 0) return;
            string itemText = guildList.GetItemText(sel[0]);
            string gName = itemText.Split(' ')[0].Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildDetails(gName));
        };
        _clanContent.AddChild(detailsBtn);

        _clanContent.AddChild(RpgTheme.CreateSpacer(4));

        // Petition + apply
        _clanContent.AddChild(RpgTheme.CreateInfoLabel("Solicitud de ingreso:", 12));
        var petitionEdit = RpgTheme.CreateRpgInput("Escribe tu solicitud...", 200);
        _clanContent.AddChild(petitionEdit);

        var applyBtn = RpgTheme.CreateRpgButton("Enviar Solicitud", false, 12);
        applyBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = guildList.GetSelectedItems();
            if (sel.Length == 0) return;
            string itemText = guildList.GetItemText(sel[0]);
            string gName = itemText.Split(' ')[0].Trim();
            string petition = petitionEdit.Text.Trim();
            if (string.IsNullOrEmpty(petition)) petition = "Solicito ingresar.";
            _tcp.SendPacket(ClientPackets.WriteGuildApply($"{gName},{petition}"));
        };
        _clanContent.AddChild(applyBtn);
    }

    // ── Leader View (VB6: frmGuildLeader) ──

    private const char BF = '\u00BF';

    private void BuildClanLeaderContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(RpgTheme.CreateTitleLabel("Administracion del Clan", 15));

        var data = _state.GuildInfoData;
        var parts = data.Split(BF);

        // Parse leader data
        string guildPoints = parts.Length > 0 ? parts[0] : "0";
        string guildLevel = parts.Length > 1 ? parts[1] : "1";
        string leader = parts.Length > 2 ? parts[2] : "?";
        string sub1 = parts.Length > 3 ? parts[3] : "";
        string sub2 = parts.Length > 4 ? parts[4] : "";
        string reputation = parts.Length > 9 ? parts[9] : "0";

        // Info
        var infoLabel = RpgTheme.CreateInfoLabel("", 12);
        infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
        string infoText = $"Nivel: {guildLevel} | Puntos: {guildPoints}\n" +
            $"Lider: {leader}\n";
        // Only show sub-lideres if at least one is set
        bool hasSub1 = !string.IsNullOrEmpty(sub1) && sub1 != "default" && sub1 != "Fermin";
        bool hasSub2 = !string.IsNullOrEmpty(sub2) && sub2 != "default" && sub2 != "Fermin";
        if (hasSub1 || hasSub2)
        {
            var subs = new List<string>();
            if (hasSub1) subs.Add(sub1);
            if (hasSub2) subs.Add(sub2);
            infoText += $"Sub-lideres: {string.Join(", ", subs)}\n";
        }
        infoText += $"Reputacion: {reputation}";
        infoLabel.Text = infoText;
        _clanContent.AddChild(infoLabel);

        _clanContent.AddChild(RpgTheme.CreateSpacer(6));

        // Guild list (other clans)
        int idx = 13;
        var guildNames = new List<string>();
        if (idx < parts.Length)
        {
            int guildCount = 0;
            int.TryParse(parts[idx], out guildCount);
            idx++;
            for (int i = 0; i < guildCount && idx < parts.Length; i++, idx++)
                guildNames.Add(parts[idx]);
        }

        // Two columns: left=clans, right=members
        var cols = RpgTheme.CreateRow();

        // Left column — Clans
        var leftCol = RpgTheme.CreateColumn();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.AddChild(RpgTheme.CreateInfoLabel("Clanes:", 12));

        var filterClans = RpgTheme.CreateRpgInput("Filtrar...", 120);
        leftCol.AddChild(filterClans);

        var guildList = RpgTheme.CreateRpgItemList(0, 100);
        foreach (var g in guildNames)
        {
            // Leader format: name$align$level
            var gf = g.Split('$');
            guildList.AddItem(gf.Length >= 1 ? gf[0] : g);
        }
        leftCol.AddChild(guildList);
        cols.AddChild(leftCol);

        filterClans.TextChanged += filter =>
        {
            guildList.Clear();
            foreach (var g in guildNames)
            {
                var gf = g.Split('$');
                string name = gf.Length >= 1 ? gf[0] : g;
                if (string.IsNullOrEmpty(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    guildList.AddItem(name);
            }
        };

        // Right column — Members
        var rightCol = RpgTheme.CreateColumn();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightCol.AddChild(RpgTheme.CreateInfoLabel("Miembros:", 12));

        var filterMembers = RpgTheme.CreateRpgInput("Filtrar...", 120);
        rightCol.AddChild(filterMembers);

        var memberList = RpgTheme.CreateRpgItemList(0, 100);
        rightCol.AddChild(memberList);
        cols.AddChild(rightCol);

        // Parse members
        var memberNames = new List<string>();
        if (idx < parts.Length)
        {
            int memberCount = 0;
            int.TryParse(parts[idx], out memberCount);
            idx++;
            if (idx < parts.Length)
            {
                var mems = parts[idx].Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in mems) memberNames.Add(m.Trim());
                idx++;
            }
        }
        foreach (var m in memberNames) memberList.AddItem(m);

        filterMembers.TextChanged += filter =>
        {
            memberList.Clear();
            foreach (var m in memberNames)
                if (string.IsNullOrEmpty(filter) || m.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    memberList.AddItem(m);
        };

        _clanContent.AddChild(cols);

        // News
        _clanContent.AddChild(RpgTheme.CreateSpacer(4));
        _clanContent.AddChild(RpgTheme.CreateInfoLabel("Noticias del clan:", 12));

        var newsEdit = RpgTheme.CreateRpgTextEdit("", 0, 50);
        newsEdit.Text = _state.GuildNewsText;
        _clanContent.AddChild(newsEdit);

        var updateNewsBtn = RpgTheme.CreateRpgButton("Actualizar Noticias", false, 12);
        updateNewsBtn.Pressed += () =>
        {
            if (_tcp != null)
                _tcp.SendPacket(ClientPackets.WriteGuildNews(newsEdit.Text));
        };
        _clanContent.AddChild(updateNewsBtn);

        _clanContent.AddChild(RpgTheme.CreateSpacer(4));

        // Solicitudes
        _clanContent.AddChild(RpgTheme.CreateInfoLabel("Solicitudes pendientes:", 12));
        var applicantList = RpgTheme.CreateRpgItemList(0, 60);

        var applicants = new List<string>();
        if (idx < parts.Length)
        {
            int appCount = 0;
            int.TryParse(parts[idx], out appCount);
            idx++;
            for (int i = 0; i < appCount && idx < parts.Length; i++, idx++)
            {
                applicants.Add(parts[idx]);
                applicantList.AddItem(parts[idx]);
            }
        }
        _clanContent.AddChild(applicantList);

        var appBtnRow = RpgTheme.CreateRow();
        var acceptBtn = RpgTheme.CreateRpgButton("Aceptar", false, 12);
        acceptBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = applicantList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = applicantList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildAccept(name));
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        appBtnRow.AddChild(acceptBtn);

        var rejectBtn = RpgTheme.CreateRpgButton("Rechazar", false, 12);
        rejectBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = applicantList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = applicantList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildReject($"{name},Rechazado"));
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        appBtnRow.AddChild(rejectBtn);
        _clanContent.AddChild(appBtnRow);

        _clanContent.AddChild(RpgTheme.CreateSpacer(4));

        // Action buttons row
        var actionRow = RpgTheme.CreateRow();

        var detailsClanBtn = RpgTheme.CreateRpgButton("Detalles Clan", false, 11);
        detailsClanBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = guildList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = guildList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildDetails(name));
        };
        actionRow.AddChild(detailsClanBtn);

        var expelBtn = RpgTheme.CreateRpgButton("Expulsar", false, 11);
        expelBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = memberList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = memberList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildExpel(name));
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        actionRow.AddChild(expelBtn);

        var closeGuildBtn = RpgTheme.CreateRpgButton("Cerrar Clan", false, 11);
        closeGuildBtn.Pressed += () =>
        {
            if (_tcp != null)
            {
                _tcp.SendPacket(ClientPackets.WriteTalk("/CERRARCLAN"));
                _clanTabRequested = false;
                CallDeferred(nameof(RefreshClanTab));
            }
        };
        actionRow.AddChild(closeGuildBtn);

        _clanContent.AddChild(actionRow);
    }

    // ── Member View (VB6: frmGuildMember) ──

    private void BuildClanMemberContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(RpgTheme.CreateTitleLabel("Mi Clan", 15));

        var data = _state.GuildInfoData;
        var parts = data.Split(BF);

        string guildPoints = parts.Length > 0 ? parts[0] : "0";
        string guildLevel = parts.Length > 1 ? parts[1] : "1";
        string leader = parts.Length > 2 ? parts[2] : "?";
        string sub1 = parts.Length > 3 ? parts[3] : "";
        string sub2 = parts.Length > 4 ? parts[4] : "";
        string reputation = parts.Length > 9 ? parts[9] : "0";

        var infoLabel = RpgTheme.CreateInfoLabel("", 12);
        infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
        string infoText = $"Clan: {_state.UserGuildName}\n" +
            $"Nivel: {guildLevel} | Puntos: {guildPoints}\n" +
            $"Lider: {leader}\n";
        bool hasSub1 = !string.IsNullOrEmpty(sub1) && sub1 != "default" && sub1 != "Fermin";
        bool hasSub2 = !string.IsNullOrEmpty(sub2) && sub2 != "default" && sub2 != "Fermin";
        if (hasSub1 || hasSub2)
        {
            var subs = new List<string>();
            if (hasSub1) subs.Add(sub1);
            if (hasSub2) subs.Add(sub2);
            infoText += $"Sub-lideres: {string.Join(", ", subs)}\n";
        }
        infoText += $"Reputacion: {reputation}";
        infoLabel.Text = infoText;
        _clanContent.AddChild(infoLabel);

        _clanContent.AddChild(RpgTheme.CreateSpacer(6));

        // Two columns: clans list + members
        var cols = RpgTheme.CreateRow();

        // Left — other clans
        var leftCol = RpgTheme.CreateColumn();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.AddChild(RpgTheme.CreateInfoLabel("Clanes:", 12));

        var searchClans = RpgTheme.CreateRpgInput("Filtrar...", 120);
        leftCol.AddChild(searchClans);

        var clanList = RpgTheme.CreateRpgItemList(0, 120);
        leftCol.AddChild(clanList);

        // Parse guild list from member data
        int parseIdx = 10;
        var guildNames = new List<string>();
        if (parseIdx < parts.Length)
        {
            int guildCount = 0;
            int.TryParse(parts[parseIdx], out guildCount);
            parseIdx++;
            for (int i = 0; i < guildCount && parseIdx < parts.Length; i++, parseIdx++)
            {
                guildNames.Add(parts[parseIdx]);
                var gf = parts[parseIdx].Split('-');
                clanList.AddItem(gf.Length >= 1 ? gf[0] : parts[parseIdx]);
            }
        }

        searchClans.TextChanged += filter =>
        {
            clanList.Clear();
            foreach (var g in guildNames)
            {
                var gf = g.Split('-');
                string n = gf.Length >= 1 ? gf[0] : g;
                if (string.IsNullOrEmpty(filter) || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    clanList.AddItem(n);
            }
        };

        cols.AddChild(leftCol);

        // Right — members
        var rightCol = RpgTheme.CreateColumn();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightCol.AddChild(RpgTheme.CreateInfoLabel("Miembros:", 12));

        var memberList = RpgTheme.CreateRpgItemList(0, 140);
        rightCol.AddChild(memberList);

        var memberNames = new List<string>();
        if (parseIdx < parts.Length)
        {
            int memberCount = 0;
            int.TryParse(parts[parseIdx], out memberCount);
            parseIdx++;
            if (parseIdx < parts.Length)
            {
                var mems = parts[parseIdx].Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in mems) memberNames.Add(m.Trim());
            }
        }
        foreach (var m in memberNames) memberList.AddItem(m);

        rightCol.AddChild(RpgTheme.CreateInfoLabel($"Total: {memberNames.Count}", 12));
        cols.AddChild(rightCol);

        _clanContent.AddChild(cols);

        _clanContent.AddChild(RpgTheme.CreateSpacer(4));

        // Buttons
        var btnRow = RpgTheme.CreateRow();

        var detailsBtn = RpgTheme.CreateRpgButton("Detalles Clan", false, 12);
        detailsBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = clanList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = clanList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildDetails(name));
        };
        btnRow.AddChild(detailsBtn);

        var newsBtn = RpgTheme.CreateRpgButton("Noticias", false, 12);
        newsBtn.Pressed += () =>
        {
            // Request news from server (will come as a console/chat message)
            if (_tcp != null)
                _tcp.SendPacket(ClientPackets.WriteTalk("/NOTICIAS"));
        };
        btnRow.AddChild(newsBtn);

        var leaveBtn = RpgTheme.CreateRpgButton("Abandonar Clan", false, 12);
        leaveBtn.Pressed += () =>
        {
            if (_tcp != null)
            {
                _tcp.SendPacket(ClientPackets.WriteTalk("/SALIRCLAN"));
                _clanTabRequested = false;
                // Delay refresh — server needs to process
                CallDeferred(nameof(RefreshClanTab));
            }
        };
        btnRow.AddChild(leaveBtn);

        _clanContent.AddChild(btnRow);
    }

    // ── Details View (VB6: frmGuildBrief) ──

    private void BuildClanDetailsContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(RpgTheme.CreateTitleLabel("Detalles del Clan", 15));

        var data = _state.GuildInfoData;
        var parts = data.Split(BF);

        string level = parts.Length > 0 ? parts[0] : "?";
        string alignment = parts.Length > 1 ? parts[1] : "?";
        string repu = parts.Length > 2 ? parts[2] : "0";
        string founder = parts.Length > 3 ? parts[3] : "?";
        string date = parts.Length > 4 ? parts[4] : "?";
        string clanLeader = parts.Length > 5 ? parts[5] : "?";
        string sub1 = parts.Length > 6 ? parts[6] : "";
        string sub2 = parts.Length > 7 ? parts[7] : "";
        string memberCount = parts.Length > 8 ? parts[8] : "0";

        // Codex (indices 9-16)
        var codexLines = new List<string>();
        for (int i = 9; i < Math.Min(parts.Length, 17); i++)
            if (!string.IsNullOrWhiteSpace(parts[i])) codexLines.Add(parts[i]);

        string desc = parts.Length > 17 ? parts[17] : "";
        string clanName = parts.Length > 18 ? parts[18] : "";

        var detailsLabel = RpgTheme.CreateInfoLabel("", 12);
        detailsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailsLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
        string detailsText = $"Nombre: {clanName}\n" +
            $"Nivel: {level} | Alineacion: {alignment}\n" +
            $"Fundador: {founder} | Fecha: {date}\n" +
            $"Lider: {clanLeader}\n";
        bool dHasSub1 = !string.IsNullOrEmpty(sub1) && sub1 != "default" && sub1 != "Fermin";
        bool dHasSub2 = !string.IsNullOrEmpty(sub2) && sub2 != "default" && sub2 != "Fermin";
        if (dHasSub1 || dHasSub2)
        {
            var subs = new List<string>();
            if (dHasSub1) subs.Add(sub1);
            if (dHasSub2) subs.Add(sub2);
            detailsText += $"Sub-lideres: {string.Join(", ", subs)}\n";
        }
        detailsText += $"Miembros: {memberCount} | Reputacion: {repu}";
        detailsLabel.Text = detailsText;
        _clanContent.AddChild(detailsLabel);

        if (!string.IsNullOrEmpty(desc))
        {
            _clanContent.AddChild(RpgTheme.CreateSpacer(4));
            _clanContent.AddChild(RpgTheme.CreateInfoLabel("Descripcion:", 12));
            var descLabel = RpgTheme.CreateInfoLabel(desc, 12);
            descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            descLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
            _clanContent.AddChild(descLabel);
        }

        if (codexLines.Count > 0)
        {
            _clanContent.AddChild(RpgTheme.CreateSpacer(4));
            _clanContent.AddChild(RpgTheme.CreateInfoLabel("Codex:", 12));
            foreach (var line in codexLines)
            {
                _clanContent.AddChild(RpgTheme.CreateInfoLabel($"  - {line}", 11));
            }
        }

        _clanContent.AddChild(RpgTheme.CreateSpacer(8));

        // Buttons row
        var btnRow = RpgTheme.CreateRow();

        // Solicitar ingreso (only if no guild)
        if (string.IsNullOrEmpty(_state.UserGuildName))
        {
            var applyBtn = RpgTheme.CreateRpgButton("Solicitar Ingreso", false, 12);
            applyBtn.Pressed += () =>
            {
                if (_tcp != null && !string.IsNullOrEmpty(clanName))
                    _tcp.SendPacket(ClientPackets.WriteGuildApply($"{clanName},Solicito ingresar."));
            };
            btnRow.AddChild(applyBtn);
        }

        var backBtn = RpgTheme.CreateRpgButton("Volver", false, 12);
        backBtn.Pressed += () =>
        {
            _clanTabRequested = false;
            if (_tcp != null) _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        btnRow.AddChild(backBtn);

        _clanContent.AddChild(btnRow);
    }

    private void RefreshClanTab()
    {
        if (_tcp != null)
        {
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        }
    }
}

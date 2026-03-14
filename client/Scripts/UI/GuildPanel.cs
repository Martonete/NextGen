using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Multi-view guild panel:
/// - "List" view: guild list for players without a clan (can apply/view details)
/// - "Leader" view: leader/sublider management (members, applicants, codex, news)
/// - "Member" view: regular member view (read-only info, codex, members)
/// - "Details" view: detailed info about a specific guild
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class GuildPanel : RpgBaseForm
{
    private const char BF = '\u00BF'; // Delimiter used in guild data

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Current view
    private string _currentView = "";

    // Views (all built in BuildContent, toggled via visibility)
    private VBoxContainer? _listView;
    private VBoxContainer? _leaderView;
    private VBoxContainer? _memberView;
    private VBoxContainer? _detailsView;

    // List view controls
    private ItemList? _guildList;
    private LineEdit? _petitionEdit;

    // Leader/Member view controls
    private Label? _infoLabel;
    private ItemList? _memberList;
    private ItemList? _applicantList;
    private LineEdit? _rejectReasonEdit;
    private TextEdit? _newsEdit;
    private TextEdit? _codexEdit;

    // Details view controls
    private Label? _detailsLabel;

    // Parsed guild list entries
    private readonly List<(string name, string align, string level)> _guilds = new();
    // Parsed members
    private readonly List<string> _members = new();
    // Parsed applicants
    private readonly List<string> _applicants = new();

    public GuildPanel() : base("Clanes", new Vector2(420, 520), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        ContentContainer.AddChild(root);

        // ── List View ───────────────────────────────────────────────
        _listView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _listView.Visible = false;
        root.AddChild(_listView);

        _guildList = RpgTheme.CreateRpgItemList(0, 260);
        _listView.AddChild(_guildList);

        var listBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _listView.AddChild(listBtnRow);

        var detailsBtn = RpgTheme.CreateRpgButton("Ver detalles", false, 12);
        detailsBtn.CustomMinimumSize = new Vector2(120, 30);
        detailsBtn.Pressed += OnDetailsPressed;
        listBtnRow.AddChild(detailsBtn);

        _listView.AddChild(RpgTheme.CreateInfoLabel("Solicitud de ingreso:", 11));

        _petitionEdit = RpgTheme.CreateRpgInput("Escribe tu solicitud aqui...");
        _listView.AddChild(_petitionEdit);

        var applyBtn = RpgTheme.CreateRpgButton("Enviar solicitud", false, 12);
        applyBtn.CustomMinimumSize = new Vector2(140, 30);
        applyBtn.Pressed += OnApplyPressed;
        _listView.AddChild(applyBtn);

        // ── Leader View ─────────────────────────────────────────────
        _leaderView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _leaderView.Visible = false;
        root.AddChild(_leaderView);

        BuildLeaderViewContent();

        // ── Member View ─────────────────────────────────────────────
        _memberView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _memberView.Visible = false;
        root.AddChild(_memberView);

        BuildMemberViewContent();

        // ── Details View ────────────────────────────────────────────
        _detailsView = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _detailsView.Visible = false;
        root.AddChild(_detailsView);

        BuildDetailsViewContent();
    }

    private void BuildLeaderViewContent()
    {
        _infoLabel = RpgTheme.CreateInfoLabel("", 11);
        _infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _leaderView!.AddChild(_infoLabel);

        _leaderView.AddChild(RpgTheme.CreateInfoLabel("Miembros:", 12));

        _memberList = RpgTheme.CreateRpgItemList(0, 100);
        _leaderView.AddChild(_memberList);

        var expelBtn = RpgTheme.CreateRpgButton("Expulsar miembro", false, 12);
        expelBtn.CustomMinimumSize = new Vector2(140, 30);
        expelBtn.Pressed += OnExpelPressed;
        _leaderView.AddChild(expelBtn);

        _leaderView.AddChild(RpgTheme.CreateInfoLabel("Solicitudes pendientes:", 12));

        _applicantList = RpgTheme.CreateRpgItemList(0, 80);
        _leaderView.AddChild(_applicantList);

        _leaderView.AddChild(RpgTheme.CreateInfoLabel("Motivo de rechazo (opcional):", 10));

        _rejectReasonEdit = RpgTheme.CreateRpgInput("Motivo...");
        _leaderView.AddChild(_rejectReasonEdit);

        var appBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _leaderView.AddChild(appBtnRow);

        var acceptBtn = RpgTheme.CreateRpgButton("Aceptar", false, 12);
        acceptBtn.CustomMinimumSize = new Vector2(100, 30);
        acceptBtn.Pressed += OnAcceptPressed;
        appBtnRow.AddChild(acceptBtn);

        var rejectBtn = RpgTheme.CreateRpgButton("Rechazar", false, 12);
        rejectBtn.CustomMinimumSize = new Vector2(100, 30);
        rejectBtn.Pressed += OnRejectPressed;
        appBtnRow.AddChild(rejectBtn);

        _leaderView.AddChild(RpgTheme.CreateInfoLabel("Noticias del clan:", 12));

        _newsEdit = RpgTheme.CreateRpgTextEdit("", 0, 60);
        _leaderView.AddChild(_newsEdit);

        var saveNewsBtn = RpgTheme.CreateRpgButton("Guardar noticias", false, 12);
        saveNewsBtn.CustomMinimumSize = new Vector2(140, 30);
        saveNewsBtn.Pressed += OnSaveNewsPressed;
        _leaderView.AddChild(saveNewsBtn);

        _leaderView.AddChild(RpgTheme.CreateInfoLabel("Codex del clan:", 12));

        _codexEdit = RpgTheme.CreateRpgTextEdit("Escriba las reglas del clan (una por linea)...", 0, 80);
        _leaderView.AddChild(_codexEdit);

        var saveCodexBtn = RpgTheme.CreateRpgButton("Guardar codex", false, 12);
        saveCodexBtn.CustomMinimumSize = new Vector2(140, 30);
        saveCodexBtn.Pressed += OnSaveCodexPressed;
        _leaderView.AddChild(saveCodexBtn);
    }

    private void BuildMemberViewContent()
    {
        // Re-use _infoLabel only for leader — member gets its own
        var memberInfoLabel = RpgTheme.CreateInfoLabel("", 11);
        memberInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _memberView!.AddChild(memberInfoLabel);
        // Store as meta for later access
        _memberView.SetMeta("info_label", memberInfoLabel);

        _memberView.AddChild(RpgTheme.CreateInfoLabel("Miembros:", 12));

        var memberMemberList = RpgTheme.CreateRpgItemList(0, 200);
        _memberView.AddChild(memberMemberList);
        _memberView.SetMeta("member_list", memberMemberList);
    }

    private void BuildDetailsViewContent()
    {
        _detailsLabel = RpgTheme.CreateInfoLabel("", 11);
        _detailsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailsView!.AddChild(_detailsLabel);

        var backBtn = RpgTheme.CreateRpgButton("Volver a la lista", false, 12);
        backBtn.CustomMinimumSize = new Vector2(140, 30);
        backBtn.Pressed += () => {
            if (_tcp != null) _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        _detailsView.AddChild(backBtn);
    }

    public void ShowView(string viewType)
    {
        if (_state == null) return;
        _currentView = viewType;

        // Hide all views
        if (_listView != null) _listView.Visible = false;
        if (_leaderView != null) _leaderView.Visible = false;
        if (_memberView != null) _memberView.Visible = false;
        if (_detailsView != null) _detailsView.Visible = false;

        switch (viewType)
        {
            case "List":
                TitleText = "Lista de Clanes";
                PopulateListView();
                if (_listView != null) _listView.Visible = true;
                break;
            case "Leader":
                TitleText = "Gestion del Clan (Lider)";
                PopulateLeaderView();
                if (_leaderView != null) _leaderView.Visible = true;
                break;
            case "Member":
                TitleText = "Info del Clan (Miembro)";
                PopulateMemberView();
                if (_memberView != null) _memberView.Visible = true;
                break;
            case "Details":
                TitleText = "Detalles del Clan";
                PopulateDetailsView();
                if (_detailsView != null) _detailsView.Visible = true;
                break;
        }

        ShowForm();
    }

    // ── View population ─────────────────────────────────────────

    private void PopulateListView()
    {
        _guilds.Clear();
        var data = _state!.GuildListData;
        if (!string.IsNullOrEmpty(data))
        {
            var parts = data.Split(',');
            for (int i = 1; i < parts.Length; i++)
            {
                var fields = parts[i].Split('-');
                if (fields.Length >= 3)
                    _guilds.Add((fields[0], fields[1], fields[2]));
                else if (fields.Length >= 1)
                    _guilds.Add((fields[0], "?", "1"));
            }
        }

        _guildList!.Clear();
        foreach (var g in _guilds)
            _guildList.AddItem($"{g.name}  [{g.align}]  Nivel {g.level}");
    }

    private void PopulateLeaderView()
    {
        ParseLeaderData();
        UpdateInfoLabel();

        _memberList!.Clear();
        foreach (var m in _members) _memberList.AddItem(m);

        _applicantList!.Clear();
        foreach (var a in _applicants) _applicantList.AddItem(a);

        // Pre-populate codex
        if (_state != null && !string.IsNullOrEmpty(_state.GuildCodexText))
            _codexEdit!.Text = _state.GuildCodexText;
    }

    private void PopulateMemberView()
    {
        ParseMemberData();

        var memberInfoLabel = _memberView!.GetMeta("info_label").As<Label>();
        if (memberInfoLabel != null)
        {
            memberInfoLabel.Text = $"Nivel del clan: {_guildLevel} | Puntos: {_guildPoints}\n" +
                $"Lider: {_leader}\n" +
                $"Sub-lideres: {_sub1}, {_sub2}\n" +
                $"Reputacion: {_reputation}";
        }

        var memberMemberList = _memberView.GetMeta("member_list").As<ItemList>();
        if (memberMemberList != null)
        {
            memberMemberList.Clear();
            foreach (var m in _members) memberMemberList.AddItem(m);
        }
    }

    private void PopulateDetailsView()
    {
        ParseDetailsData();
    }

    // ── Parsing ──────────────────────────────────────────────────

    private string _guildPoints = "0";
    private string _guildLevel = "1";
    private string _leader = "";
    private string _sub1 = "";
    private string _sub2 = "";
    private string _reputation = "0";
    private string _castleSieges = "0";

    /// <summary>
    /// Parse IREDAEL (leader) format:
    /// points BF level BF leader BF sub1 BF sub2 BF castle1..4 BF repu BF ... BF ... BF castis BF
    /// guildCount BF guild1$align$level BF ... BF memberCount BF member1,member2 BF
    /// applicantCount BF app1 BF app2 ...
    /// </summary>
    private void ParseLeaderData()
    {
        _members.Clear();
        _applicants.Clear();
        var data = _state?.GuildInfoData ?? "";
        var parts = data.Split(BF);
        if (parts.Length < 12) return;

        _guildPoints = parts[0];
        _guildLevel = parts[1];
        _leader = parts[2];
        _sub1 = parts[3];
        _sub2 = parts[4];
        // parts[5..8] = castle positions (skip)
        _reputation = parts.Length > 9 ? parts[9] : "0";
        _castleSieges = parts.Length > 12 ? parts[12] : "0";

        // Guild list (skip for leader view — not needed)
        int idx = 13;
        if (idx < parts.Length)
        {
            int guildCount = 0;
            int.TryParse(parts[idx], out guildCount);
            idx += guildCount + 1;
        }

        // Members
        if (idx < parts.Length)
        {
            int memberCount = 0;
            int.TryParse(parts[idx], out memberCount);
            idx++;
            if (idx < parts.Length)
            {
                var memberNames = parts[idx].Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in memberNames) _members.Add(m.Trim());
                idx++;
            }
        }

        // Applicants
        if (idx < parts.Length)
        {
            int appCount = 0;
            int.TryParse(parts[idx], out appCount);
            idx++;
            for (int i = 0; i < appCount && idx < parts.Length; i++, idx++)
                _applicants.Add(parts[idx]);
        }
    }

    /// <summary>
    /// Parse IREDAEK (member) format:
    /// points BF level BF leader BF sub1 BF sub2 BF castle1..4 BF repu BF
    /// guildCount BF guild1-level-align BF ... BF memberCount BF member1,member2
    /// </summary>
    private void ParseMemberData()
    {
        _members.Clear();
        var data = _state?.GuildInfoData ?? "";
        var parts = data.Split(BF);
        if (parts.Length < 10) return;

        _guildPoints = parts[0];
        _guildLevel = parts[1];
        _leader = parts[2];
        _sub1 = parts[3];
        _sub2 = parts[4];
        _reputation = parts.Length > 9 ? parts[9] : "0";

        // Skip guild list
        int idx = 10;
        if (idx < parts.Length)
        {
            int guildCount = 0;
            int.TryParse(parts[idx], out guildCount);
            idx += guildCount + 1;
        }

        // Members
        if (idx < parts.Length)
        {
            int memberCount = 0;
            int.TryParse(parts[idx], out memberCount);
            idx++;
            if (idx < parts.Length)
            {
                var memberNames = parts[idx].Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in memberNames) _members.Add(m.Trim());
            }
        }
    }

    /// <summary>
    /// Parse DTLC (details) format:
    /// level BF alignment BF repu BF founder BF date BF leader BF sub1 BF sub2 BF
    /// memberCount BF codex1..8 BF desc BF name
    /// </summary>
    private void ParseDetailsData()
    {
        var data = _state?.GuildInfoData ?? "";
        var parts = data.Split(BF);
        if (parts.Length < 10) return;

        string level = parts[0];
        string alignment = parts[1];
        string repu = parts[2];
        string founder = parts[3];
        string date = parts[4];
        string leader = parts[5];
        string sub1 = parts[6];
        string sub2 = parts[7];
        string memberCount = parts[8];

        // Codex (next 8)
        var codex = new List<string>();
        for (int i = 9; i < Math.Min(parts.Length, 17); i++)
            if (!string.IsNullOrWhiteSpace(parts[i])) codex.Add(parts[i]);

        string desc = parts.Length > 17 ? parts[17] : "";
        string name = parts.Length > 18 ? parts[18] : "";

        if (_detailsLabel != null)
        {
            _detailsLabel.Text = $"Clan: {name}\n" +
                $"Nivel: {level} | Alineacion: {alignment}\n" +
                $"Fundador: {founder} | Fecha: {date}\n" +
                $"Lider: {leader}\n" +
                $"Sub-lideres: {sub1}, {sub2}\n" +
                $"Miembros: {memberCount} | Reputacion: {repu}\n" +
                $"\nDescripcion: {desc}\n" +
                (codex.Count > 0 ? $"\nCodex:\n{string.Join("\n", codex)}" : "");
        }
    }

    private void UpdateInfoLabel()
    {
        if (_infoLabel == null) return;
        _infoLabel.Text = $"Nivel del clan: {_guildLevel} | Puntos: {_guildPoints}\n" +
            $"Lider: {_leader}\n" +
            $"Sub-lideres: {_sub1}, {_sub2}\n" +
            $"Reputacion: {_reputation}\n" +
            $"Asedios: {_castleSieges}";
    }

    // ── Button Handlers ──────────────────────────────────────────

    private void OnDetailsPressed()
    {
        if (_guildList == null || _tcp == null) return;
        var selected = _guildList.GetSelectedItems();
        if (selected.Length == 0) return;
        int idx = selected[0];
        if (idx < 0 || idx >= _guilds.Count) return;
        _tcp.SendPacket(ClientPackets.WriteGuildDetails(_guilds[idx].name));
    }

    private void OnApplyPressed()
    {
        if (_guildList == null || _tcp == null || _petitionEdit == null) return;
        var selected = _guildList.GetSelectedItems();
        if (selected.Length == 0) return;
        int idx = selected[0];
        if (idx < 0 || idx >= _guilds.Count) return;
        string petition = _petitionEdit.Text.Trim();
        if (string.IsNullOrEmpty(petition)) petition = "Solicito ingresar.";
        _tcp.SendPacket(ClientPackets.WriteGuildApply($"{_guilds[idx].name},{petition}"));
    }

    private void OnAcceptPressed()
    {
        if (_applicantList == null || _tcp == null) return;
        var selected = _applicantList.GetSelectedItems();
        if (selected.Length == 0) return;
        string appText = _applicantList.GetItemText(selected[0]);
        // Applicant format: "Name: detail" — extract name
        string name = appText.Contains(':') ? appText[..appText.IndexOf(':')].Trim() : appText.Trim();
        _tcp.SendPacket(ClientPackets.WriteGuildAccept(name));
        // Refresh: request guild info again
        _tcp.SendPacket(ClientPackets.WriteGuildInfo());
    }

    private void OnRejectPressed()
    {
        if (_applicantList == null || _tcp == null) return;
        var selected = _applicantList.GetSelectedItems();
        if (selected.Length == 0) return;
        string appText = _applicantList.GetItemText(selected[0]);
        string name = appText.Contains(':') ? appText[..appText.IndexOf(':')].Trim() : appText.Trim();
        string reason = _rejectReasonEdit?.Text.Trim() ?? "";
        if (string.IsNullOrEmpty(reason)) reason = "Rechazado";
        _tcp.SendPacket(ClientPackets.WriteGuildReject($"{name},{reason}"));
        _tcp.SendPacket(ClientPackets.WriteGuildInfo());
    }

    private void OnExpelPressed()
    {
        if (_memberList == null || _tcp == null) return;
        var selected = _memberList.GetSelectedItems();
        if (selected.Length == 0) return;
        string name = _memberList.GetItemText(selected[0]).Trim();
        _tcp.SendPacket(ClientPackets.WriteGuildExpel(name));
        _tcp.SendPacket(ClientPackets.WriteGuildInfo());
    }

    private void OnSaveNewsPressed()
    {
        if (_newsEdit == null || _tcp == null) return;
        _tcp.SendPacket(ClientPackets.WriteGuildNews(_newsEdit.Text));
    }

    private void OnSaveCodexPressed()
    {
        if (_codexEdit == null || _tcp == null) return;
        string codexText = _codexEdit.Text.Trim();
        // Send codex update via guild update codex packet
        // VB6 format: desc + BF + codex lines joined by BF
        _tcp.SendPacket(ClientPackets.WriteGuildUpdateCodex(codexText));
    }
}

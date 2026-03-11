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
/// </summary>
public partial class GuildPanel : Control
{
    private const int PanelW = 420;
    private const int PanelH = 480;
    private const char BF = '\u00BF'; // Delimiter used in guild data

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Current view
    private string _currentView = "";

    // Controls
    private Label? _titleLabel;
    private Button? _closeBtn;
    private VBoxContainer? _contentBox;
    private ScrollContainer? _scrollContainer;

    // List view
    private ItemList? _guildList;
    private Button? _applyBtn;
    private Button? _detailsBtn;
    private LineEdit? _petitionEdit;

    // Leader/Member view
    private Label? _infoLabel;
    private ItemList? _memberList;
    private ItemList? _applicantList;
    private Button? _acceptBtn;
    private Button? _rejectBtn;
    private Button? _expelBtn;
    private TextEdit? _newsEdit;
    private Button? _saveNewsBtn;
    private TextEdit? _codexEdit;
    private Button? _saveCodexBtn;

    // Details view
    private Label? _detailsLabel;

    // Applicant comment/rejection reason
    private LineEdit? _rejectReasonEdit;

    // Parsed guild list entries
    private readonly List<(string name, string align, string level)> _guilds = new();
    // Parsed members
    private readonly List<string> _members = new();
    // Parsed applicants
    private readonly List<string> _applicants = new();

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        // Background
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Title
        _titleLabel = new Label();
        _titleLabel.Text = "Clanes";
        _titleLabel.Position = new Vector2(10, 4);
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_titleLabel);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Text = "X";
        _closeBtn.Position = new Vector2(PanelW - 28, 2);
        _closeBtn.Size = new Vector2(24, 24);
        _closeBtn.Pressed += () => Hide();
        AddChild(_closeBtn);

        // Content container
        _scrollContainer = new ScrollContainer();
        _scrollContainer.Position = new Vector2(8, 30);
        _scrollContainer.Size = new Vector2(PanelW - 16, PanelH - 38);
        AddChild(_scrollContainer);

        _contentBox = new VBoxContainer();
        _contentBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollContainer.AddChild(_contentBox);
    }

    public void ShowView(string viewType)
    {
        if (_contentBox == null || _state == null) return;
        _currentView = viewType;
        Visible = true;

        // Clear content
        foreach (var child in _contentBox.GetChildren())
            child.QueueFree();
        _guildList = null;
        _memberList = null;
        _applicantList = null;
        _rejectReasonEdit = null;
        _codexEdit = null;
        _saveCodexBtn = null;

        switch (viewType)
        {
            case "List": BuildListView(); break;
            case "Leader": BuildLeaderView(); break;
            case "Member": BuildMemberView(); break;
            case "Details": BuildDetailsView(); break;
        }
    }

    // ── List View (no clan) ──────────────────────────────────────────

    private void BuildListView()
    {
        _titleLabel!.Text = "Lista de Clanes";

        // Parse guild list: "count,name1-align1-level1,name2-align2-level2,..."
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

        _guildList = new ItemList();
        _guildList.CustomMinimumSize = new Vector2(PanelW - 24, 280);
        _guildList.AddThemeFontSizeOverride("font_size", 12);
        foreach (var g in _guilds)
            _guildList.AddItem($"{g.name}  [{g.align}]  Nivel {g.level}");
        _contentBox!.AddChild(_guildList);

        // Details button
        _detailsBtn = new Button();
        _detailsBtn.Text = "Ver detalles";
        _detailsBtn.Pressed += OnDetailsPressed;
        _contentBox.AddChild(_detailsBtn);

        // Petition text
        var petLabel = new Label();
        petLabel.Text = "Solicitud de ingreso:";
        petLabel.AddThemeFontSizeOverride("font_size", 11);
        _contentBox.AddChild(petLabel);

        _petitionEdit = new LineEdit();
        _petitionEdit.PlaceholderText = "Escribe tu solicitud aqui...";
        _contentBox.AddChild(_petitionEdit);

        // Apply button
        _applyBtn = new Button();
        _applyBtn.Text = "Enviar solicitud";
        _applyBtn.Pressed += OnApplyPressed;
        _contentBox.AddChild(_applyBtn);
    }

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

    // ── Leader View ──────────────────────────────────────────────────

    private void BuildLeaderView()
    {
        _titleLabel!.Text = "Gestion del Clan (Lider)";
        ParseLeaderData();

        // Info label (points, level, leader, subliders, etc.)
        _infoLabel = new Label();
        _infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoLabel.CustomMinimumSize = new Vector2(PanelW - 24, 0);
        _infoLabel.AddThemeFontSizeOverride("font_size", 11);
        _contentBox!.AddChild(_infoLabel);
        UpdateInfoLabel();

        // Members section
        var memLabel = new Label();
        memLabel.Text = "Miembros:";
        memLabel.AddThemeFontSizeOverride("font_size", 12);
        _contentBox.AddChild(memLabel);

        _memberList = new ItemList();
        _memberList.CustomMinimumSize = new Vector2(PanelW - 24, 100);
        _memberList.AddThemeFontSizeOverride("font_size", 11);
        foreach (var m in _members) _memberList.AddItem(m);
        _contentBox.AddChild(_memberList);

        _expelBtn = new Button();
        _expelBtn.Text = "Expulsar miembro";
        _expelBtn.Pressed += OnExpelPressed;
        _contentBox.AddChild(_expelBtn);

        // Applicants section
        var appLabel = new Label();
        appLabel.Text = "Solicitudes pendientes:";
        appLabel.AddThemeFontSizeOverride("font_size", 12);
        _contentBox.AddChild(appLabel);

        _applicantList = new ItemList();
        _applicantList.CustomMinimumSize = new Vector2(PanelW - 24, 80);
        _applicantList.AddThemeFontSizeOverride("font_size", 11);
        foreach (var a in _applicants) _applicantList.AddItem(a);
        _contentBox.AddChild(_applicantList);

        // Rejection reason input
        var reasonLabel = new Label();
        reasonLabel.Text = "Motivo de rechazo (opcional):";
        reasonLabel.AddThemeFontSizeOverride("font_size", 10);
        _contentBox.AddChild(reasonLabel);

        _rejectReasonEdit = new LineEdit();
        _rejectReasonEdit.PlaceholderText = "Motivo...";
        _rejectReasonEdit.CustomMinimumSize = new Vector2(PanelW - 24, 0);
        _contentBox.AddChild(_rejectReasonEdit);

        var btnRow = new HBoxContainer();
        _acceptBtn = new Button();
        _acceptBtn.Text = "Aceptar";
        _acceptBtn.Pressed += OnAcceptPressed;
        btnRow.AddChild(_acceptBtn);

        _rejectBtn = new Button();
        _rejectBtn.Text = "Rechazar";
        _rejectBtn.Pressed += OnRejectPressed;
        btnRow.AddChild(_rejectBtn);
        _contentBox.AddChild(btnRow);

        // News section
        var newsLabel = new Label();
        newsLabel.Text = "Noticias del clan:";
        newsLabel.AddThemeFontSizeOverride("font_size", 12);
        _contentBox.AddChild(newsLabel);

        _newsEdit = new TextEdit();
        _newsEdit.CustomMinimumSize = new Vector2(PanelW - 24, 60);
        _newsEdit.AddThemeFontSizeOverride("font_size", 11);
        _contentBox.AddChild(_newsEdit);

        _saveNewsBtn = new Button();
        _saveNewsBtn.Text = "Guardar noticias";
        _saveNewsBtn.Pressed += OnSaveNewsPressed;
        _contentBox.AddChild(_saveNewsBtn);

        // Codex section (guild code of conduct — up to 8 lines)
        var codexLabel = new Label();
        codexLabel.Text = "Codex del clan:";
        codexLabel.AddThemeFontSizeOverride("font_size", 12);
        _contentBox.AddChild(codexLabel);

        _codexEdit = new TextEdit();
        _codexEdit.CustomMinimumSize = new Vector2(PanelW - 24, 80);
        _codexEdit.AddThemeFontSizeOverride("font_size", 11);
        _codexEdit.PlaceholderText = "Escriba las reglas del clan (una por linea)...";
        // Pre-populate with existing codex
        if (_state != null && !string.IsNullOrEmpty(_state.GuildCodexText))
            _codexEdit.Text = _state.GuildCodexText;
        _contentBox.AddChild(_codexEdit);

        _saveCodexBtn = new Button();
        _saveCodexBtn.Text = "Guardar codex";
        _saveCodexBtn.Pressed += OnSaveCodexPressed;
        _contentBox.AddChild(_saveCodexBtn);
    }

    // ── Member View ──────────────────────────────────────────────────

    private void BuildMemberView()
    {
        _titleLabel!.Text = "Info del Clan (Miembro)";
        ParseMemberData();

        _infoLabel = new Label();
        _infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoLabel.CustomMinimumSize = new Vector2(PanelW - 24, 0);
        _infoLabel.AddThemeFontSizeOverride("font_size", 11);
        _contentBox!.AddChild(_infoLabel);
        UpdateInfoLabel();

        var memLabel = new Label();
        memLabel.Text = "Miembros:";
        memLabel.AddThemeFontSizeOverride("font_size", 12);
        _contentBox.AddChild(memLabel);

        _memberList = new ItemList();
        _memberList.CustomMinimumSize = new Vector2(PanelW - 24, 200);
        _memberList.AddThemeFontSizeOverride("font_size", 11);
        foreach (var m in _members) _memberList.AddItem(m);
        _contentBox.AddChild(_memberList);
    }

    // ── Details View ─────────────────────────────────────────────────

    private void BuildDetailsView()
    {
        _titleLabel!.Text = "Detalles del Clan";
        ParseDetailsData();

        _detailsLabel = new Label();
        _detailsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailsLabel.CustomMinimumSize = new Vector2(PanelW - 24, 0);
        _detailsLabel.AddThemeFontSizeOverride("font_size", 11);
        _contentBox!.AddChild(_detailsLabel);

        // Apply button (to go back to list)
        var backBtn = new Button();
        backBtn.Text = "Volver a la lista";
        backBtn.Pressed += () => {
            if (_tcp != null) _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        _contentBox.AddChild(backBtn);
    }

    // ── Parsing ──────────────────────────────────────────────────────

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

    // ── Button Handlers ──────────────────────────────────────────────

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

    // ── Drag ─────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < 28)
                {
                    _dragging = true;
                    _dragOffset = mb.GlobalPosition - GlobalPosition;
                }
                else
                    _dragging = false;
            }
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            GlobalPosition = mm.GlobalPosition - _dragOffset;
        }
    }
}

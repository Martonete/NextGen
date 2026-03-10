using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Party (group) panel — shows party members, leader tag, HP bars.
/// Commands: /NUEVAPARTY, /PARTY target, /FINPARTY, /CANCELAR, /PINFO, /SACAR target, /DARPARTIDO target.
/// The server sends party info as console messages (text-based). This panel
/// parses incoming console messages tagged as party info to populate the member list.
/// </summary>
public partial class PartyPanel : Control
{
    private const int PanelW = 300;
    private const int PanelH = 340;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private Label? _titleLabel;
    private Button? _closeBtn;
    private VBoxContainer? _contentBox;
    private ScrollContainer? _scrollContainer;

    // Member list
    private VBoxContainer? _memberListBox;

    // Action buttons
    private Button? _createBtn;
    private Button? _disbandBtn;
    private Button? _leaveBtn;
    private Button? _refreshBtn;

    // Invite row
    private HBoxContainer? _inviteRow;
    private LineEdit? _inviteNameEdit;
    private Button? _inviteBtn;

    // Context menu (right-click on member)
    private PopupMenu? _contextMenu;
    private int _contextMemberIndex = -1;

    // Party member data (populated from server info)
    private readonly List<PartyMemberInfo> _members = new();
    private bool _inParty;
    private bool _isLeader;

    /// <summary>Read-only access to party members for minimap markers.</summary>
    public IReadOnlyList<PartyMemberInfo> Members => _members;

    // Tracks whether we're expecting PINFO response lines
    private bool _parsingPinfo;
    private readonly List<string> _pinfoLines = new();

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
        _titleLabel.Text = "Grupo";
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

        // Context menu for right-click actions
        _contextMenu = new PopupMenu();
        _contextMenu.AddItem("Expulsar", 0);
        _contextMenu.AddItem("Dar liderazgo", 1);
        _contextMenu.IdPressed += OnContextMenuItemPressed;
        AddChild(_contextMenu);

        BuildUI();
    }

    private void BuildUI()
    {
        if (_contentBox == null) return;

        // Clear existing
        foreach (var child in _contentBox.GetChildren())
            child.QueueFree();

        // Invite row
        _inviteRow = new HBoxContainer();
        _inviteNameEdit = new LineEdit();
        _inviteNameEdit.PlaceholderText = "Nombre del jugador...";
        _inviteNameEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _inviteNameEdit.CustomMinimumSize = new Vector2(150, 0);
        _inviteRow.AddChild(_inviteNameEdit);

        _inviteBtn = new Button();
        _inviteBtn.Text = "Invitar";
        _inviteBtn.Pressed += OnInvitePressed;
        _inviteRow.AddChild(_inviteBtn);
        _contentBox.AddChild(_inviteRow);

        // Separator
        var sep1 = new HSeparator();
        sep1.CustomMinimumSize = new Vector2(0, 8);
        _contentBox.AddChild(sep1);

        // Members header
        var memHeader = new Label();
        memHeader.Text = "Miembros:";
        memHeader.AddThemeFontSizeOverride("font_size", 12);
        _contentBox.AddChild(memHeader);

        // Member list (scrollable VBox with HP bars)
        _memberListBox = new VBoxContainer();
        _memberListBox.CustomMinimumSize = new Vector2(PanelW - 24, 120);
        _contentBox.AddChild(_memberListBox);

        // Separator
        var sep2 = new HSeparator();
        sep2.CustomMinimumSize = new Vector2(0, 8);
        _contentBox.AddChild(sep2);

        // Action buttons row
        var btnRow = new HBoxContainer();

        _createBtn = new Button();
        _createBtn.Text = "Crear Grupo";
        _createBtn.Pressed += OnCreatePressed;
        _createBtn.CustomMinimumSize = new Vector2(85, 0);
        btnRow.AddChild(_createBtn);

        _disbandBtn = new Button();
        _disbandBtn.Text = "Disolver";
        _disbandBtn.Pressed += OnDisbandPressed;
        _disbandBtn.CustomMinimumSize = new Vector2(70, 0);
        btnRow.AddChild(_disbandBtn);

        _leaveBtn = new Button();
        _leaveBtn.Text = "Salir";
        _leaveBtn.Pressed += OnLeavePressed;
        _leaveBtn.CustomMinimumSize = new Vector2(55, 0);
        btnRow.AddChild(_leaveBtn);

        _contentBox.AddChild(btnRow);

        // Refresh button
        _refreshBtn = new Button();
        _refreshBtn.Text = "Actualizar info (/PINFO)";
        _refreshBtn.Pressed += OnRefreshPressed;
        _contentBox.AddChild(_refreshBtn);

        UpdateButtonStates();
        RefreshMemberList();
    }

    /// <summary>
    /// Toggle panel visibility. When showing, request party info from server.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Show();
            // Request party info
            RequestPartyInfo();
        }
    }

    /// <summary>
    /// Open the panel (called from ShowPartyForm packet or button press).
    /// </summary>
    public void OpenPanel()
    {
        Show();
        RequestPartyInfo();
    }

    /// <summary>
    /// Feed a console message line for party info parsing.
    /// Called from PacketHandler or Main when a console message arrives.
    /// Returns true if the message was consumed as party info.
    /// </summary>
    public bool TryParsePartyMessage(string text)
    {
        // Detect PINFO header
        if (text.Contains("--- Miembros del grupo ---"))
        {
            _parsingPinfo = true;
            _pinfoLines.Clear();
            _members.Clear();
            _inParty = true;
            return true;
        }

        // While parsing, collect indented member lines
        if (_parsingPinfo)
        {
            string trimmed = text.Trim();
            if (trimmed.Length > 0 && text.StartsWith("  "))
            {
                bool isLeader = trimmed.EndsWith("[Lider]");
                string name = isLeader ? trimmed.Replace("[Lider]", "").Trim() : trimmed;

                _members.Add(new PartyMemberInfo
                {
                    Name = name,
                    IsLeader = isLeader,
                });

                // Check if this is our user
                if (_state != null && name.Equals(_state.UserName, StringComparison.OrdinalIgnoreCase) && isLeader)
                    _isLeader = true;

                return true;
            }
            else
            {
                // Non-indented line = end of PINFO block
                _parsingPinfo = false;
                FinalizePinfoUpdate();
                return false; // Don't consume this line — it belongs to something else
            }
        }

        // Detect party-related console messages for state tracking
        if (text.Contains("Has creado un grupo"))
        {
            _inParty = true;
            _isLeader = true;
            _members.Clear();
            _members.Add(new PartyMemberInfo { Name = _state?.UserName ?? "Tu", IsLeader = true });
            UpdateButtonStates();
            RefreshMemberList();
            return false; // Still show in console
        }
        if (text.Contains("se ha unido al grupo"))
        {
            // Someone joined — request refresh
            RequestPartyInfo();
            return false;
        }
        if (text.Contains("ha abandonado el grupo") || text.Contains("ha sido expulsado del grupo"))
        {
            RequestPartyInfo();
            return false;
        }
        if (text.Contains("El grupo ha sido disuelto") || text.Contains("Has abandonado el grupo") || text.Contains("Has sido expulsado del grupo"))
        {
            _inParty = false;
            _isLeader = false;
            _members.Clear();
            UpdateButtonStates();
            RefreshMemberList();
            return false;
        }
        if (text.Contains("El nuevo lider del grupo es"))
        {
            RequestPartyInfo();
            return false;
        }

        return false;
    }

    private void FinalizePinfoUpdate()
    {
        // Determine if we are leader
        _isLeader = false;
        foreach (var m in _members)
        {
            if (m.IsLeader && _state != null && m.Name.Equals(_state.UserName, StringComparison.OrdinalIgnoreCase))
            {
                _isLeader = true;
                break;
            }
        }
        _inParty = _members.Count > 0;
        UpdateButtonStates();
        RefreshMemberList();
    }

    private void RequestPartyInfo()
    {
        _tcp?.SendPacket(ClientPackets.WriteSlashCommand("/PINFO"));
    }

    private void RefreshMemberList()
    {
        if (_memberListBox == null) return;

        // Clear
        foreach (var child in _memberListBox.GetChildren())
            child.QueueFree();

        if (_members.Count == 0)
        {
            var emptyLabel = new Label();
            emptyLabel.Text = _inParty ? "(Esperando info...)" : "No perteneces a un grupo.";
            emptyLabel.AddThemeFontSizeOverride("font_size", 11);
            emptyLabel.Modulate = new Color(0.6f, 0.6f, 0.6f);
            _memberListBox.AddChild(emptyLabel);
            return;
        }

        for (int i = 0; i < _members.Count; i++)
        {
            var member = _members[i];
            var row = new HBoxContainer();
            row.CustomMinimumSize = new Vector2(PanelW - 32, 24);

            // Leader tag + name
            var nameLabel = new Label();
            string tag = member.IsLeader ? "[L] " : "    ";
            nameLabel.Text = $"{tag}{member.Name}";
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (member.IsLeader)
                nameLabel.Modulate = new Color(1.0f, 0.85f, 0.2f); // Gold for leader
            row.AddChild(nameLabel);

            // HP bar (placeholder — server doesn't send HP info in PINFO)
            var hpBar = new ProgressBar();
            hpBar.CustomMinimumSize = new Vector2(60, 16);
            hpBar.MaxValue = 100;
            hpBar.Value = member.HpPercent > 0 ? member.HpPercent : 100;
            hpBar.ShowPercentage = false;

            // Style the HP bar green
            var styleFill = new StyleBoxFlat();
            styleFill.BgColor = new Color(0.2f, 0.7f, 0.2f);
            hpBar.AddThemeStyleboxOverride("fill", styleFill);

            var styleBg = new StyleBoxFlat();
            styleBg.BgColor = new Color(0.3f, 0.1f, 0.1f);
            hpBar.AddThemeStyleboxOverride("background", styleBg);

            row.AddChild(hpBar);

            _memberListBox.AddChild(row);

            // Right-click handler for kick/transfer leadership
            int memberIndex = i;
            row.GuiInput += (ev) => OnMemberRowInput(ev, memberIndex);
        }
    }

    private void OnMemberRowInput(InputEvent ev, int memberIndex)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            if (!_isLeader) return; // Only leader can kick/transfer
            if (memberIndex < 0 || memberIndex >= _members.Count) return;

            // Don't show context menu on yourself
            if (_state != null && _members[memberIndex].Name.Equals(_state.UserName, StringComparison.OrdinalIgnoreCase))
                return;

            _contextMemberIndex = memberIndex;
            _contextMenu?.Popup(new Rect2I(
                (int)(GlobalPosition.X + mb.Position.X),
                (int)(GlobalPosition.Y + mb.Position.Y + 30 + memberIndex * 24),
                120, 60));
        }
    }

    private void OnContextMenuItemPressed(long id)
    {
        if (_contextMemberIndex < 0 || _contextMemberIndex >= _members.Count) return;
        string targetName = _members[_contextMemberIndex].Name;

        switch (id)
        {
            case 0: // Kick
                _tcp?.SendPacket(ClientPackets.WriteSlashCommand($"/SACAR {targetName}"));
                break;
            case 1: // Transfer leadership
                _tcp?.SendPacket(ClientPackets.WriteSlashCommand($"/DARPARTIDO {targetName}"));
                break;
        }
    }

    private void UpdateButtonStates()
    {
        if (_createBtn != null) _createBtn.Disabled = _inParty;
        if (_disbandBtn != null) _disbandBtn.Disabled = !_inParty || !_isLeader;
        if (_leaveBtn != null) _leaveBtn.Disabled = !_inParty || _isLeader;
        if (_inviteBtn != null) _inviteBtn.Disabled = !_inParty || !_isLeader;
        if (_inviteNameEdit != null) _inviteNameEdit.Editable = _inParty && _isLeader;
        if (_refreshBtn != null) _refreshBtn.Disabled = !_inParty;
    }

    // ── Button Handlers ──────────────────────────────────────────────

    private void OnCreatePressed()
    {
        _tcp?.SendPacket(ClientPackets.WriteSlashCommand("/NUEVAPARTY"));
    }

    private void OnDisbandPressed()
    {
        _tcp?.SendPacket(ClientPackets.WriteSlashCommand("/FINPARTY"));
    }

    private void OnLeavePressed()
    {
        _tcp?.SendPacket(ClientPackets.WriteSlashCommand("/CANCELAR"));
    }

    private void OnInvitePressed()
    {
        if (_inviteNameEdit == null || _tcp == null) return;
        string name = _inviteNameEdit.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _tcp.SendPacket(ClientPackets.WriteSlashCommand($"/PARTY {name}"));
        _inviteNameEdit.Text = "";
    }

    private void OnRefreshPressed()
    {
        RequestPartyInfo();
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

/// <summary>
/// Stores info about a party member for display.
/// </summary>
public class PartyMemberInfo
{
    public string Name = "";
    public bool IsLeader;
    public int HpPercent = 100; // Default 100% — server does not send HP in PINFO
}

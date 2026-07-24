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
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class PartyPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Controls
    private VBoxContainer? _memberListBox;

    // Action buttons
    private TextureButton? _createBtn;
    private TextureButton? _disbandBtn;
    private TextureButton? _leaveBtn;
    private TextureButton? _refreshBtn;

    // Invite row
    private LineEdit? _inviteNameEdit;
    private TextureButton? _inviteBtn;

    // Context menu (right-click on member)
    private PopupMenu? _contextMenu;
    private int _contextMemberIndex = -1;

    // Party member data (populated from server info)
    private readonly List<PartyMemberInfo> _members = new();
    private bool _inParty;
    private bool _isLeader;
    private byte _serverPanelType;

    /// <summary>Read-only access to party members for minimap markers.</summary>
    public IReadOnlyList<PartyMemberInfo> Members => _members;
    public byte ServerPanelType => _serverPanelType;

    // Tracks whether we're expecting PINFO response lines
    private bool _parsingPinfo;
    private readonly List<string> _pinfoLines = new();

    public PartyPanel() : base("Grupo", new Vector2(300, 380), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        ContentContainer.AddChild(vbox);

        // Invite row
        var inviteRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(inviteRow);

        _inviteNameEdit = RpgTheme.CreateRpgInput("Nombre del jugador...", 150);
        inviteRow.AddChild(_inviteNameEdit);

        _inviteBtn = RpgTheme.CreateRpgButton("Invitar", false, 12);
        _inviteBtn.CustomMinimumSize = new Vector2(70, 30);
        _inviteBtn.Pressed += OnInvitePressed;
        inviteRow.AddChild(_inviteBtn);

        // Separator
        vbox.AddChild(RpgTheme.CreateSeparator());

        // Members header
        vbox.AddChild(RpgTheme.CreateInfoLabel("Miembros:", 13));

        // Member list (scrollable VBox)
        _memberListBox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _memberListBox.CustomMinimumSize = new Vector2(0, 120);
        vbox.AddChild(_memberListBox);

        // Separator
        vbox.AddChild(RpgTheme.CreateSeparator());

        // Action buttons row
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _createBtn = RpgTheme.CreateRpgButton("Crear", false, 11);
        _createBtn.CustomMinimumSize = new Vector2(70, 28);
        _createBtn.Pressed += OnCreatePressed;
        btnRow.AddChild(_createBtn);

        _disbandBtn = RpgTheme.CreateRpgButton("Disolver", false, 11);
        _disbandBtn.CustomMinimumSize = new Vector2(70, 28);
        _disbandBtn.Pressed += OnDisbandPressed;
        btnRow.AddChild(_disbandBtn);

        _leaveBtn = RpgTheme.CreateRpgButton("Salir", false, 11);
        _leaveBtn.CustomMinimumSize = new Vector2(60, 28);
        _leaveBtn.Pressed += OnLeavePressed;
        btnRow.AddChild(_leaveBtn);

        // Refresh button
        _refreshBtn = RpgTheme.CreateRpgButton("Actualizar (/PINFO)", true, 12);
        _refreshBtn.CustomMinimumSize = new Vector2(0, 30);
        _refreshBtn.Pressed += OnRefreshPressed;
        vbox.AddChild(_refreshBtn);

        // Context menu for right-click actions
        _contextMenu = new PopupMenu();
        _contextMenu.AddItem("Expulsar", 0);
        _contextMenu.AddItem("Dar liderazgo", 1);
        _contextMenu.IdPressed += OnContextMenuItemPressed;
        AddChild(_contextMenu);

        UpdateButtonStates();
        RefreshMemberList();
    }

    // Track last local HP to detect changes and refresh the bar live
    private int _lastLocalHpPct = -1;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!Visible || _state == null || _members.Count == 0) return;

        // Refresh member list when local HP changes (live bar for self)
        int curHpPct = _state.MaxHp > 0
            ? Math.Clamp(_state.MinHp * 100 / _state.MaxHp, 0, 100)
            : 100;
        if (curHpPct != _lastLocalHpPct)
        {
            _lastLocalHpPct = curHpPct;
            RefreshMemberList();
        }
    }

    /// <summary>
    /// Toggle panel visibility. When showing, request party info from server.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
        {
            HideForm();
        }
        else
        {
            ShowForm();
            RequestPartyInfo();
        }
    }

    /// <summary>
    /// Open the panel (called from ShowPartyForm packet or button press).
    /// </summary>
    public void OpenPanel(byte panelType = 0)
    {
        _serverPanelType = panelType;
        ShowForm();
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
                // Extract HP percent: " HP:75" at end
                int hpPct = 100;
                int hpTagIdx = trimmed.LastIndexOf(" HP:", StringComparison.Ordinal);
                if (hpTagIdx >= 0)
                {
                    if (int.TryParse(trimmed[(hpTagIdx + 4)..], out int parsedHp))
                        hpPct = Math.Clamp(parsedHp, 0, 100);
                    trimmed = trimmed[..hpTagIdx];
                }

                bool isLeader = trimmed.EndsWith("[Lider]");
                string name = isLeader ? trimmed.Replace("[Lider]", "").Trim() : trimmed;

                _members.Add(new PartyMemberInfo
                {
                    Name = name,
                    IsLeader = isLeader,
                    HpPercent = hpPct,
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
            var emptyLabel = RpgTheme.CreateInfoLabel(
                _inParty ? "(Esperando info...)" : "No perteneces a un grupo.", 11);
            emptyLabel.Modulate = new Color(0.6f, 0.6f, 0.6f);
            _memberListBox.AddChild(emptyLabel);
            return;
        }

        for (int i = 0; i < _members.Count; i++)
        {
            var member = _members[i];
            var row = RpgTheme.CreateRow(RpgTheme.SpacingSm);
            row.CustomMinimumSize = new Vector2(0, 24);

            // Leader tag + name
            string tag = member.IsLeader ? "[L] " : "    ";
            var nameLabel = RpgTheme.CreateInfoLabel($"{tag}{member.Name}", 11);
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (member.IsLeader)
                nameLabel.Modulate = new Color(1.0f, 0.85f, 0.2f); // Gold for leader
            row.AddChild(nameLabel);

            // Use live HP for the local player, PINFO HP% for others
            int hpPct = member.HpPercent;
            if (_state != null && member.Name.Equals(_state.UserName, StringComparison.OrdinalIgnoreCase) && _state.MaxHp > 0)
                hpPct = Math.Clamp(_state.MinHp * 100 / _state.MaxHp, 0, 100);

            // HP bar color: green above 50%, yellow 25-50%, red below 25%
            var fillColor = hpPct >= 50
                ? new Color(0.2f, 0.7f, 0.2f)
                : hpPct >= 25
                    ? new Color(0.85f, 0.75f, 0.0f)
                    : new Color(0.8f, 0.15f, 0.15f);

            var hpBar = RpgTheme.CreateRpgProgressBar(60, 16,
                fillColor: fillColor,
                bgColor: new Color(0.3f, 0.1f, 0.1f));
            hpBar.MaxValue = 100;
            hpBar.Value = hpPct > 0 ? hpPct : 100;
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

    // -- Button Handlers --

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

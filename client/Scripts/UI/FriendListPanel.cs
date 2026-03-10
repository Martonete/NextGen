using Godot;
using System.Collections.Generic;
using System.Linq;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Friend list panel — shows online/offline friends with add/remove/whisper actions.
/// Follows the same pattern as ForumPanel: programmatic nodes, dark theme, draggable.
/// </summary>
public partial class FriendListPanel : Control
{
    private const int PanelW = 320;
    private const int PanelH = 420;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // UI controls
    private Label? _titleLabel;
    private Label? _countLabel;
    private Button? _closeBtn;
    private ScrollContainer? _listScroll;
    private VBoxContainer? _listBox;
    private LineEdit? _addInput;
    private Button? _addBtn;

    // Track last known dirty state to rebuild list only when needed
    private bool _lastDirty;

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
        _titleLabel.Text = "Amigos";
        _titleLabel.Position = new Vector2(10, 4);
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_titleLabel);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Text = "X";
        _closeBtn.Position = new Vector2(PanelW - 28, 2);
        _closeBtn.Size = new Vector2(24, 24);
        _closeBtn.Pressed += OnClose;
        AddChild(_closeBtn);

        // Count label
        _countLabel = new Label();
        _countLabel.Text = "Amigos: 0 online / 0 total";
        _countLabel.Position = new Vector2(10, 26);
        _countLabel.Size = new Vector2(PanelW - 20, 18);
        _countLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        _countLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_countLabel);

        // Friend list (scrollable)
        _listScroll = new ScrollContainer();
        _listScroll.Position = new Vector2(8, 48);
        _listScroll.Size = new Vector2(PanelW - 16, PanelH - 110);
        AddChild(_listScroll);

        _listBox = new VBoxContainer();
        _listBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _listScroll.AddChild(_listBox);

        // Add friend bar (bottom)
        _addInput = new LineEdit();
        _addInput.Position = new Vector2(8, PanelH - 54);
        _addInput.Size = new Vector2(PanelW - 100, 28);
        _addInput.PlaceholderText = "Nombre del amigo...";
        _addInput.MaxLength = 30;
        AddChild(_addInput);

        _addBtn = new Button();
        _addBtn.Text = "Agregar";
        _addBtn.Position = new Vector2(PanelW - 86, PanelH - 54);
        _addBtn.Size = new Vector2(78, 28);
        _addBtn.Pressed += OnAddFriend;
        AddChild(_addBtn);

        // Close bottom button
        var closeBottomBtn = new Button { Text = "Cerrar" };
        closeBottomBtn.Position = new Vector2(PanelW - 80, PanelH - 22);
        closeBottomBtn.Size = new Vector2(70, 20);
        closeBottomBtn.Pressed += OnClose;
        AddChild(closeBottomBtn);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Rebuild list when dirty
        if (_state.FriendListDirty)
        {
            _state.FriendListDirty = false;
            BuildFriendList();
        }
    }

    /// <summary>
    /// Open (or refresh) the friend list panel.
    /// </summary>
    public void ShowPanel()
    {
        if (_state == null) return;
        BuildFriendList();
        Visible = true;
    }

    /// <summary>
    /// Toggle visibility.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
            OnClose();
        else
            ShowPanel();
    }

    private void BuildFriendList()
    {
        if (_listBox == null || _state == null) return;

        // Clear existing entries
        foreach (var child in _listBox.GetChildren())
            child.QueueFree();

        // Sort: online first, then alphabetical
        var sorted = _state.FriendList
            .OrderByDescending(f => f.Online)
            .ThenBy(f => f.Name)
            .ToList();

        // Update count label
        int online = sorted.Count(f => f.Online);
        _countLabel!.Text = $"Amigos: {online} online / {sorted.Count} total";

        if (sorted.Count == 0)
        {
            var emptyLbl = new Label { Text = "No tienes amigos en la lista." };
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _listBox.AddChild(emptyLbl);
            return;
        }

        foreach (var friend in sorted)
        {
            var row = new HBoxContainer();
            row.CustomMinimumSize = new Vector2(PanelW - 30, 30);

            // Status indicator (colored circle via label)
            var statusLbl = new Label();
            statusLbl.Text = friend.Online ? "●" : "●";
            statusLbl.CustomMinimumSize = new Vector2(18, 28);
            statusLbl.AddThemeColorOverride("font_color",
                friend.Online ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.5f, 0.5f, 0.5f));
            statusLbl.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(statusLbl);

            // Name label
            var nameLbl = new Label();
            nameLbl.Text = friend.Name;
            nameLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLbl.AddThemeColorOverride("font_color",
                friend.Online ? new Color(0.3f, 1.0f, 0.4f) : new Color(0.6f, 0.6f, 0.6f));
            nameLbl.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(nameLbl);

            // Whisper button
            var captured = friend;
            var whisperBtn = new Button();
            whisperBtn.Text = "Msg";
            whisperBtn.CustomMinimumSize = new Vector2(40, 26);
            whisperBtn.TooltipText = $"Susurrar a {friend.Name}";
            whisperBtn.Pressed += () => OnWhisper(captured.Name);
            row.AddChild(whisperBtn);

            // Remove button
            var removeBtn = new Button();
            removeBtn.Text = "X";
            removeBtn.CustomMinimumSize = new Vector2(28, 26);
            removeBtn.TooltipText = $"Eliminar a {friend.Name}";
            removeBtn.Pressed += () => OnRemoveFriend(captured.Name);
            row.AddChild(removeBtn);

            _listBox.AddChild(row);
        }
    }

    private void OnAddFriend()
    {
        if (_tcp == null || _state == null || _addInput == null) return;

        string name = _addInput.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Ingresa el nombre del amigo a agregar.",
                Color = "FF0000"
            });
            return;
        }

        _tcp.SendPacket(ClientPackets.WriteFriendAdd(name));
        _addInput.Text = "";

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Solicitud de amistad enviada a {name}.",
            Color = "00FF00"
        });
    }

    private void OnRemoveFriend(string name)
    {
        if (_tcp == null || _state == null) return;

        _tcp.SendPacket(ClientPackets.WriteFriendRemove(name));

        // Remove locally for immediate feedback
        _state.FriendList.RemoveAll(f =>
            f.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
        BuildFriendList();

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{name} eliminado de tu lista de amigos.",
            Color = "FFAA00"
        });
    }

    private void OnWhisper(string name)
    {
        if (_state == null) return;

        // Set whisper mode targeting this friend
        _state.ChatMode = 7; // whisper mode
        _state.WhisperTarget = name;
        _state.ChatModePrefix = $"/w \"{name}\" ";
        _state.ChatActive = true;

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Susurrando a {name}. Escribe tu mensaje.",
            Color = "CC88FF"
        });
    }

    private void OnClose()
    {
        Visible = false;
    }

    // ── Dragging ─────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < 28)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                }
                else
                {
                    _dragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Position - _dragOffset;
        }
    }
}

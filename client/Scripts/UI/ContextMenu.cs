using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Right-click context menu for interacting with characters in the game world.
/// Shows different options for players, NPCs, and pets.
/// Programmatically created (no .tscn).
/// </summary>
public partial class ContextMenu : PanelContainer
{
    private const int ItemHeight = 24;
    private const int ItemPadding = 6;
    private const int MinWidth = 140;
    private const int FontSize = 11;

    private GameState? _state;
    private AoTcpClient? _tcp;

    private VBoxContainer? _container;
    private readonly List<Button> _buttons = new();

    // Target info (set when menu opens)
    private int _targetCharIndex;
    private string _targetName = "";
    private bool _targetIsNpc;
    private bool _targetIsPet;

    /// <summary>
    /// Fired when user selects "Whisper" — Main.cs should activate chat with /msg prefix.
    /// Arg: target character name.
    /// </summary>
    public event Action<string>? OnWhisper;

    /// <summary>
    /// Fired when user selects "Add Friend".
    /// Arg: target character name.
    /// </summary>
    public event Action<string>? OnAddFriend;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        // Dark panel style
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.96f);
        stylebox.BorderColor = new Color(0.5f, 0.4f, 0.3f, 0.8f);
        stylebox.SetBorderWidthAll(1);
        stylebox.SetCornerRadiusAll(2);
        stylebox.SetContentMarginAll(2);
        AddThemeStyleboxOverride("panel", stylebox);

        _container = new VBoxContainer();
        _container.AddThemeConstantOverride("separation", 1);
        AddChild(_container);

        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        ZIndex = 110; // above tooltips
    }

    /// <summary>
    /// Open context menu at the given screen position for a character on a tile.
    /// Scans GameState.Characters for a character at (tileX, tileY).
    /// Returns true if a target was found and menu was shown.
    /// </summary>
    public bool TryOpen(Vector2 screenPos, int tileX, int tileY)
    {
        if (_state == null) return false;

        // Find character at this tile
        Character? target = null;
        foreach (var kvp in _state.Characters)
        {
            var ch = kvp.Value;
            if (ch.PosX == tileX && ch.PosY == tileY && kvp.Key != _state.UserCharIndex)
            {
                target = ch;
                break;
            }
        }

        if (target == null) return false;

        _targetCharIndex = target.CharIndex;
        _targetName = target.Name;
        _targetIsNpc = target.NpcNumber > 0;
        // Simple pet detection: NPCs that share the user's name or common pet patterns
        // In AO, pets are NPCs owned by a player — detected by NPC type on server side.
        // Client-side heuristic: NPC with NpcNumber > 0 near user.
        _targetIsPet = false; // Will be set from context if needed

        // Strip clan tag from name for commands (name is "PlayerName<ClanTag>")
        string cleanName = _targetName;
        int tagIdx = cleanName.IndexOf('<');
        if (tagIdx > 0)
            cleanName = cleanName.Substring(0, tagIdx).Trim();

        BuildMenu(cleanName);
        ShowAt(screenPos);
        return true;
    }

    /// <summary>
    /// Close the context menu.
    /// </summary>
    public void CloseMenu()
    {
        Visible = false;
    }

    public bool IsOpen => Visible;

    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;

        // Close on any click outside the menu
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            var localPos = GetLocalMousePosition();
            if (localPos.X < 0 || localPos.Y < 0 || localPos.X > Size.X || localPos.Y > Size.Y)
            {
                CloseMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        // Close on Escape
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            CloseMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildMenu(string cleanName)
    {
        // Clear all existing children (buttons + separators)
        if (_container != null)
        {
            foreach (var child in _container.GetChildren())
                child.QueueFree();
        }
        _buttons.Clear();

        if (_targetIsNpc)
        {
            AddMenuItem("Hablar", () => {
                // VB6: left-click on NPC tile triggers talk
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/HABLAR"));
                CloseMenu();
            });
            AddMenuItem("Comerciar", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/COMERCIAR"));
                CloseMenu();
            });
            AddSeparator();
            AddMenuItem("Atacar", () => {
                // Click on tile to target, then attack
                _tcp?.SendPacket(ClientPackets.WriteAttack());
                CloseMenu();
            }, new Color(1f, 0.3f, 0.3f));
        }
        else
        {
            // Player target
            AddMenuItem($"Susurrar a {TruncateName(cleanName)}", () => {
                OnWhisper?.Invoke(cleanName);
                CloseMenu();
            });
            AddMenuItem("Invitar al Grupo", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/PARTY {cleanName}"));
                CloseMenu();
            });
            AddMenuItem("Agregar Amigo", () => {
                OnAddFriend?.Invoke(cleanName);
                CloseMenu();
            });
            AddMenuItem("Comerciar", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/COMERCIAR"));
                CloseMenu();
            });
            AddMenuItem("Ver Info", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/MIRAR {cleanName}"));
                CloseMenu();
            });
            AddSeparator();
            AddMenuItem("Atacar", () => {
                _tcp?.SendPacket(ClientPackets.WriteAttack());
                CloseMenu();
            }, new Color(1f, 0.3f, 0.3f));
        }
    }

    private void AddMenuItem(string text, Action action, Color? color = null)
    {
        if (_container == null) return;

        var btn = new Button();
        btn.Text = text;
        btn.Alignment = HorizontalAlignment.Left;
        btn.CustomMinimumSize = new Vector2(MinWidth, ItemHeight);
        btn.FocusMode = FocusModeEnum.None;
        btn.MouseDefaultCursorShape = CursorShape.PointingHand;
        btn.AddThemeFontSizeOverride("font_size", FontSize);

        // Flat button style with hover highlight
        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = Colors.Transparent;
        normalStyle.SetContentMarginIndividual(ItemPadding, 2, ItemPadding, 2);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.3f, 0.3f, 0.6f, 0.5f);
        hoverStyle.SetContentMarginIndividual(ItemPadding, 2, ItemPadding, 2);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(0.2f, 0.2f, 0.5f, 0.7f);
        pressedStyle.SetContentMarginIndividual(ItemPadding, 2, ItemPadding, 2);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        if (color.HasValue)
            btn.AddThemeColorOverride("font_color", color.Value);
        else
            btn.AddThemeColorOverride("font_color", Colors.White);

        btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.9f, 0.5f));

        btn.Pressed += action;
        _container.AddChild(btn);
        _buttons.Add(btn);
    }

    private void AddSeparator()
    {
        if (_container == null) return;

        var sep = new HSeparator();
        sep.CustomMinimumSize = new Vector2(MinWidth, 4);
        sep.AddThemeStyleboxOverride("separator", new StyleBoxFlat
        {
            BgColor = new Color(0.4f, 0.35f, 0.3f, 0.5f)
        });
        _container.AddChild(sep);
        // Separators are not tracked in _buttons since they don't need action cleanup;
        // they will be freed when container children are cleared via QueueFree on buttons.
        // Actually, we need to free them too. Let's add a dummy button reference.
        // Better approach: just clear all children.
    }

    private Vector2 _pendingPosition;
    private bool _needsPosition;

    private void ShowAt(Vector2 screenPos)
    {
        _pendingPosition = screenPos;
        _needsPosition = true;
        Visible = true;

        // Initial position (may be adjusted next frame once layout is computed)
        GlobalPosition = screenPos;
    }

    public override void _Process(double delta)
    {
        if (_needsPosition && Visible)
        {
            _needsPosition = false;
            var viewportSize = GetViewportRect().Size;
            float x = _pendingPosition.X;
            float y = _pendingPosition.Y;

            if (x + Size.X > viewportSize.X)
                x = viewportSize.X - Size.X;
            if (y + Size.Y > viewportSize.Y)
                y = viewportSize.Y - Size.Y;
            if (x < 0) x = 0;
            if (y < 0) y = 0;

            GlobalPosition = new Vector2(x, y);
        }
    }

    private static string TruncateName(string name)
    {
        return name.Length > 14 ? name.Substring(0, 14) + "..." : name;
    }

    /// <summary>
    /// Clear all menu children properly (buttons + separators).
    /// Called before rebuilding menu and on close.
    /// </summary>
    private void ClearChildren()
    {
        if (_container == null) return;
        foreach (var child in _container.GetChildren())
        {
            child.QueueFree();
        }
        _buttons.Clear();
    }
}

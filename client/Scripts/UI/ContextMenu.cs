using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Right-click context menu for interacting with characters in the game world.
/// Now uses RpgTheme for consistent RPG-styled UI.
/// </summary>
public partial class ContextMenu : PanelContainer
{
    private const int MinWidth = 140;

    private GameState? _state;
    private AoTcpClient? _tcp;

    private VBoxContainer? _container;
    private readonly List<Button> _buttons = new();

    private int _targetCharIndex;
    private string _targetName = "";
    private bool _targetIsNpc;

    public event Action<string>? OnWhisper;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        // RPG-styled context menu
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.10f, 0.09f, 0.07f, 0.97f);
        stylebox.BorderColor = new Color(0.55f, 0.45f, 0.3f, 0.95f);
        stylebox.SetBorderWidthAll(2);
        stylebox.SetCornerRadiusAll(3);
        stylebox.ContentMarginLeft = 4; stylebox.ContentMarginRight = 4;
        stylebox.ContentMarginTop = 4;  stylebox.ContentMarginBottom = 4;
        AddThemeStyleboxOverride("panel", stylebox);

        _container = new VBoxContainer();
        _container.AddThemeConstantOverride("separation", 1);
        AddChild(_container);

        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        ZIndex = RpgBaseForm.ZContextMenu;
    }

    public bool TryOpen(Vector2 screenPos, int tileX, int tileY)
    {
        if (_state == null) return false;

        Character? target = null;
        foreach (var kvp in _state.Characters)
        {
            var ch = kvp.Value;
            if (ch.PosX == tileX && ch.PosY == tileY && kvp.Key != _state.UserCharIndex)
            { target = ch; break; }
        }

        if (target == null) return false;

        _targetCharIndex = target.CharIndex;
        _targetName = target.Name;
        _targetIsNpc = target.NpcNumber > 0;

        string cleanName = _targetName;
        int tagIdx = cleanName.IndexOf('<');
        if (tagIdx > 0) cleanName = cleanName.Substring(0, tagIdx).Trim();

        BuildMenu(cleanName);
        ShowAt(screenPos);
        return true;
    }

    public void CloseMenu()
    {
        Visible = false;
    }

    public bool IsOpen => Visible;

    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            var localPos = GetLocalMousePosition();
            if (localPos.X < 0 || localPos.Y < 0 || localPos.X > Size.X || localPos.Y > Size.Y)
            {
                CloseMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            CloseMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildMenu(string cleanName)
    {
        if (_container != null)
        {
            foreach (var child in _container.GetChildren())
                child.QueueFree();
        }
        _buttons.Clear();

        if (_targetIsNpc)
        {
            AddMenuItem("Hablar", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/HABLAR"));
                CloseMenu();
            });
            AddMenuItem("Comerciar", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/COMERCIAR"));
                CloseMenu();
            });
            AddSeparator();
            AddMenuItem("Atacar", () => {
                _tcp?.SendPacket(ClientPackets.WriteAttack());
                CloseMenu();
            }, new Color(1f, 0.3f, 0.3f));
        }
        else
        {
            AddMenuItem($"Susurrar a {TruncateName(cleanName)}", () => {
                OnWhisper?.Invoke(cleanName);
                CloseMenu();
            });
            AddMenuItem("Invitar al Grupo", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/PARTY {cleanName}"));
                CloseMenu();
            });
            AddMenuItem("Comerciar", () => {
                _tcp?.SendPacket(ClientPackets.WriteTalk($"/COMERCIAR"));
                CloseMenu();
            });
            AddMenuItem("Ver Info", () => {
                _tcp?.SendPacket(ClientPackets.WritePlayerInfo(cleanName));
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

        var btn = RpgTheme.CreateRpgContextMenuItem(text, action);
        if (color.HasValue)
            btn.AddThemeColorOverride("font_color", color.Value);
        btn.CustomMinimumSize = new Vector2(MinWidth, 26);
        btn.MouseDefaultCursorShape = CursorShape.PointingHand;

        _container.AddChild(btn);
        _buttons.Add(btn);
    }

    private void AddSeparator()
    {
        if (_container == null) return;
        var sep = new HSeparator();
        sep.CustomMinimumSize = new Vector2(MinWidth, 4);
        var sepStyle = new StyleBoxFlat();
        sepStyle.BgColor = new Color(0.4f, 0.35f, 0.25f, 0.5f);
        sep.AddThemeStyleboxOverride("separator", sepStyle);
        _container.AddChild(sep);
    }

    private Vector2 _pendingPosition;
    private bool _needsPosition;

    private void ShowAt(Vector2 screenPos)
    {
        _pendingPosition = screenPos;
        _needsPosition = true;
        Visible = true;
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
            if (x + Size.X > viewportSize.X) x = viewportSize.X - Size.X;
            if (y + Size.Y > viewportSize.Y) y = viewportSize.Y - Size.Y;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            GlobalPosition = new Vector2(x, y);
        }
    }

    private static string TruncateName(string name) =>
        name.Length > 14 ? name.Substring(0, 14) + "..." : name;
}

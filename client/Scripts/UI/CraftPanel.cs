using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Crafting panel for Blacksmith (weapons + armors tabs) and Carpenter (single list).
/// VB6: frmHerrero / frmCarpintero — shows buildable items list with material costs.
/// Player selects an item and clicks "Construir" to request construction.
/// </summary>
public partial class CraftPanel : Control
{
    private const int PanelW = 360;
    private const int PanelH = 420;
    private const int TitleBarH = 28;
    private const int ItemH = 24;
    private const int ListY = 60;
    private const int ListH = 280;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // UI elements
    private Panel? _bg;
    private Label? _titleLabel;
    private Button? _closeBtn;
    private Button? _tab1Btn;    // Weapons / (hidden for carpenter)
    private Button? _tab2Btn;    // Armors / (hidden for carpenter)
    private ScrollContainer? _scroll;
    private VBoxContainer? _list;
    private Label? _matLabel;    // Material requirements for selected item
    private Button? _buildBtn;

    // State
    private bool _dragging;
    private Vector2 _dragOffset;
    private bool _isBlacksmith; // true=blacksmith, false=carpenter
    private int _activeTab;     // 0=weapons/carp items, 1=armors
    private int _selectedIndex = -1;
    private List<CraftEntry> _currentList = new();

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
        BuildUI();
        Visible = false;
    }

    public void ShowBlacksmith()
    {
        _isBlacksmith = true;
        _activeTab = 0;
        _selectedIndex = -1;
        if (_tab1Btn != null) { _tab1Btn.Visible = true; _tab1Btn.Text = "Armas"; }
        if (_tab2Btn != null) { _tab2Btn.Visible = true; _tab2Btn.Text = "Armaduras"; }
        if (_titleLabel != null) _titleLabel.Text = "Herrería";
        RefreshList();
        CenterOnScreen();
        Visible = true;
    }

    public void ShowCarpenter()
    {
        _isBlacksmith = false;
        _activeTab = 0;
        _selectedIndex = -1;
        if (_tab1Btn != null) _tab1Btn.Visible = false;
        if (_tab2Btn != null) _tab2Btn.Visible = false;
        if (_titleLabel != null) _titleLabel.Text = "Carpintería";
        RefreshList();
        CenterOnScreen();
        Visible = true;
    }

    public void ClosePanel()
    {
        Visible = false;
    }

    private void CenterOnScreen()
    {
        var vp = GetViewportRect().Size;
        Position = new Vector2((vp.X - PanelW) / 2, (vp.Y - PanelH) / 2);
    }

    private void BuildUI()
    {
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;

        _bg = new Panel();
        _bg.Size = new Vector2(PanelW, PanelH);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.12f, 0.16f, 0.95f);
        style.BorderColor = new Color(0.6f, 0.5f, 0.3f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        _bg.AddThemeStyleboxOverride("panel", style);
        AddChild(_bg);

        // Title
        _titleLabel = new Label();
        _titleLabel.Position = new Vector2(10, 5);
        _titleLabel.Size = new Vector2(PanelW - 50, TitleBarH);
        _titleLabel.Text = "Herrería";
        _titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_titleLabel);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Position = new Vector2(PanelW - 30, 4);
        _closeBtn.Size = new Vector2(24, 22);
        _closeBtn.Text = "X";
        _closeBtn.Pressed += ClosePanel;
        AddChild(_closeBtn);

        // Tab buttons
        _tab1Btn = new Button();
        _tab1Btn.Position = new Vector2(10, 32);
        _tab1Btn.Size = new Vector2(100, 24);
        _tab1Btn.Text = "Armas";
        _tab1Btn.Pressed += () => { _activeTab = 0; _selectedIndex = -1; RefreshList(); };
        AddChild(_tab1Btn);

        _tab2Btn = new Button();
        _tab2Btn.Position = new Vector2(115, 32);
        _tab2Btn.Size = new Vector2(100, 24);
        _tab2Btn.Text = "Armaduras";
        _tab2Btn.Pressed += () => { _activeTab = 1; _selectedIndex = -1; RefreshList(); };
        AddChild(_tab2Btn);

        // Scroll list
        _scroll = new ScrollContainer();
        _scroll.Position = new Vector2(8, ListY);
        _scroll.Size = new Vector2(PanelW - 16, ListH);
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        AddChild(_scroll);

        _list = new VBoxContainer();
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_list);

        // Material label
        _matLabel = new Label();
        _matLabel.Position = new Vector2(10, ListY + ListH + 4);
        _matLabel.Size = new Vector2(PanelW - 20, 40);
        _matLabel.AddThemeFontSizeOverride("font_size", 10);
        _matLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        _matLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_matLabel);

        // Build button
        _buildBtn = new Button();
        _buildBtn.Position = new Vector2(PanelW / 2 - 55, PanelH - 36);
        _buildBtn.Size = new Vector2(110, 28);
        _buildBtn.Text = "Construir";
        _buildBtn.Pressed += OnBuildPressed;
        AddChild(_buildBtn);
    }

    private void RefreshList()
    {
        if (_state == null || _list == null) return;

        // Choose the right list
        if (_isBlacksmith)
            _currentList = _activeTab == 0 ? _state.SmithWeapons : _state.SmithArmors;
        else
            _currentList = _state.CarpItems;

        // Clear old items
        foreach (var child in _list.GetChildren())
            child.QueueFree();

        // Highlight tabs
        if (_tab1Btn != null && _tab1Btn.Visible)
        {
            _tab1Btn.Modulate = _activeTab == 0 ? Colors.White : new Color(0.6f, 0.6f, 0.6f);
            _tab2Btn!.Modulate = _activeTab == 1 ? Colors.White : new Color(0.6f, 0.6f, 0.6f);
        }

        for (int i = 0; i < _currentList.Count; i++)
        {
            var entry = _currentList[i];
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(PanelW - 32, ItemH);
            btn.Text = entry.Name;
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", 11);
            int idx = i;
            btn.Pressed += () => SelectItem(idx);

            // Flat style
            var flat = new StyleBoxFlat();
            flat.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.8f);
            flat.SetBorderWidthAll(0);
            btn.AddThemeStyleboxOverride("normal", flat);

            var hover = new StyleBoxFlat();
            hover.BgColor = new Color(0.25f, 0.25f, 0.35f, 0.9f);
            hover.SetBorderWidthAll(0);
            btn.AddThemeStyleboxOverride("hover", hover);

            _list.AddChild(btn);
        }

        UpdateMaterialLabel();
        UpdateBuildButton();
    }

    private void SelectItem(int idx)
    {
        _selectedIndex = idx;

        // Highlight selected row
        var children = _list?.GetChildren();
        if (children != null)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is Button btn)
                {
                    var s = new StyleBoxFlat();
                    s.BgColor = i == idx
                        ? new Color(0.3f, 0.3f, 0.5f, 1f)
                        : new Color(0.15f, 0.15f, 0.2f, 0.8f);
                    s.SetBorderWidthAll(0);
                    btn.AddThemeStyleboxOverride("normal", s);
                }
            }
        }

        UpdateMaterialLabel();
        UpdateBuildButton();
    }

    private void UpdateMaterialLabel()
    {
        if (_matLabel == null) return;

        if (_selectedIndex < 0 || _selectedIndex >= _currentList.Count)
        {
            _matLabel.Text = "Selecciona un item para ver los materiales.";
            return;
        }

        var entry = _currentList[_selectedIndex];
        if (_isBlacksmith)
        {
            _matLabel.Text = $"Lingotes: Hierro={entry.Mat1}  Plata={entry.Mat2}  Oro={entry.Mat3}";
        }
        else
        {
            _matLabel.Text = entry.Mat2 > 0
                ? $"Madera: {entry.Mat1}  Madera Élfica: {entry.Mat2}"
                : $"Madera: {entry.Mat1}";
        }
    }

    private void UpdateBuildButton()
    {
        if (_buildBtn != null)
            _buildBtn.Disabled = _selectedIndex < 0 || _selectedIndex >= _currentList.Count;
    }

    private void OnBuildPressed()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _currentList.Count || _tcp == null) return;

        var entry = _currentList[_selectedIndex];
        if (_isBlacksmith)
            _tcp.SendPacket(ClientPackets.WriteConstructSmith((short)entry.ObjIndex));
        else
            _tcp.SendPacket(ClientPackets.WriteConstructCarp((short)entry.ObjIndex));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.GlobalPosition - GlobalPosition;
                    AcceptEvent();
                }
                else if (!mb.Pressed)
                    _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            GlobalPosition = mm.GlobalPosition - _dragOffset;
            AcceptEvent();
        }
    }
}

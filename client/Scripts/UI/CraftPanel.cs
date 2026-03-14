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
/// Now uses RpgBaseForm for consistent RPG chrome (frame, title, close, drag).
/// </summary>
public partial class CraftPanel : RpgBaseForm
{
    private const int ItemH = 24;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // UI elements
    private HBoxContainer? _tabBar;
    private Control? _scrollArea;
    private VBoxContainer? _list;
    private Label? _matLabel;
    private TextureButton? _buildBtn;

    // State
    private bool _isBlacksmith; // true=blacksmith, false=carpenter
    private int _activeTab;     // 0=weapons/carp items, 1=armors
    private int _selectedIndex = -1;
    private List<CraftEntry> _currentList = new();

    public CraftPanel() : base("Herreria", new Vector2(360, 420), "v2") { }

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // Tab bar (Armas / Armaduras) — hidden for carpenter
        _tabBar = RpgTheme.CreateTabBar(new[] { "Armas", "Armaduras" }, OnTabChanged);
        vbox.AddChild(_tabBar);

        // Scroll list
        _scrollArea = RpgTheme.CreateScrollArea();
        _scrollArea.CustomMinimumSize = new Vector2(0, 230);
        vbox.AddChild(_scrollArea);
        _list = _scrollArea.GetMeta("content").As<VBoxContainer>();
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Material label
        _matLabel = RpgTheme.CreateInfoLabel("Selecciona un item para ver los materiales.", 11);
        _matLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _matLabel.CustomMinimumSize = new Vector2(0, 36);
        vbox.AddChild(_matLabel);

        // Build button
        var footerRow = RpgTheme.CreateRow();
        footerRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(footerRow);

        _buildBtn = RpgTheme.CreateRpgButton("Construir", true, 16);
        _buildBtn.CustomMinimumSize = new Vector2(140, 32);
        _buildBtn.Pressed += OnBuildPressed;
        _buildBtn.Disabled = true;
        footerRow.AddChild(_buildBtn);
    }

    private void OnTabChanged(int tabIndex)
    {
        _activeTab = tabIndex;
        _selectedIndex = -1;
        RefreshList();
    }

    public void ShowBlacksmith()
    {
        _isBlacksmith = true;
        _activeTab = 0;
        _selectedIndex = -1;
        if (_tabBar != null) _tabBar.Visible = true;
        TitleText = "Herreria";
        RpgTheme.SetTabBarActive(_tabBar!, 0);
        RefreshList();
        ShowForm();
    }

    public void ShowCarpenter()
    {
        _isBlacksmith = false;
        _activeTab = 0;
        _selectedIndex = -1;
        if (_tabBar != null) _tabBar.Visible = false;
        TitleText = "Carpinteria";
        RefreshList();
        ShowForm();
    }

    public void ClosePanel()
    {
        HideForm();
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

        for (int i = 0; i < _currentList.Count; i++)
        {
            var entry = _currentList[i];
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(0, ItemH);
            btn.Text = entry.Name;
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.FocusMode = FocusModeEnum.None;
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
                ? $"Madera: {entry.Mat1}  Madera Elfica: {entry.Mat2}"
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
}

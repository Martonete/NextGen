using System;
using System.Linq;
using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.UI;

namespace ArgentumNextgen;

public partial class Main
{
    private const int HudLayoutVersion = 2;
    private FloatingHudWindow? _inventoryWindow, _statusWindow, _quickWindow, _actionsWindow, _minimapWindow;
    private Quickbar? _quickbar;
    private HudActionBar? _actionBar;
    private Panel? _worldInfoPanel;
    private HBoxContainer? _hudToggles;
    private Control?[] InventoryControls => new Control?[] { _invTabButton, _spellTabButton, _inventoryPanel, _spellPanel, _dydToggle, _lanzarButton, _infoButton, _spellUpButton, _spellDownButton };
    private Control?[] StatusControls => new Control?[] { _goldIcon, _goldLabel, _agilidadLabel, _fuerzaLabel, _statSepLabel };
    private TextureButton?[] ActionButtons => new[] { _grupoButton, _opcionesButton, _estadisticasButton, _clanesButton };

    private void SetupFloatingHud()
    {
        FloatingHudWindow Window(string caption, Vector2 size, bool chromeless = false, bool moveHandle = false)
        {
            var window = new FloatingHudWindow { Caption = caption, Size = size, ZIndex = 2, Chromeless = chromeless, MoveHandle = moveHandle };
            _gameUI!.AddChild(window);
            window.LayoutChanged = SaveHudLayout;
            return window;
        }
        float scale = ResolutionManager.UIScale;
        _inventoryWindow = Window("Inventario y hechizos", new Vector2(240 * scale, 254 * scale + 44));
        _statusWindow = Window("Estado", new Vector2(140 * scale, 150 * scale + 44));
        _quickWindow = Window("Macros", new Vector2(Quickbar.BarWidth, Quickbar.BarHeight + 18), chromeless: true, moveHandle: true);
        _actionsWindow = Window("Accesos", new Vector2(HudActionBar.BarWidth, HudActionBar.BarHeight + 18), chromeless: true, moveHandle: true);
        _minimapWindow = Window("Minimapa", new Vector2(120 * scale, 120 * scale + 44));
        foreach (var control in InventoryControls) if (control != null) { control.SetAnchorsPreset(Control.LayoutPreset.TopLeft); control.Reparent(_inventoryWindow.Content, false); }
        foreach (var control in StatusControls) if (control != null) { control.SetAnchorsPreset(Control.LayoutPreset.TopLeft); control.Reparent(_statusWindow.Content, false); }
        _statBarOverlay?.Reparent(_statusWindow.Content, false);
        if (_coordsLabel != null) _coordsLabel.Visible = false;
        if (_mapaButton != null) _mapaButton.Visible = false;

        _actionBar = new HudActionBar();
        _actionsWindow.Content.AddChild(_actionBar);
        _actionBar.Attach(ActionButtons);

        if (_minimapPanel != null)
        {
            _minimapPanel.Reparent(_minimapWindow.Content, false);
            _minimapPanel.Visible = true;
        }
        if (_minimapBorder != null) _minimapBorder.Visible = false;

        _worldInfoPanel = new Panel { ZIndex = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        _worldInfoPanel.AddThemeStyleboxOverride("panel", SacredTheme.Surface(
            new Color(0.025f, 0.035f, 0.04f, 0.78f), new Color(SacredTheme.Bronze, 0.62f), 5));
        _gameUI!.AddChild(_worldInfoPanel);
        foreach (var label in new[] { _onlineLabel, _fpsLabel })
            if (label != null) { label.Reparent(_worldInfoPanel, false); label.Visible = true; label.MouseFilter = Control.MouseFilterEnum.Ignore; }
        // Re-fit the backpack window's height whenever the visible content changes shape
        // (Inventario's 5x5 grid vs. Hechizos' list + Lanzar/Info buttons).
        if (_inventoryUI != null) _inventoryUI.TabChanged = () => _inventoryWindow?.FitToContent();
        _quickbar = new Quickbar { State = _state, Data = _gameData,
            SelectedSpell = () => _spellPanel?.SelectedSlot ?? -1,
            SelectedObject = () => _inventoryPanel?.SelectedSlot ?? -1 };
        _quickbar.Execute = (slot, index) =>
        {
            if (_tcp == null) return;
            if (slot.Spell)
            {
                _tcp.SendPacket(ClientPackets.WriteCastSpell((byte)(index + 1)));
                _state.UsingSkill = 2;
            }
            else if (_state.MainTimer.Check(TimersIndex.UseItemWithDblClick))
                // Same opcode as double-clicking the item in the backpack: the server
                // decides whether to equip/unequip or consume, depending on the item.
                _tcp.SendPacket(ClientPackets.WriteUseItemClick((byte)(index + 1)));
        };
        _quickWindow.Content.AddChild(_quickbar);
        _hudToggles = new HBoxContainer { Position = new Vector2(50, 44), ZIndex = 3 };
        _gameUI!.AddChild(_hudToggles);
        foreach (var pair in new[] { ("Mochila", _inventoryWindow), ("Estado", _statusWindow), ("Minimapa", _minimapWindow), ("Accesos", _actionsWindow), ("Macros", _quickWindow) })
        {
            var button = EntryTheme.Button(pair.Item1);
            button.CustomMinimumSize = new Vector2(75, 28);
            button.FocusMode = FocusModeEnum.None;
            button.Pressed += pair.Item2.ToggleWindow;
            _hudToggles.AddChild(button);
        }
        RepositionUI();
        var cfg = new ConfigFile();
        cfg.Load(ProjectSettings.GlobalizePath("user://floating-hud.cfg"));
        bool compatibleLayout = (int)cfg.GetValue("meta", "version", 0) == HudLayoutVersion;
        var area = GetViewportRect().Size;
        float margin = 24 * scale;
        float inventoryX = area.X - _inventoryWindow.Size.X - margin;
        float quickScale = _quickWindow.Scale.X;
        float actionScale = _actionsWindow.Scale.X;
        float quickY = area.Y - _quickWindow.Size.Y * quickScale - margin;
        var defaults = new[]
        {
            new Vector2(inventoryX, 40 * scale),
            new Vector2(area.X - _statusWindow.Size.X - margin, area.Y - _statusWindow.Size.Y - margin),
            new Vector2((area.X - _quickWindow.Size.X * quickScale) / 2f, quickY),
            new Vector2((area.X - _actionsWindow.Size.X * actionScale) / 2f,
                quickY - _actionsWindow.Size.Y * actionScale - 8 * scale),
            new Vector2(Math.Max(margin, inventoryX - _minimapWindow.Size.X - margin), 40 * scale)
        };
        int n = 0;
        foreach (var window in HudWindows())
        {
            string id = n.ToString();
            Vector2 defaultPosition = defaults[n++];
            window.Position = compatibleLayout ? (Vector2)cfg.GetValue(id, "position", defaultPosition) : defaultPosition;
            bool defaultVisible = window != _minimapWindow || _state.Config.ShowMinimap;
            window.Visible = compatibleLayout ? (bool)cfg.GetValue(id, "visible", defaultVisible) : defaultVisible;
            // Only a size the player actually dragged is restored — otherwise the
            // window keeps auto-fitting to its content on every resolution change.
            if (compatibleLayout && !window.Chromeless && (bool)cfg.GetValue(id, "resized", false))
                window.ApplySavedSize((Vector2)cfg.GetValue(id, "size", window.Size));
            window.ClampToScreen();
        }
    }

    private FloatingHudWindow[] HudWindows() => new[] { _inventoryWindow!, _statusWindow!, _quickWindow!, _actionsWindow!, _minimapWindow! };
    private void SaveHudLayout()
    {
        if (_quickWindow == null) return;
        var cfg = new ConfigFile(); int n = 0;
        cfg.SetValue("meta", "version", HudLayoutVersion);
        foreach (var window in HudWindows())
        {
            string id = (n++).ToString();
            cfg.SetValue(id, "position", window.Position);
            cfg.SetValue(id, "visible", window.Visible);
            cfg.SetValue(id, "resized", window.UserResized);
            if (window.UserResized) cfg.SetValue(id, "size", window.Size);
        }
        if (_minimapWindow != null) _state.Config.ShowMinimap = _minimapWindow.Visible;
        if (cfg.Save(ProjectSettings.GlobalizePath("user://floating-hud.cfg")) != Error.Ok)
            GD.PrintErr("[HUD] No se pudo guardar la disposición de ventanas.");
    }

    private void LayoutFloatingHud()
    {
        if (_inventoryWindow == null || _statusWindow == null) return;
        int S(int value) => ResolutionManager.S(value);
        LayoutInventoryContent();
        LayoutStatusContent();
        LayoutMinimapContent();
        if (!_inventoryWindow.UserResized) _inventoryWindow.Size = new Vector2(S(240), _inventoryWindow.Size.Y);
        if (!_statusWindow.UserResized) _statusWindow.Size = new Vector2(S(130), _statusWindow.Size.Y);
        _inventoryWindow.FitToContent();
        _statusWindow.FitToContent();
        if (_quickWindow != null && !_quickWindow.UserResized)
        {
            float barScale = Math.Min(1.3f, (ResolutionManager.WindowWidth - S(240) - S(72)) / (float)Quickbar.BarWidth);
            _quickWindow.Scale = Vector2.One * Math.Max(0.7f, barScale);
        }
        if (_actionsWindow != null)
        {
            float actionScale = Math.Min(1.2f, (ResolutionManager.WindowWidth - S(80)) / (float)HudActionBar.BarWidth);
            _actionsWindow.Scale = Vector2.One * Math.Max(0.75f, actionScale);
        }
        foreach (var window in HudWindows()) if (window != null) window.ClampToScreen();
    }

    /// <summary>
    /// Compact layout for the "Mochila" window: tabs on top, then either the 5x5
    /// inventory grid + DyD toggle, or the spell list + Lanzar/Info + move arrows —
    /// same purpose-built local offsets as LayoutStatusContent, so nothing here is
    /// inherited from the old full-height sidebar's spacing.
    /// </summary>
    private void LayoutInventoryContent()
    {
        int S(int v) => ResolutionManager.S(v);
        float uiScale = ResolutionManager.UIScale;
        int colW = S(220);

        if (_invTabButton != null) { _invTabButton.Position = Vector2.Zero; _invTabButton.Size = new Vector2(colW / 2f, S(32)); }
        if (_spellTabButton != null) { _spellTabButton.Position = new Vector2(colW / 2f, 0); _spellTabButton.Size = new Vector2(colW / 2f, S(32)); }

        float gridTop = S(36);
        // _inventoryPanel/_spellPanel keep their raw (unscaled) Size and get visually
        // scaled via .Scale = uiScale. Read the size back instead of hardcoding it here,
        // so this stays correct if either panel's Size is ever changed elsewhere.
        Vector2 gridSize = (_inventoryPanel?.Size ?? new Vector2(171, 174)) * uiScale;
        if (_inventoryPanel != null)
            _inventoryPanel.Position = new Vector2((colW - gridSize.X) / 2f, gridTop);
        if (_dydToggle != null)
            // Directly under the grid, inside the left margin — the old sidebar X put
            // this a few px to the LEFT of the window's own padding, off-screen.
            _dydToggle.Position = new Vector2(S(4), gridTop + gridSize.Y + S(4));

        Vector2 spellSize = (_spellPanel?.Size ?? new Vector2(190, 186)) * uiScale;
        // Left-aligned rather than centered — a centered panel's right edge crept past
        // where the move-arrows sit, so the arrows rendered on top of the spell list
        // instead of beside it. Left-aligning guarantees the gap on the right.
        if (_spellPanel != null) _spellPanel.Position = new Vector2(S(4), gridTop);
        float spellRight = S(4) + spellSize.X;
        float spellBottom = gridTop + spellSize.Y;
        if (_spellUpButton != null) _spellUpButton.Position = new Vector2(spellRight + S(4), gridTop + S(40));
        if (_spellDownButton != null) _spellDownButton.Position = new Vector2(spellRight + S(4), gridTop + S(74));
        float halfBtn = (colW - S(8)) / 2f;
        if (_lanzarButton != null) { _lanzarButton.Position = new Vector2(0, spellBottom + S(4)); _lanzarButton.Size = new Vector2(halfBtn, S(28)); }
        if (_infoButton != null) { _infoButton.Position = new Vector2(halfBtn + S(8), spellBottom + S(4)); _infoButton.Size = new Vector2(halfBtn, S(28)); }
    }

    /// <summary>
    /// Compact status-only layout: gold, agility/strength and five vital bars.
    /// </summary>
    private void LayoutStatusContent()
    {
        int S(int v) => ResolutionManager.S(v);
        const int leftX = 4, leftW = 120;

        if (_goldIcon != null) { _goldIcon.Position = new Vector2(S(leftX), S(2)); _goldIcon.Size = new Vector2(S(14), S(14)); }
        if (_goldLabel != null) { _goldLabel.Position = new Vector2(S(leftX + 18), S(3)); _goldLabel.Size = new Vector2(S(leftW - 18), S(14)); }

        const int statY = 20;
        if (_agilidadLabel != null) { _agilidadLabel.Position = new Vector2(S(leftX), S(statY)); _agilidadLabel.Size = new Vector2(S(leftW / 2 - 3), S(14)); }
        if (_statSepLabel != null) { _statSepLabel.Position = new Vector2(S(leftX + leftW / 2 - 3), S(statY)); _statSepLabel.Size = new Vector2(S(8), S(14)); }
        if (_fuerzaLabel != null) { _fuerzaLabel.Position = new Vector2(S(leftX + leftW / 2 + 5), S(statY)); _fuerzaLabel.Size = new Vector2(S(leftW / 2 - 5), S(14)); }

        // Bars start right under the stat row; StatBarOverlay.IntrinsicSize.Y == S(80) (5 rows).
        const int barsY = 38;
        if (_statBarOverlay != null) _statBarOverlay.Position = new Vector2(S(leftX), S(barsY));

    }

    private void LayoutMinimapContent()
    {
        if (_minimapWindow == null || _minimapPanel == null) return;
        float mapSize = ResolutionManager.S(100);
        _minimapPanel.Position = new Vector2(Math.Max(0, (_minimapWindow.Content.Size.X - mapSize) / 2f), 0);
        _minimapPanel.Size = new Vector2(mapSize, mapSize);
        _minimapWindow.FitToContent();
    }

    private bool OverFloatingHud(Vector2 point) => _quickbar?.IsOver(point) == true
        || (_inventoryWindow != null && HudWindows().Any(w => w.IsVisibleInTree() && w.GetGlobalRect().HasPoint(point)))
        || (_hudToggles?.IsVisibleInTree() == true && _hudToggles.GetGlobalRect().HasPoint(point));
}

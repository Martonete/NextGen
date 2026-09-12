using System;
using System.Linq;
using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.UI;

namespace ArgentumNextgen;

public partial class Main
{
    private FloatingHudWindow? _inventoryWindow, _statusWindow, _quickWindow;
    private Quickbar? _quickbar;
    private HBoxContainer? _hudToggles;
    private Control?[] InventoryControls => new Control?[] { _invTabButton, _spellTabButton, _inventoryPanel, _spellPanel, _dydToggle, _lanzarButton, _infoButton, _spellUpButton, _spellDownButton };
    private Control?[] StatusControls => new Control?[] { _goldIcon, _goldLabel, _coordsLabel, _onlineLabel, _fpsLabel, _agilidadLabel, _fuerzaLabel, _statSepLabel, _mapaButton, _grupoButton, _opcionesButton, _estadisticasButton, _clanesButton };
    private TextureButton?[] StatusButtons => new[] { _mapaButton, _grupoButton, _opcionesButton, _estadisticasButton, _clanesButton };

    private void SetupFloatingHud()
    {
        FloatingHudWindow Window(string caption, Vector2 size)
        {
            var window = new FloatingHudWindow { Caption = caption, Size = size, ZIndex = 2 };
            _gameUI!.AddChild(window);
            window.LayoutChanged = SaveHudLayout;
            return window;
        }
        float scale = ResolutionManager.UIScale;
        _inventoryWindow = Window("Inventario y hechizos", new Vector2(240 * scale, 254 * scale + 44));
        _statusWindow = Window("Estado y menú", new Vector2(240 * scale, 190 * scale + 44));
        _quickWindow = Window("Accesos rápidos · click para configurar", new Vector2(580, 98));
        foreach (var control in InventoryControls) if (control != null) { control.SetAnchorsPreset(Control.LayoutPreset.TopLeft); control.Reparent(_inventoryWindow.Content, false); }
        foreach (var control in StatusControls) if (control != null) { control.SetAnchorsPreset(Control.LayoutPreset.TopLeft); control.Reparent(_statusWindow.Content, false); }
        _statBarOverlay?.Reparent(_statusWindow.Content, false);
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
            else
                // Same opcode as double-clicking the item in the backpack: the server
                // decides whether to equip/unequip or consume, depending on the item.
                _tcp.SendPacket(ClientPackets.WriteUseItemClick((byte)(index + 1)));
        };
        _quickWindow.Content.AddChild(_quickbar);
        _hudToggles = new HBoxContainer { Position = new Vector2(50, 44), ZIndex = 3 };
        _gameUI!.AddChild(_hudToggles);
        foreach (var pair in new[] { ("Mochila", _inventoryWindow), ("Estado", _statusWindow), ("Macros", _quickWindow) })
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
        var area = GetViewportRect().Size;
        float margin = 24 * scale;
        var defaults = new[] { new Vector2(area.X - _inventoryWindow.Size.X - margin, 40 * scale), new Vector2(area.X - _statusWindow.Size.X - margin, area.Y - _statusWindow.Size.Y - margin), new Vector2(margin, 100) };
        int n = 0;
        foreach (var window in HudWindows())
        {
            string id = n.ToString();
            window.Position = (Vector2)cfg.GetValue(id, "position", defaults[n++]);
            window.Visible = (bool)cfg.GetValue(id, "visible", true);
            // Only a size the player actually dragged is restored — otherwise the
            // window keeps auto-fitting to its content on every resolution change.
            if ((bool)cfg.GetValue(id, "resized", false))
                window.ApplySavedSize((Vector2)cfg.GetValue(id, "size", window.Size));
            window.ClampToScreen();
        }
    }

    private FloatingHudWindow[] HudWindows() => new[] { _inventoryWindow!, _statusWindow!, _quickWindow! };
    private void SaveHudLayout()
    {
        if (_quickWindow == null) return;
        var cfg = new ConfigFile(); int n = 0;
        foreach (var window in HudWindows())
        {
            string id = (n++).ToString();
            cfg.SetValue(id, "position", window.Position);
            cfg.SetValue(id, "visible", window.Visible);
            cfg.SetValue(id, "resized", window.UserResized);
            if (window.UserResized) cfg.SetValue(id, "size", window.Size);
        }
        if (cfg.Save(ProjectSettings.GlobalizePath("user://floating-hud.cfg")) != Error.Ok)
            GD.PrintErr("[HUD] No se pudo guardar la disposición de ventanas.");
    }

    private void LayoutFloatingHud()
    {
        if (_inventoryWindow == null || _statusWindow == null) return;
        int S(int value) => ResolutionManager.S(value);
        LayoutInventoryContent();
        LayoutStatusContent();
        if (!_inventoryWindow.UserResized) _inventoryWindow.Size = new Vector2(S(240), _inventoryWindow.Size.Y);
        if (!_statusWindow.UserResized) _statusWindow.Size = new Vector2(S(240), _statusWindow.Size.Y);
        _inventoryWindow.FitToContent();
        _statusWindow.FitToContent();
        if (_quickWindow != null && !_quickWindow.UserResized)
        {
            float barScale = Math.Min(1.3f, (ResolutionManager.WindowWidth - S(240) - S(72)) / 580f);
            _quickWindow.Scale = Vector2.One * Math.Max(0.7f, barScale);
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
    /// Compact two-column layout for the "Estado" window: gold + Agilidad/Fuerza +
    /// the five stat bars on the left, the five menu buttons + Online/FPS on the right.
    /// Purpose-built local offsets (not the old full-sidebar coordinates RepositionUI
    /// computes for these same controls) — that's what used to leave the window mostly
    /// empty: the sidebar design spread them across ~450px of a screen-tall column.
    /// </summary>
    private void LayoutStatusContent()
    {
        int S(int v) => ResolutionManager.S(v);
        const int leftX = 4, leftW = 150, rightX = 158, rightW = 78;

        if (_goldIcon != null) { _goldIcon.Position = new Vector2(S(leftX), S(2)); _goldIcon.Size = new Vector2(S(14), S(14)); }
        if (_goldLabel != null) { _goldLabel.Position = new Vector2(S(leftX + 18), S(3)); _goldLabel.Size = new Vector2(S(leftW - 18), S(14)); }

        const int statY = 20;
        if (_agilidadLabel != null) { _agilidadLabel.Position = new Vector2(S(leftX), S(statY)); _agilidadLabel.Size = new Vector2(S(leftW / 2 - 3), S(14)); }
        if (_statSepLabel != null) { _statSepLabel.Position = new Vector2(S(leftX + leftW / 2 - 3), S(statY)); _statSepLabel.Size = new Vector2(S(8), S(14)); }
        if (_fuerzaLabel != null) { _fuerzaLabel.Position = new Vector2(S(leftX + leftW / 2 + 5), S(statY)); _fuerzaLabel.Size = new Vector2(S(leftW / 2 - 5), S(14)); }

        // Bars start right under the stat row; StatBarOverlay.IntrinsicSize.Y == S(80) (5 rows).
        const int barsY = 38;
        if (_statBarOverlay != null) _statBarOverlay.Position = new Vector2(S(leftX), S(barsY));

        const int coordsY = barsY + 84; // 4px gap under the 80px-tall bar stack
        if (_coordsLabel != null)
        {
            _coordsLabel.Position = new Vector2(S(leftX), S(coordsY));
            _coordsLabel.Size = new Vector2(S(leftW), S(26));
            _coordsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        }

        var buttons = StatusButtons;
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null) { buttons[i]!.Position = new Vector2(S(rightX), S(2 + i * 23)); buttons[i]!.Size = new Vector2(S(rightW), S(20)); }

        if (_onlineLabel != null) { _onlineLabel.Position = new Vector2(S(rightX), S(coordsY)); _onlineLabel.Size = new Vector2(S(rightW), S(12)); _onlineLabel.HorizontalAlignment = HorizontalAlignment.Center; }
        if (_fpsLabel != null) { _fpsLabel.Position = new Vector2(S(rightX), S(coordsY + 14)); _fpsLabel.Size = new Vector2(S(rightW), S(12)); _fpsLabel.HorizontalAlignment = HorizontalAlignment.Center; }
    }

    private bool OverFloatingHud(Vector2 point) => _quickbar?.IsOver(point) == true
        || (_inventoryWindow != null && HudWindows().Any(w => w.IsVisibleInTree() && w.GetGlobalRect().HasPoint(point)))
        || (_hudToggles?.IsVisibleInTree() == true && _hudToggles.GetGlobalRect().HasPoint(point));
}

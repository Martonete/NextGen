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
            else if (!_state.Inventory[index].Equipped)
                _tcp.SendPacket(ClientPackets.WriteEquipItem((byte)(index + 1)));
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
            string id = (n++).ToString(); cfg.SetValue(id, "position", window.Position); cfg.SetValue(id, "visible", window.Visible);
        }
        if (cfg.Save(ProjectSettings.GlobalizePath("user://floating-hud.cfg")) != Error.Ok)
            GD.PrintErr("[HUD] No se pudo guardar la disposición de ventanas.");
    }

    private void LayoutFloatingHud()
    {
        if (_inventoryWindow == null || _statusWindow == null) return;
        int S(int value) => ResolutionManager.S(value);
        int extra = Math.Max(0, ResolutionManager.WindowWidth - ResolutionManager.SidebarX - S(240));
        int side = ResolutionManager.SidebarX + extra / 2;
        var inventoryOrigin = new Vector2(side - S(14), S(122));
        var statusOrigin = new Vector2(side, ResolutionManager.BottomBarY - S(160));
        foreach (var control in InventoryControls) if (control != null) control.Position -= inventoryOrigin;
        foreach (var control in StatusControls) if (control != null) control.Position -= statusOrigin;
        if (_statBarOverlay != null) _statBarOverlay.Position = -statusOrigin;
        _inventoryWindow.Size = new Vector2(S(240), S(254) + 44);
        _statusWindow.Size = new Vector2(S(240), S(190) + 44);
        if (_quickWindow != null)
        {
            float barScale = Math.Min(1.3f, (ResolutionManager.WindowWidth - S(240) - S(72)) / 580f);
            _quickWindow.Scale = Vector2.One * Math.Max(0.7f, barScale);
        }
        foreach (var window in HudWindows()) if (window != null) window.ClampToScreen();
    }

    private bool OverFloatingHud(Vector2 point) => _quickbar?.IsOver(point) == true
        || (_inventoryWindow != null && HudWindows().Any(w => w.IsVisibleInTree() && w.GetGlobalRect().HasPoint(point)))
        || (_hudToggles?.IsVisibleInTree() == true && _hudToggles.GetGlobalRect().HasPoint(point));
}

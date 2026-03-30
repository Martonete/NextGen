using Godot;
using System;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Game;

/// <summary>
/// Updates HUD labels, stat bars, macro status, minimap party names,
/// and arrow projectiles each frame. Extracted from Main.cs.
/// </summary>
public class GameUIUpdater
{
    private readonly GameState _state;

    // HUD labels
    private Label? _expLabel;
    private Label? _goldLabel;
    private Label? _levelLabel;
    private Label? _nameLabel;
    private Label? _onlineLabel;
    private Label? _coordsLabel;
    private Label? _armorLabel;
    private Label? _helmLabel;
    private Label? _shieldLabel;
    private Label? _weaponLabel;
    private Label? _fuerzaLabel;
    private Label? _agilidadLabel;
    private Label? _fpsLabel;
    private Label? _macroStatusLabel;
    private Control? _btnCastiGM;
    private StatBarOverlay? _statBarOverlay;
    private MinimapPanel? _minimapPanel;
    private PartyPanel? _partyPanel;

    // Delta-check caches — only write .Text when value changes
    private string _cachedExp = "";
    private string _cachedGold = "";
    private string _cachedLevel = "";
    private string _cachedName = "";
    private string _cachedOnline = "";
    private string _cachedCoords = "";
    private string _cachedArmor = "";
    private string _cachedHelm = "";
    private string _cachedShield = "";
    private string _cachedWeapon = "";
    private string _cachedFuerza = "";
    private string _cachedAgilidad = "";
    private string _cachedFps = "";
    private string _cachedMacroText = "";
    private bool _cachedMacroVisible = false;
    private bool _cachedBtnCastiVisible = false;

    /// <summary>Callback to get WorldRenderer for arrow redraw.</summary>
    public Action? QueueWorldRedraw;

    public GameUIUpdater(GameState state)
    {
        _state = state;
    }

    /// <summary>Bind all HUD label references.</summary>
    public void BindLabels(
        Label expLabel, Label goldLabel, Label levelLabel, Label nameLabel,
        Label onlineLabel, Label coordsLabel,
        Label armorLabel, Label helmLabel, Label shieldLabel, Label weaponLabel,
        Label fuerzaLabel, Label agilidadLabel, Label? repLabel, // repLabel parameter kept for API compatibility — unused
        Label fpsLabel, Label? macroStatusLabel,
        Control? btnCastiGM, StatBarOverlay? statBarOverlay)
    {
        _expLabel = expLabel;
        _goldLabel = goldLabel;
        _levelLabel = levelLabel;
        _nameLabel = nameLabel;
        _onlineLabel = onlineLabel;
        _coordsLabel = coordsLabel;
        _armorLabel = armorLabel;
        _helmLabel = helmLabel;
        _shieldLabel = shieldLabel;
        _weaponLabel = weaponLabel;
        _fuerzaLabel = fuerzaLabel;
        _agilidadLabel = agilidadLabel;
        _fpsLabel = fpsLabel;
        _macroStatusLabel = macroStatusLabel;
        _btnCastiGM = btnCastiGM;
        _statBarOverlay = statBarOverlay;
    }

    /// <summary>Set minimap and party panel references for party member sync.</summary>
    public void BindMinimap(MinimapPanel? minimapPanel, PartyPanel? partyPanel)
    {
        _minimapPanel = minimapPanel;
        _partyPanel = partyPanel;
    }

    /// <summary>Update all HUD labels and stat bars from current GameState.</summary>
    public void UpdateGameUI()
    {
        if (_statBarOverlay == null) return;

        // Push stat values to the overlay — it draws colored fill rects
        _statBarOverlay.SetStats(
            _state.MinHp, _state.MaxHp,
            _state.MinMana, _state.MaxMana,
            _state.MinSta, _state.MaxSta,
            _state.MinAgua, _state.MaxAgua,
            _state.MinHam, _state.MaxHam,
            _state.Exp, _state.ExpNext
        );

        var newExp = $"EXP: {_state.Exp}/{_state.ExpNext}";
        if (_expLabel!.Text != newExp) { _expLabel.Text = newExp; _cachedExp = newExp; }

        var newGold = _state.Gold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");
        if (_goldLabel!.Text != newGold) { _goldLabel.Text = newGold; _cachedGold = newGold; }

        var newLevel = $"{_state.Level}";
        if (_levelLabel!.Text != newLevel) { _levelLabel.Text = newLevel; _cachedLevel = newLevel; }

        var newName = _state.UserName;
        if (_nameLabel!.Text != newName) { _nameLabel.Text = newName; _cachedName = newName; }

        var newOnline = $"Onlines: {_state.OnlineCount}";
        if (_onlineLabel!.Text != newOnline) { _onlineLabel.Text = newOnline; _cachedOnline = newOnline; }

        // Zone name replaces map name when inside a zone
        string locationName = _state.CurrentZoneName.Length > 0 ? _state.CurrentZoneName : _state.MapName;
        var newCoords = $"{locationName}\n({_state.CurrentMap}, {_state.UserPosX}, {_state.UserPosY})";
        if (_coordsLabel!.Text != newCoords) { _coordsLabel.Text = newCoords; _cachedCoords = newCoords; }

        // GM button visibility
        if (_btnCastiGM != null)
        {
            bool newBtnCastiVisible = _state.Privileges >= 1;
            if (_cachedBtnCastiVisible != newBtnCastiVisible) { _btnCastiGM.Visible = newBtnCastiVisible; _cachedBtnCastiVisible = newBtnCastiVisible; }
        }

        // Combat stat labels
        var newArmor = $"Armadura: {_state.ArmourLabel}";
        if (_armorLabel!.Text != newArmor) { _armorLabel.Text = newArmor; _cachedArmor = newArmor; }

        var newHelm = $"Casco: {_state.HelmLabel}";
        if (_helmLabel!.Text != newHelm) { _helmLabel.Text = newHelm; _cachedHelm = newHelm; }

        var newShield = $"Escudo: {_state.ShieldLabel}";
        if (_shieldLabel!.Text != newShield) { _shieldLabel.Text = newShield; _cachedShield = newShield; }

        var newWeapon = $"Arma: {_state.WeaponLabel}";
        if (_weaponLabel!.Text != newWeapon) { _weaponLabel.Text = newWeapon; _cachedWeapon = newWeapon; }

        var newFuerza = $"Fuerza: {_state.Strength}";
        if (_fuerzaLabel!.Text != newFuerza) { _fuerzaLabel.Text = newFuerza; _cachedFuerza = newFuerza; }

        var newAgilidad = $"Agilidad: {_state.Agility}";
        if (_agilidadLabel!.Text != newAgilidad) { _agilidadLabel.Text = newAgilidad; _cachedAgilidad = newAgilidad; }

        // FPS
        var newFps = $"FPS: {Engine.GetFramesPerSecond()}";
        if (_fpsLabel!.Text != newFps) { _fpsLabel.Text = newFps; _cachedFps = newFps; }

        // Macro status indicator
        if (_macroStatusLabel != null)
        {
            bool workActive = _state.WorkMacro.Active;
            bool spellActive = _state.SpellMacro.Active;
            if (workActive || spellActive)
            {
                string newMacroText = workActive ? "MACRO" : "MACROSP";
                if (_cachedMacroText != newMacroText) { _macroStatusLabel.Text = newMacroText; _cachedMacroText = newMacroText; }
                if (!_cachedMacroVisible) { _macroStatusLabel.Visible = true; _cachedMacroVisible = true; }
            }
            else
            {
                if (_cachedMacroVisible) { _macroStatusLabel.Visible = false; _cachedMacroVisible = false; }
            }
        }

        // Update minimap party member names from PartyPanel
        if (_minimapPanel != null && _partyPanel != null)
        {
            _minimapPanel.PartyMemberNames.Clear();
            foreach (var m in _partyPanel.Members)
            {
                if (!string.IsNullOrEmpty(m.Name))
                    _minimapPanel.PartyMemberNames.Add(m.Name);
            }
        }
    }

    /// <summary>
    /// Move active arrows toward targets and remove on arrival.
    /// VB6: arrows fly from shooter to target tile, rendered as a GRH.
    /// </summary>
    public void UpdateArrowProjectiles(float delta)
    {
        if (_state.ActiveArrows.Count == 0) return;
        float pixelsPerSec = 320f; // ~10 tiles/sec at 32px/tile
        for (int i = _state.ActiveArrows.Count - 1; i >= 0; i--)
        {
            var a = _state.ActiveArrows[i];
            if (!a.Active) { _state.ActiveArrows.RemoveAt(i); continue; }

            a.LifetimeMs += delta * 1000f;
            if (a.LifetimeMs > 3000f) { _state.ActiveArrows.RemoveAt(i); continue; }

            // Update target position from live character data (target may be moving)
            if (_state.Characters.TryGetValue((short)a.TargetCharIndex, out var tgt))
            {
                a.TargetX = tgt.PosX * 32f + 16f;
                a.TargetY = tgt.PosY * 32f + 16f;
            }

            float dx = a.TargetX - a.X;
            float dy = a.TargetY - a.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 4f)
            {
                _state.ActiveArrows.RemoveAt(i);
                continue;
            }
            float move = pixelsPerSec * delta;
            if (move >= dist) { _state.ActiveArrows.RemoveAt(i); continue; }
            a.X += dx / dist * move;
            a.Y += dy / dist * move;
        }
        QueueWorldRedraw?.Invoke(); // ensure arrows are redrawn
    }
}

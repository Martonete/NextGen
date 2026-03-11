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
    private Label? _repLabel;
    private Label? _fpsLabel;
    private Label? _macroStatusLabel;
    private Control? _btnCastiGM;
    private StatBarOverlay? _statBarOverlay;
    private MinimapPanel? _minimapPanel;
    private PartyPanel? _partyPanel;

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
        Label fuerzaLabel, Label agilidadLabel, Label repLabel,
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
        _repLabel = repLabel;
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

        _expLabel!.Text = $"{_state.Exp}/{_state.ExpNext}";
        _goldLabel!.Text = _state.Gold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");
        _levelLabel!.Text = $"Nivel: {_state.Level}";
        _nameLabel!.Text = _state.UserName;
        _onlineLabel!.Text = $"{_state.OnlineCount}";
        // VB6: Coord.Caption = NombreMapa on first line, (Map, X, Y) on second
        _coordsLabel!.Text = $"{_state.MapName}\n({_state.CurrentMap}, {_state.UserPosX}, {_state.UserPosY})";

        // GM button visibility
        if (_btnCastiGM != null) _btnCastiGM.Visible = _state.Privileges >= 1;

        // Combat stat labels
        _armorLabel!.Text = _state.ArmourLabel;
        _helmLabel!.Text = _state.HelmLabel;
        _shieldLabel!.Text = _state.ShieldLabel;
        _weaponLabel!.Text = _state.WeaponLabel;
        _fuerzaLabel!.Text = $"{_state.Strength}";
        _agilidadLabel!.Text = $"{_state.Agility}";

        // Reputation: negative = red with "- " prefix, positive = white
        if (_state.Reputation < 0)
        {
            _repLabel!.Text = $"- {Math.Abs(_state.Reputation)}";
            _repLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0));
        }
        else
        {
            _repLabel!.Text = $"{_state.Reputation}";
            _repLabel.AddThemeColorOverride("font_color", Colors.White);
        }

        // FPS
        _fpsLabel!.Text = $"{Engine.GetFramesPerSecond()}";

        // Macro status indicator
        if (_macroStatusLabel != null)
        {
            bool workActive = _state.WorkMacro.Active;
            bool spellActive = _state.SpellMacro.Active;
            if (workActive || spellActive)
            {
                _macroStatusLabel.Text = workActive ? "MACRO" : "MACROSP";
                _macroStatusLabel.Visible = true;
            }
            else
            {
                _macroStatusLabel.Visible = false;
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

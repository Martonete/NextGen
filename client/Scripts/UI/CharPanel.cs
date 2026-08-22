using Godot;

namespace ArgentumNextgen.UI;

/// <summary>
/// Character sheet laid over the ornate frame lifted from the UI atlas.
///
/// Every field is a real node in CharPanel.tscn — open that scene in Godot and
/// drag the labels or bars wherever they look right. Nothing here hardcodes a
/// position, so edits in the editor survive without touching this file.
/// </summary>
public partial class CharPanel : Control
{
    /// <summary>Authoring size of the frame art; callers use it to centre the panel.</summary>
    public const int FrameW = 304;
    public const int FrameH = 184;

    /// <summary>Raised when the user pins or unpins the panel, so the caller can persist it.</summary>
    public event System.Action<bool>? LockedChanged;
    /// <summary>Raised after a drag, so the caller can persist the new position.</summary>
    public event System.Action<Vector2>? Moved;

    private Label? _nameLabel;
    private Label? _levelLabel;
    private Label? _armorLabel;
    private Label? _helmLabel;
    private Label? _shieldLabel;
    private Label? _weaponLabel;
    private TextureProgressBar? _expBar;
    private TextureProgressBar? _thirstBar;
    private TextureProgressBar? _hungerBar;
    private Button? _lockButton;

    private bool _isDragging;
    private Vector2 _dragOffset;

    /// <summary>While locked the panel ignores drags, so it cannot be nudged in combat.</summary>
    public bool Locked { get; private set; }

    public override void _Ready()
    {
        // Must receive clicks to be draggable; children keep MouseFilter=Ignore
        // so they never swallow the drag.
        MouseFilter = MouseFilterEnum.Stop;

        _nameLabel   = GetNodeOrNull<Label>("NameLabel");
        _levelLabel  = GetNodeOrNull<Label>("LevelLabel");
        _armorLabel  = GetNodeOrNull<Label>("ArmorLabel");
        _helmLabel   = GetNodeOrNull<Label>("HelmLabel");
        _shieldLabel = GetNodeOrNull<Label>("ShieldLabel");
        _weaponLabel = GetNodeOrNull<Label>("WeaponLabel");
        _expBar      = GetNodeOrNull<TextureProgressBar>("ExpBar");
        _thirstBar   = GetNodeOrNull<TextureProgressBar>("ThirstBar");
        _hungerBar   = GetNodeOrNull<TextureProgressBar>("HungerBar");

        // The bar art is only a few pixels tall. Nine-patch stretching leaves
        // those slivers invisible at the sizes the frame allows, so the fill is
        // stretched plainly instead.
        foreach (var bar in new[] { _expBar, _thirstBar, _hungerBar })
        {
            if (bar == null) continue;
            bar.NinePatchStretch = false;
            bar.FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight;
        }

        // Labels default to Stop and would eat the drag before it reaches the
        // panel. Only the lock button is meant to be clickable.
        foreach (var child in GetChildren())
        {
            if (child is Control c && child is not Button)
                c.MouseFilter = MouseFilterEnum.Ignore;
        }

        BuildLockButton();
        UpdateLockVisual();
    }

    /// <summary>
    /// Small pin in the top-right corner. Placed in code rather than the scene
    /// so it always sits on the frame's corner regardless of how the fields
    /// inside get rearranged in the editor.
    /// </summary>
    private void BuildLockButton()
    {
        _lockButton = new Button
        {
            Size = new Vector2(16, 16),
            Position = new Vector2(FrameW - 22, 6),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        _lockButton.AddThemeFontSizeOverride("font_size", 10);
        _lockButton.Pressed += () =>
        {
            Locked = !Locked;
            _isDragging = false;
            UpdateLockVisual();
            LockedChanged?.Invoke(Locked);
        };
        AddChild(_lockButton);
    }

    private void UpdateLockVisual()
    {
        if (_lockButton == null) return;
        _lockButton.Text = Locked ? "*" : "+";
        _lockButton.TooltipText = Locked
            ? "Panel fijado — clic para poder moverlo"
            : "Panel libre — clic para fijarlo";
        _lockButton.Modulate = Locked
            ? new Color(1f, 0.82f, 0.35f)
            : new Color(0.72f, 0.74f, 0.80f);
    }

    /// <summary>Restores a previously saved position and pin state.</summary>
    public void ApplyLayout(Vector2 position, bool locked)
    {
        if (position != Vector2.Zero) Position = position;
        Locked = locked;
        UpdateLockVisual();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Locked) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _isDragging = true;
                // Scale-aware: the panel is drawn scaled, so the grab offset has
                // to be converted to parent space or the panel jumps on grab.
                _dragOffset = mb.Position * Scale;
                MoveToFront();
            }
            else if (_isDragging)
            {
                _isDragging = false;
                Moved?.Invoke(Position);
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mm && _isDragging)
        {
            Position += mm.Position * Scale - _dragOffset;
            AcceptEvent();
        }
    }

    /// <summary>Equipment condition readouts, previously in the bottom bar.</summary>
    public void SetEquipment(string armour, string helm, string shield, string weapon)
    {
        if (_armorLabel != null)  _armorLabel.Text  = $"Armadura: {armour}";
        if (_helmLabel != null)   _helmLabel.Text   = $"Casco: {helm}";
        if (_shieldLabel != null) _shieldLabel.Text = $"Escudo: {shield}";
        if (_weaponLabel != null) _weaponLabel.Text = $"Arma: {weapon}";
    }

    public void SetCharInfo(string name, int level)
    {
        if (_nameLabel != null) _nameLabel.Text = name;
        if (_levelLabel != null) _levelLabel.Text = $"Nivel {level}";
    }

    public void SetExp(long current, long max)
    {
        if (_expBar == null) return;
        _expBar.Value = max > 0 ? Mathf.Clamp(current * 100.0 / max, 0, 100) : 0;
    }

    /// <summary>Thirst and hunger arrive as 0-100 percentages.</summary>
    public void SetVitals(int thirst, int hunger)
    {
        if (_thirstBar != null) _thirstBar.Value = Mathf.Clamp(thirst, 0, 100);
        if (_hungerBar != null) _hungerBar.Value = Mathf.Clamp(hunger, 0, 100);
    }
}

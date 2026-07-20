#nullable enable
using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// 2-step wizard for creating a new particle definition:
///   Step 1 — pick a starting point: a blank definition, or clone one of the
///            existing (real, motor-balanced) Particles.ini definitions.
///   Step 2 — pick an emission Shape and a Movement pattern; the wizard
///            translates those into the actual ParticleStreamDef fields
///            (X1/Y1/X2/Y2 for shape, VecX/Y + Gravity + Spin + XMove/YMove
///            for movement) as a ready-to-tweak starting point.
///
/// Produces a ParticleStreamDef in memory only — the caller (ParticlePalette)
/// is responsible for opening ParticleEditPopup on the result and, if the user
/// saves, committing it via ParticleEngine.AddDefinition.
/// </summary>
public partial class ParticleWizardPopup : Window
{
    public ParticleEngine? Engine;
    /// <summary>Called with the finished draft definition when the user clicks "Crear".</summary>
    public Action<ParticleStreamDef>? OnCreate;

    private VBoxContainer? _step1Box;
    private VBoxContainer? _step2Box;
    private OptionButton? _baseOption;
    private OptionButton? _shapeOption;
    private OptionButton? _moveOption;
    private SpinBox? _shapeSizeSpin;
    private Label? _shapeSizeLabel;

    private ParticleStreamDef _draft = new();

    public override void _Ready()
    {
        Title = "Nueva Partícula";
        Size = new Vector2I(380, 420);
        Exclusive = true;
        CloseRequested += () => QueueFree();

        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        bg.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        BuildStep1(root);
        BuildStep2(root);

        if (_step2Box != null) _step2Box.Visible = false;
    }

    // ── Step 1: base ──────────────────────────────────────────────────

    private void BuildStep1(VBoxContainer root)
    {
        _step1Box = new VBoxContainer();
        _step1Box.AddThemeConstantOverride("separation", 8);
        root.AddChild(_step1Box);

        _step1Box.AddChild(EditorTheme.SectionLabel("Paso 1 — Punto de partida"));
        _step1Box.AddChild(EditorTheme.MakeLabel(
            "Empezá desde una definición real (ya balanceada) o en blanco.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM));

        _baseOption = new OptionButton();
        _baseOption.AddItem("En blanco", 0);
        if (Engine != null)
        {
            for (int i = 1; i < Engine.Defs.Length; i++)
            {
                var def = Engine.Defs[i];
                if (def == null) continue;
                string name = string.IsNullOrWhiteSpace(def.Name) ? $"Partícula {i}" : def.Name;
                _baseOption.AddItem($"#{i} {name}", i);
            }
        }
        _baseOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _step1Box.AddChild(_baseOption);

        _step1Box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var nextBtn = EditorTheme.PrimaryButton("Siguiente →", GoToStep2);
        _step1Box.AddChild(nextBtn);
    }

    private void GoToStep2()
    {
        int selectedId = _baseOption?.GetSelectedId() ?? 0;
        _draft = (selectedId > 0 && Engine != null && selectedId < Engine.Defs.Length)
            ? Engine.Defs[selectedId].Clone()
            : BlankDefault();
        if (selectedId <= 0) _draft.Name = "Nueva Partícula";

        if (_step1Box != null) _step1Box.Visible = false;
        if (_step2Box != null) _step2Box.Visible = true;
    }

    private static ParticleStreamDef BlankDefault() => new()
    {
        Name = "Nueva Partícula",
        NumParticles = 20,
        LifeMin = 20,
        LifeMax = 40,
        Friction = 4,
        Speed = 0.5f,
        AlphaBlend = true,
        LifeCounter = -1,
        GrhList = Array.Empty<int>(),
        ColR1 = 255, ColG1 = 255, ColB1 = 255,
        ColR2 = 255, ColG2 = 255, ColB2 = 255,
        ColR3 = 255, ColG3 = 255, ColB3 = 255,
        ColR4 = 255, ColG4 = 255, ColB4 = 255,
    };

    // ── Step 2: shape + movement ─────────────────────────────────────

    private void BuildStep2(VBoxContainer root)
    {
        _step2Box = new VBoxContainer();
        _step2Box.AddThemeConstantOverride("separation", 8);
        root.AddChild(_step2Box);

        _step2Box.AddChild(EditorTheme.SectionLabel("Paso 2 — Forma y Movimiento"));

        _step2Box.AddChild(EditorTheme.MakeLabel("Forma de emisión:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _shapeOption = new OptionButton();
        _shapeOption.AddItem("Punto", 0);
        _shapeOption.AddItem("Círculo", 1);
        _shapeOption.AddItem("Rectángulo", 2);
        _shapeOption.AddItem("Línea", 3);
        _shapeOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _shapeOption.ItemSelected += _ => SyncShapeSizeVisibility();
        _step2Box.AddChild(_shapeOption);

        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 8);
        _shapeSizeLabel = EditorTheme.MakeLabel("Radio (px):", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        sizeRow.AddChild(_shapeSizeLabel);
        _shapeSizeSpin = EditorTheme.MakeSpinBox(0, 500, 5, 30);
        sizeRow.AddChild(_shapeSizeSpin);
        _step2Box.AddChild(sizeRow);
        SyncShapeSizeVisibility();

        _step2Box.AddChild(EditorTheme.MakeLabel("Patrón de movimiento:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _moveOption = new OptionButton();
        _moveOption.AddItem("Explosión radial", 0);
        _moveOption.AddItem("Chorro direccional", 1);
        _moveOption.AddItem("Caída con gravedad", 2);
        _moveOption.AddItem("Flotante con spin", 3);
        _moveOption.AddItem("Estático / Jitter", 4);
        _moveOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _step2Box.AddChild(_moveOption);

        _step2Box.AddChild(EditorTheme.MakeLabel(
            "Podés ajustar todos los valores en detalle después de crearla.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS));

        _step2Box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        var backBtn = EditorTheme.MakeButton("← Atrás", () =>
        {
            if (_step1Box != null) _step1Box.Visible = true;
            if (_step2Box != null) _step2Box.Visible = false;
        });
        btnRow.AddChild(backBtn);
        btnRow.AddChild(EditorTheme.Spacer());
        var createBtn = EditorTheme.PrimaryButton("Crear →", OnCreatePressed);
        btnRow.AddChild(createBtn);
        _step2Box.AddChild(btnRow);
    }

    private void SyncShapeSizeVisibility()
    {
        int shape = _shapeOption?.Selected ?? 0;
        if (_shapeSizeLabel == null || _shapeSizeSpin == null) return;
        bool isPoint = shape == 0;
        _shapeSizeLabel.Visible = !isPoint;
        _shapeSizeSpin.Visible = !isPoint;
        _shapeSizeLabel.Text = shape switch
        {
            1 => "Radio (px):",
            2 => "Ancho x Alto (px):",
            3 => "Largo (px):",
            _ => "Tamaño (px):",
        };
    }

    private void OnCreatePressed()
    {
        ApplyShape(_draft, _shapeOption?.Selected ?? 0, (float)(_shapeSizeSpin?.Value ?? 30));
        ApplyMovement(_draft, _moveOption?.Selected ?? 0);
        OnCreate?.Invoke(_draft);
        QueueFree();
    }

    /// <summary>
    /// Translate a shape choice into the X1/Y1/X2/Y2 spawn-area rectangle. The
    /// motor only supports a rectangular spawn area (independent RandRange on
    /// X and Y — see ParticleEngine.SpawnParticle), so "Círculo" is an honest
    /// approximation: a square area sized by radius, not a true circular mask.
    /// </summary>
    private static void ApplyShape(ParticleStreamDef d, int shape, float size)
    {
        switch (shape)
        {
            case 0: // Punto
                d.X1 = d.Y1 = d.X2 = d.Y2 = 0;
                break;
            case 1: // Círculo (radius → square spawn area)
                d.X1 = -size; d.X2 = size;
                d.Y1 = -size; d.Y2 = size;
                break;
            case 2: // Rectángulo (size = width, used for both axes as a square by default)
                d.X1 = -size / 2f; d.X2 = size / 2f;
                d.Y1 = -size / 2f; d.Y2 = size / 2f;
                break;
            case 3: // Línea horizontal
                d.X1 = -size / 2f; d.X2 = size / 2f;
                d.Y1 = 0; d.Y2 = 0;
                break;
        }
    }

    /// <summary>
    /// Translate a movement choice into VecX/Y, Gravity, Spin, XMove/YMove.
    /// Values are deliberately moderate — a solid starting point to fine-tune
    /// in the full editor, not a final tuned effect.
    /// </summary>
    private static void ApplyMovement(ParticleStreamDef d, int move)
    {
        // Reset movement-related fields so switching the wizard's choice never
        // leaves stale flags/values from the cloned base definition.
        d.Gravity = 0; d.GravStrength = 0; d.BounceStrength = 0;
        d.Spin = false; d.SpinSpeedL = 0; d.SpinSpeedH = 0;
        d.XMove = false; d.YMove = false;
        d.MoveX1 = d.MoveX2 = d.MoveY1 = d.MoveY2 = 0;

        switch (move)
        {
            case 0: // Explosión radial
                d.VecX1 = -30; d.VecX2 = 30;
                d.VecY1 = -30; d.VecY2 = 30;
                d.Friction = 4;
                break;
            case 1: // Chorro direccional (upward jet by default; Angle/vectors are
                    // editable afterwards to point it any direction)
                d.VecX1 = -6; d.VecX2 = 6;
                d.VecY1 = -40; d.VecY2 = -20;
                d.Friction = 5;
                break;
            case 2: // Caída con gravedad
                d.VecX1 = -5; d.VecX2 = 5;
                d.VecY1 = 0; d.VecY2 = 5;
                d.Gravity = 1;
                d.GravStrength = 2;
                d.BounceStrength = -5;
                d.Friction = 6;
                break;
            case 3: // Flotante con spin
                d.VecX1 = -4; d.VecX2 = 4;
                d.VecY1 = -4; d.VecY2 = 4;
                d.Spin = true;
                d.SpinSpeedL = 10; d.SpinSpeedH = 40;
                d.Friction = 8;
                break;
            case 4: // Estático / Jitter en el lugar
                d.VecX1 = d.VecX2 = d.VecY1 = d.VecY2 = 0;
                d.XMove = true; d.YMove = true;
                d.MoveX1 = -2; d.MoveX2 = 2;
                d.MoveY1 = -2; d.MoveY2 = 2;
                d.Friction = 3;
                break;
        }
    }
}

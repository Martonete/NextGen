#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Full particle definition editor: every ParticleStreamDef field, grouped into
/// collapsible sections, with a big live preview that reacts to every change
/// immediately — WITHOUT resetting the running simulation (values are pushed onto
/// the live def in place; only a change to particle count re-sizes the pool). The
/// preview centers a reference character and lets you drag the emission rectangle
/// (X1/Y1/X2/Y2) directly over it with the mouse.
///
/// Works on an in-memory draft (a clone of the target); OnSave copies the draft's
/// fields back onto the real def only when confirmed, so Cancelar is a true no-op.
/// </summary>
public partial class ParticleEditPopup : Window
{
    public ParticleStreamDef? Target;
    public int TargetIndex;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public int[]? NpcBodyGrhs;
    public Action? OnSaved;
    /// <summary>Clone the current (unsaved) draft as a new definition; host returns the new index.</summary>
    public Func<ParticleStreamDef, int>? OnDuplicateDraft;

    private ParticleStreamDef _draft = new();
    private bool _dirty;
    private bool _forceClose;

    private LineEdit? _nameEdit;
    private LabeledSlider? _numParticles;
    private LabeledSlider? _x1, _y1, _x2, _y2, _angle;
    private LabeledSlider? _vecX1, _vecX2, _vecY1, _vecY2;
    private LabeledSlider? _friction, _speed;
    private CheckBox? _gravityCheck;
    private LabeledSlider? _gravStrength, _bounceStrength;
    private CheckBox? _spinCheck;
    private LabeledSlider? _spinSpeedL, _spinSpeedH;
    private CheckBox? _xMoveCheck, _yMoveCheck;
    private LabeledSlider? _moveX1, _moveX2, _moveY1, _moveY2;
    private LabeledSlider? _life1, _life2, _lifeCounter;
    private CheckBox? _alphaBlendCheck;
    private GrhMultiPicker? _grhPicker;
    private ColorSetEditor? _color1, _color2, _color3, _color4;
    private ColorRect? _finalColorSwatch;

    // Extended motor (opt-in, "more than VB6") controls.
    private CheckBox? _fadeAlphaCheck, _rotateVisualCheck, _scaleOverLifeCheck, _colorGradientCheck;
    private LabeledSlider? _scaleStart, _scaleEnd;
    private CheckBox? _turbulenceCheck, _attractCheck;
    private LabeledSlider? _turbulenceStrength, _attractX, _attractY, _attractStrength;

    private ParticlePreviewControl? _preview;

    public override void _Ready()
    {
        if (Target == null) { QueueFree(); return; }
        _draft = Target.Clone();

        Title = $"Editar Partícula #{TargetIndex}: {Target.Name}";
        Size = new Vector2I(860, 640);
        Exclusive = true;
        CloseRequested += RequestClose;

        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var outerMargin = new MarginContainer();
        outerMargin.AddThemeConstantOverride("margin_left", 12);
        outerMargin.AddThemeConstantOverride("margin_right", 12);
        outerMargin.AddThemeConstantOverride("margin_top", 12);
        outerMargin.AddThemeConstantOverride("margin_bottom", 12);
        bg.AddChild(outerMargin);

        // Two columns: preview (left, fixed) | options (right, scrollable).
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 14);
        outerMargin.AddChild(columns);

        // ── Left column: preview + toggles + action buttons (no scroll) ──
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 6);
        leftCol.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.AddChild(leftCol);

        _preview = new ParticlePreviewControl { Grhs = Grhs, Textures = Textures, NpcBodyGrhs = NpcBodyGrhs };
        _preview.SetDef(_draft);
        // When the emission rect is dragged in the preview, reflect it back in the sliders.
        _preview.OnEmissionRectDragged = (x1, y1, x2, y2) =>
        {
            _x1?.SetValueNoSignal(x1); _y1?.SetValueNoSignal(y1);
            _x2?.SetValueNoSignal(x2); _y2?.SetValueNoSignal(y2);
            _draft.X1 = x1; _draft.Y1 = y1; _draft.X2 = x2; _draft.Y2 = y2;
            MarkDirty();
        };
        leftCol.AddChild(_preview);

        // Row 1: existing toggles
        var toggleRow1 = new HBoxContainer(); toggleRow1.AddThemeConstantOverride("separation", 10);
        var charToggle = MiniCheck(toggleRow1, "Personaje", true);
        charToggle.Toggled += on => _preview?.SetShowCharacter(on);
        var rectToggle = MiniCheck(toggleRow1, "Área c/mouse", true);
        rectToggle.Toggled += on => _preview?.SetShowEmissionRect(on);
        leftCol.AddChild(toggleRow1);

        // Row 2: new toggles — particles behind character, animate character
        var toggleRow2 = new HBoxContainer(); toggleRow2.AddThemeConstantOverride("separation", 10);
        var behindToggle = MiniCheck(toggleRow2, "Partículas detrás", false);
        behindToggle.TooltipText = "Dibuja las partículas DETRÁS del personaje (solo vista del editor, no afecta el juego)";
        behindToggle.Toggled += on => _preview?.SetParticlesBehindCharacter(on);
        var animToggle = MiniCheck(toggleRow2, "Animar personaje", false);
        animToggle.Toggled += on => _preview?.SetAnimateCharacter(on);
        leftCol.AddChild(toggleRow2);

        // Row 3: scene background + reference character pickers
        var pickerRow = new HBoxContainer(); pickerRow.AddThemeConstantOverride("separation", 6);
        var bgOption = new OptionButton();
        bgOption.AddItem("Fondo: Negro", 0);
        bgOption.AddItem("Fondo: Pasto", 1);
        bgOption.AddItem("Fondo: Noche", 2);
        bgOption.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_XS);
        bgOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bgOption.ItemSelected += idx => _preview?.SetBackgroundMode((int)idx);
        pickerRow.AddChild(bgOption);

        var bodyOption = new OptionButton();
        bodyOption.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_XS);
        bodyOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (NpcBodyGrhs != null)
        {
            for (int i = 1; i < NpcBodyGrhs.Length && bodyOption.ItemCount < 200; i++)
                if (NpcBodyGrhs[i] > 0) bodyOption.AddItem($"Body #{i}", i);
        }
        // Select body #1 by default if present.
        for (int i = 0; i < bodyOption.ItemCount; i++)
            if (bodyOption.GetItemId(i) == 1) { bodyOption.Selected = i; break; }
        bodyOption.ItemSelected += idx => _preview?.SetReferenceBodyIndex(bodyOption.GetItemId((int)idx));
        pickerRow.AddChild(bodyOption);
        leftCol.AddChild(pickerRow);

        // Row 4: playback controls — pause/play, speed, restart burst, live count
        var playRow = new HBoxContainer(); playRow.AddThemeConstantOverride("separation", 4);
        var pauseBtn = EditorTheme.MakeButton("⏸");
        pauseBtn.ToggleMode = true;
        pauseBtn.CustomMinimumSize = new Vector2(32, 0);
        pauseBtn.Toggled += on => { pauseBtn.Text = on ? "▶" : "⏸"; _preview?.SetPaused(on); };
        playRow.AddChild(pauseBtn);
        var burstBtn = EditorTheme.MakeButton("↻ Burst");
        burstBtn.TooltipText = "Reinicia todas las partículas de golpe (ver el arranque de una explosión)";
        burstBtn.Pressed += () => _preview?.RestartBurst();
        playRow.AddChild(burstBtn);
        var countLabel = EditorTheme.MakeLabel("0 vivas", EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS);
        countLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        countLabel.HorizontalAlignment = HorizontalAlignment.Right;
        playRow.AddChild(countLabel);
        leftCol.AddChild(playRow);
        if (_preview != null) _preview.OnLiveCountChanged = n => countLabel.Text = $"{n} vivas";

        var speedRow = new HBoxContainer(); speedRow.AddThemeConstantOverride("separation", 6);
        speedRow.AddChild(EditorTheme.MakeLabel("Velocidad:", EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS));
        var speedSlider = new HSlider { MinValue = 0.25, MaxValue = 4, Step = 0.25, Value = 1, CustomMinimumSize = new Vector2(0, 16) };
        speedSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var speedLabel = EditorTheme.MakeLabel("1.0x", EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS);
        speedLabel.CustomMinimumSize = new Vector2(36, 0);
        speedSlider.ValueChanged += v => { _preview?.SetSimSpeed((float)v); speedLabel.Text = $"{v:0.00}x"; };
        speedRow.AddChild(speedSlider);
        speedRow.AddChild(speedLabel);
        leftCol.AddChild(speedRow);

        _preview?.SetShowCharacter(true);
        _preview?.SetShowEmissionRect(true);

        // Action buttons pinned at the bottom of the left column.
        leftCol.AddChild(EditorTheme.Spacer());
        var btnCol = new VBoxContainer();
        btnCol.AddThemeConstantOverride("separation", 6);
        var saveBtn = EditorTheme.PrimaryButton("Guardar", OnSave);
        saveBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btnCol.AddChild(saveBtn);
        var lowerBtns = new HBoxContainer();
        lowerBtns.AddThemeConstantOverride("separation", 6);
        var dupBtn = EditorTheme.MakeButton("Duplicar", OnDuplicate);
        dupBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lowerBtns.AddChild(dupBtn);
        var cancelBtn = EditorTheme.MakeButton("Cancelar", RequestClose);
        cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lowerBtns.AddChild(cancelBtn);
        btnCol.AddChild(lowerBtns);
        leftCol.AddChild(btnCol);

        // ── Right column: all options, scrollable ──
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(440, 0);
        columns.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(vbox);

        // === Identity ===
        AddLabeledEdit(vbox, "Nombre:", _draft.Name, out _nameEdit);
        if (_nameEdit != null) _nameEdit.TextChanged += t => _draft.Name = t;
        _numParticles = AddSlider(vbox, "Cant. partículas", 1, 500, _draft.NumParticles, 1, v =>
        {
            _draft.NumParticles = (int)v;
            _preview?.ResizePool((int)v); // pool size is the one change that needs a resize, not a full reset
        });

        // === Collapsible sections ===
        AddSection(vbox, "Área de aparición (px, relativo al personaje)", spawn =>
        {
            _x1 = AddSlider(spawn, "X1", -300, 300, _draft.X1, 1, v => { _draft.X1 = (float)v; SyncEmissionRect(); });
            _y1 = AddSlider(spawn, "Y1", -300, 300, _draft.Y1, 1, v => { _draft.Y1 = (float)v; SyncEmissionRect(); });
            _x2 = AddSlider(spawn, "X2", -300, 300, _draft.X2, 1, v => { _draft.X2 = (float)v; SyncEmissionRect(); });
            _y2 = AddSlider(spawn, "Y2", -300, 300, _draft.Y2, 1, v => { _draft.Y2 = (float)v; SyncEmissionRect(); });
            _angle = AddSlider(spawn, "Ángulo inicial", -360, 360, _draft.Angle, 1, v => _draft.Angle = (float)v);
        }, onReset: () =>
        {
            if (Target == null) return;
            _draft.X1 = Target.X1; _draft.Y1 = Target.Y1; _draft.X2 = Target.X2; _draft.Y2 = Target.Y2; _draft.Angle = Target.Angle;
            _x1?.SetValueNoSignal(_draft.X1); _y1?.SetValueNoSignal(_draft.Y1);
            _x2?.SetValueNoSignal(_draft.X2); _y2?.SetValueNoSignal(_draft.Y2);
            _angle?.SetValueNoSignal(_draft.Angle);
            SyncEmissionRect();
        });

        AddSection(vbox, "Física — velocidad inicial (px/tick)", phys =>
        {
            _vecX1 = AddSlider(phys, "VelX min", -200, 200, _draft.VecX1, 1, v => _draft.VecX1 = (float)v);
            _vecX2 = AddSlider(phys, "VelX max", -200, 200, _draft.VecX2, 1, v => _draft.VecX2 = (float)v);
            _vecY1 = AddSlider(phys, "VelY min", -200, 200, _draft.VecY1, 1, v => _draft.VecY1 = (float)v);
            _vecY2 = AddSlider(phys, "VelY max", -200, 200, _draft.VecY2, 1, v => _draft.VecY2 = (float)v);
            _friction = AddSlider(phys, "Fricción (divisor)", 1, 50, _draft.Friction <= 0 ? 1 : _draft.Friction, 1, v => _draft.Friction = (float)v);
            _speed = AddSlider(phys, "Speed (umbral tick)", 0.05, 5, _draft.Speed <= 0 ? 0.5f : _draft.Speed, 0.05, v => _draft.Speed = (float)v);
        }, onReset: () =>
        {
            if (Target == null) return;
            _draft.VecX1 = Target.VecX1; _draft.VecX2 = Target.VecX2; _draft.VecY1 = Target.VecY1; _draft.VecY2 = Target.VecY2;
            _draft.Friction = Target.Friction; _draft.Speed = Target.Speed;
            _vecX1?.SetValueNoSignal(_draft.VecX1); _vecX2?.SetValueNoSignal(_draft.VecX2);
            _vecY1?.SetValueNoSignal(_draft.VecY1); _vecY2?.SetValueNoSignal(_draft.VecY2);
            _friction?.SetValueNoSignal(_draft.Friction <= 0 ? 1 : _draft.Friction);
            _speed?.SetValueNoSignal(_draft.Speed <= 0 ? 0.5f : _draft.Speed);
        });

        AddSection(vbox, "Gravedad y rebote", grav =>
        {
            _gravityCheck = AddCheck(grav, "Gravedad activa", _draft.Gravity > 0);
            _gravityCheck.Toggled += on => _draft.Gravity = on ? 1f : 0f;
            _gravStrength = AddSlider(grav, "Fuerza", -20, 20, _draft.GravStrength, 0.1, v => _draft.GravStrength = (float)v);
            _bounceStrength = AddSlider(grav, "Rebote (VelY al piso)", -50, 50, _draft.BounceStrength, 0.5, v => _draft.BounceStrength = (float)v);
        }, onReset: () =>
        {
            if (Target == null) return;
            _draft.Gravity = Target.Gravity; _draft.GravStrength = Target.GravStrength; _draft.BounceStrength = Target.BounceStrength;
            if (_gravityCheck != null) _gravityCheck.ButtonPressed = _draft.Gravity > 0;
            _gravStrength?.SetValueNoSignal(_draft.GravStrength);
            _bounceStrength?.SetValueNoSignal(_draft.BounceStrength);
        });

        AddSection(vbox, "Rotación (spin)", sp =>
        {
            _spinCheck = AddCheck(sp, "Spin activo", _draft.Spin);
            _spinCheck.Toggled += on => _draft.Spin = on;
            _spinSpeedL = AddSlider(sp, "Vel. angular min", -200, 200, _draft.SpinSpeedL, 0.5, v => _draft.SpinSpeedL = (float)v);
            _spinSpeedH = AddSlider(sp, "Vel. angular max", -200, 200, _draft.SpinSpeedH, 0.5, v => _draft.SpinSpeedH = (float)v);
        }, startCollapsed: true, onReset: () =>
        {
            if (Target == null) return;
            _draft.Spin = Target.Spin; _draft.SpinSpeedL = Target.SpinSpeedL; _draft.SpinSpeedH = Target.SpinSpeedH;
            if (_spinCheck != null) _spinCheck.ButtonPressed = _draft.Spin;
            _spinSpeedL?.SetValueNoSignal(_draft.SpinSpeedL);
            _spinSpeedH?.SetValueNoSignal(_draft.SpinSpeedH);
        });

        AddSection(vbox, "Movimiento forzado (reemplaza velocidad cada tick)", mv =>
        {
            var checkRow = new HBoxContainer(); checkRow.AddThemeConstantOverride("separation", 12);
            _xMoveCheck = AddCheckInline(checkRow, "XMove", _draft.XMove);
            _xMoveCheck.Toggled += on => _draft.XMove = on;
            _yMoveCheck = AddCheckInline(checkRow, "YMove", _draft.YMove);
            _yMoveCheck.Toggled += on => _draft.YMove = on;
            mv.AddChild(checkRow);
            _moveX1 = AddSlider(mv, "MoveX min", -50, 50, _draft.MoveX1, 0.1, v => _draft.MoveX1 = (float)v);
            _moveX2 = AddSlider(mv, "MoveX max", -50, 50, _draft.MoveX2, 0.1, v => _draft.MoveX2 = (float)v);
            _moveY1 = AddSlider(mv, "MoveY min", -50, 50, _draft.MoveY1, 0.1, v => _draft.MoveY1 = (float)v);
            _moveY2 = AddSlider(mv, "MoveY max", -50, 50, _draft.MoveY2, 0.1, v => _draft.MoveY2 = (float)v);
        }, startCollapsed: true, onReset: () =>
        {
            if (Target == null) return;
            _draft.XMove = Target.XMove; _draft.YMove = Target.YMove;
            _draft.MoveX1 = Target.MoveX1; _draft.MoveX2 = Target.MoveX2; _draft.MoveY1 = Target.MoveY1; _draft.MoveY2 = Target.MoveY2;
            if (_xMoveCheck != null) _xMoveCheck.ButtonPressed = _draft.XMove;
            if (_yMoveCheck != null) _yMoveCheck.ButtonPressed = _draft.YMove;
            _moveX1?.SetValueNoSignal(_draft.MoveX1); _moveX2?.SetValueNoSignal(_draft.MoveX2);
            _moveY1?.SetValueNoSignal(_draft.MoveY1); _moveY2?.SetValueNoSignal(_draft.MoveY2);
        });

        AddSection(vbox, "Vida", life =>
        {
            _life1 = AddSlider(life, "Vida min (ticks)", 1, 500, _draft.LifeMin <= 0 ? 20 : _draft.LifeMin, 1, v => _draft.LifeMin = (float)v);
            _life2 = AddSlider(life, "Vida max (ticks)", 1, 500, _draft.LifeMax <= 0 ? 40 : _draft.LifeMax, 1, v => _draft.LifeMax = (float)v);
            _lifeCounter = AddSlider(life, "Vida del stream (-1 = infinito)", -1, 1000, _draft.LifeCounter, 1, v => _draft.LifeCounter = (int)v);
            _alphaBlendCheck = AddCheck(life, "Blend aditivo (AlphaBlend)", _draft.AlphaBlend);
            _alphaBlendCheck.Toggled += on => _draft.AlphaBlend = on;
        }, onReset: () =>
        {
            if (Target == null) return;
            _draft.LifeMin = Target.LifeMin; _draft.LifeMax = Target.LifeMax; _draft.LifeCounter = Target.LifeCounter;
            _draft.AlphaBlend = Target.AlphaBlend;
            _life1?.SetValueNoSignal(_draft.LifeMin <= 0 ? 20 : _draft.LifeMin);
            _life2?.SetValueNoSignal(_draft.LifeMax <= 0 ? 40 : _draft.LifeMax);
            _lifeCounter?.SetValueNoSignal(_draft.LifeCounter);
            if (_alphaBlendCheck != null) _alphaBlendCheck.ButtonPressed = _draft.AlphaBlend;
        });

        AddSection(vbox, "Sprites", spr =>
        {
            _grhPicker = new GrhMultiPicker { Grhs = Grhs, Textures = Textures };
            spr.AddChild(_grhPicker);
            _grhPicker.SetChosen(_draft.GrhList);
            _grhPicker.OnChanged += () => _draft.GrhList = _grhPicker.GetChosen();
        }, onReset: () =>
        {
            if (Target == null) return;
            _draft.GrhList = (int[])Target.GrhList.Clone();
            _grhPicker?.SetChosen(_draft.GrhList);
        });

        AddSection(vbox, "Colores", col =>
        {
            col.AddChild(EditorTheme.MakeLabel(
                "El color final es el promedio de los 4 sets. Los swatches muestran el color real en juego.",
                EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS));

            _color1 = new ColorSetEditor(); _color1.Init("Set 1:", _draft.ColR1, _draft.ColG1, _draft.ColB1);
            _color2 = new ColorSetEditor(); _color2.Init("Set 2:", _draft.ColR2, _draft.ColG2, _draft.ColB2);
            _color3 = new ColorSetEditor(); _color3.Init("Set 3:", _draft.ColR3, _draft.ColG3, _draft.ColB3);
            _color4 = new ColorSetEditor(); _color4.Init("Set 4:", _draft.ColR4, _draft.ColG4, _draft.ColB4);
            col.AddChild(_color1); col.AddChild(_color2); col.AddChild(_color3); col.AddChild(_color4);
            _color1.OnChanged += () => { _draft.ColR1 = _color1.R; _draft.ColG1 = _color1.G; _draft.ColB1 = _color1.B; OnColorEdited(); };
            _color2.OnChanged += () => { _draft.ColR2 = _color2.R; _draft.ColG2 = _color2.G; _draft.ColB2 = _color2.B; OnColorEdited(); };
            _color3.OnChanged += () => { _draft.ColR3 = _color3.R; _draft.ColG3 = _color3.G; _draft.ColB3 = _color3.B; OnColorEdited(); };
            _color4.OnChanged += () => { _draft.ColR4 = _color4.R; _draft.ColG4 = _color4.G; _draft.ColB4 = _color4.B; OnColorEdited(); };

            // "Set 1 → todos": most real particles use identical/near-identical sets.
            var copyBtn = EditorTheme.MakeButton("Set 1 → todos", () =>
            {
                _color2?.SetRawNoSignal(_draft.ColR1, _draft.ColG1, _draft.ColB1);
                _color3?.SetRawNoSignal(_draft.ColR1, _draft.ColG1, _draft.ColB1);
                _color4?.SetRawNoSignal(_draft.ColR1, _draft.ColG1, _draft.ColB1);
                _draft.ColR2 = _draft.ColR3 = _draft.ColR4 = _draft.ColR1;
                _draft.ColG2 = _draft.ColG3 = _draft.ColG4 = _draft.ColG1;
                _draft.ColB2 = _draft.ColB3 = _draft.ColB4 = _draft.ColB1;
                OnColorEdited();
            });
            col.AddChild(copyBtn);

            // Final averaged color the player will actually see.
            var finalRow = new HBoxContainer();
            finalRow.AddThemeConstantOverride("separation", 8);
            finalRow.AddChild(EditorTheme.MakeLabel("Color final:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
            _finalColorSwatch = new ColorRect { CustomMinimumSize = new Vector2(0, 26) };
            _finalColorSwatch.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            finalRow.AddChild(_finalColorSwatch);
            col.AddChild(finalRow);
            UpdateFinalColorSwatch();
        }, onReset: () =>
        {
            if (Target == null) return;
            _draft.ColR1 = Target.ColR1; _draft.ColG1 = Target.ColG1; _draft.ColB1 = Target.ColB1;
            _draft.ColR2 = Target.ColR2; _draft.ColG2 = Target.ColG2; _draft.ColB2 = Target.ColB2;
            _draft.ColR3 = Target.ColR3; _draft.ColG3 = Target.ColG3; _draft.ColB3 = Target.ColB3;
            _draft.ColR4 = Target.ColR4; _draft.ColG4 = Target.ColG4; _draft.ColB4 = Target.ColB4;
            _color1?.SetRawNoSignal(_draft.ColR1, _draft.ColG1, _draft.ColB1);
            _color2?.SetRawNoSignal(_draft.ColR2, _draft.ColG2, _draft.ColB2);
            _color3?.SetRawNoSignal(_draft.ColR3, _draft.ColG3, _draft.ColB3);
            _color4?.SetRawNoSignal(_draft.ColR4, _draft.ColG4, _draft.ColB4);
            OnColorEdited();
        });

        AddSection(vbox, "✨ Motor extendido (más allá de VB6)", ext =>
        {
            ext.AddChild(EditorTheme.MakeLabel(
                "Todo acá es opcional. Desactivado = comportamiento clásico idéntico al original.",
                EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS));

            _fadeAlphaCheck = AddCheck(ext, "Fade de alpha (se desvanece en vez de desaparecer de golpe)", _draft.FadeAlpha);
            _fadeAlphaCheck.Toggled += on => _draft.FadeAlpha = on;

            _rotateVisualCheck = AddCheck(ext, "Rotación visual (gira con el spin simulado)", _draft.RotateVisual);
            _rotateVisualCheck.Toggled += on => _draft.RotateVisual = on;

            ext.AddChild(EditorTheme.MakeHSeparator());
            _scaleOverLifeCheck = AddCheck(ext, "Escalar con la vida (crece/encoge)", _draft.ScaleOverLife);
            _scaleOverLifeCheck.Toggled += on => _draft.ScaleOverLife = on;
            _scaleStart = AddSlider(ext, "Escala al nacer", 0, 5, _draft.ResizeX <= 0 ? 1 : _draft.ResizeX, 0.05, v => _draft.ResizeX = (float)v);
            _scaleEnd = AddSlider(ext, "Escala al morir", 0, 5, _draft.ResizeY <= 0 ? 1 : _draft.ResizeY, 0.05, v => _draft.ResizeY = (float)v);

            ext.AddChild(EditorTheme.MakeHSeparator());
            _colorGradientCheck = AddCheck(ext, "Gradiente de color real (interpola Set1→Set4 con la vida, en vez de promediar)", _draft.ColorGradient);
            _colorGradientCheck.Toggled += on => _draft.ColorGradient = on;
        }, startCollapsed: true, onReset: () =>
        {
            if (Target == null) return;
            _draft.FadeAlpha = Target.FadeAlpha;
            _draft.RotateVisual = Target.RotateVisual;
            _draft.ScaleOverLife = Target.ScaleOverLife;
            _draft.ResizeX = Target.ResizeX; _draft.ResizeY = Target.ResizeY;
            _draft.ColorGradient = Target.ColorGradient;
            if (_fadeAlphaCheck != null) _fadeAlphaCheck.ButtonPressed = _draft.FadeAlpha;
            if (_rotateVisualCheck != null) _rotateVisualCheck.ButtonPressed = _draft.RotateVisual;
            if (_scaleOverLifeCheck != null) _scaleOverLifeCheck.ButtonPressed = _draft.ScaleOverLife;
            _scaleStart?.SetValueNoSignal(_draft.ResizeX <= 0 ? 1 : _draft.ResizeX);
            _scaleEnd?.SetValueNoSignal(_draft.ResizeY <= 0 ? 1 : _draft.ResizeY);
            if (_colorGradientCheck != null) _colorGradientCheck.ButtonPressed = _draft.ColorGradient;
        });

        AddSection(vbox, "🌪 Efectos avanzados (turbulencia y vórtices)", adv =>
        {
            adv.AddChild(EditorTheme.MakeLabel(
                "También opcional. Movimiento orgánico y atracción/repulsión hacia un punto.",
                EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS));

            _turbulenceCheck = AddCheck(adv, "Turbulencia (humo/niebla con deriva orgánica)", _draft.Turbulence);
            _turbulenceCheck.Toggled += on => _draft.Turbulence = on;
            _turbulenceStrength = AddSlider(adv, "Fuerza de turbulencia", 0, 100, _draft.TurbulenceStrength, 1, v => _draft.TurbulenceStrength = (float)v);

            adv.AddChild(EditorTheme.MakeHSeparator());
            _attractCheck = AddCheck(adv, "Atracción a un punto (vórtice / explosión)", _draft.AttractToPoint);
            _attractCheck.Toggled += on => _draft.AttractToPoint = on;
            _attractX = AddSlider(adv, "Punto X", -300, 300, _draft.AttractX, 1, v => _draft.AttractX = (float)v);
            _attractY = AddSlider(adv, "Punto Y", -300, 300, _draft.AttractY, 1, v => _draft.AttractY = (float)v);
            _attractStrength = AddSlider(adv, "Fuerza (+atrae / -repele)", -50, 50, _draft.AttractStrength, 0.5, v => _draft.AttractStrength = (float)v);
        }, startCollapsed: true, onReset: () =>
        {
            if (Target == null) return;
            _draft.Turbulence = Target.Turbulence;
            _draft.TurbulenceStrength = Target.TurbulenceStrength;
            _draft.AttractToPoint = Target.AttractToPoint;
            _draft.AttractX = Target.AttractX; _draft.AttractY = Target.AttractY;
            _draft.AttractStrength = Target.AttractStrength;
            if (_turbulenceCheck != null) _turbulenceCheck.ButtonPressed = _draft.Turbulence;
            _turbulenceStrength?.SetValueNoSignal(_draft.TurbulenceStrength);
            if (_attractCheck != null) _attractCheck.ButtonPressed = _draft.AttractToPoint;
            _attractX?.SetValueNoSignal(_draft.AttractX);
            _attractY?.SetValueNoSignal(_draft.AttractY);
            _attractStrength?.SetValueNoSignal(_draft.AttractStrength);
        });

        SyncEmissionRect();
    }

    /// <summary>
    /// Called whenever any color set changes: refresh the final-color swatch and
    /// recolor the live preview particles so the change is visible immediately
    /// (otherwise their color stays cached until each dies and respawns).
    /// </summary>
    private void OnColorEdited()
    {
        UpdateFinalColorSwatch();
        _preview?.RecolorLive();
    }

    /// <summary>Final color = 4-set average with the BGR quirk, matching SpawnParticle.</summary>
    private void UpdateFinalColorSwatch()
    {
        if (_finalColorSwatch == null) return;
        float r = (_draft.ColB1 + _draft.ColB2 + _draft.ColB3 + _draft.ColB4) / 4f / 255f;
        float g = (_draft.ColG1 + _draft.ColG2 + _draft.ColG3 + _draft.ColG4) / 4f / 255f;
        float b = (_draft.ColR1 + _draft.ColR2 + _draft.ColR3 + _draft.ColR4) / 4f / 255f;
        _finalColorSwatch.Color = new Color(r, g, b);
    }

    /// <summary>Push the current X1/Y1/X2/Y2 to the preview's draggable rect overlay.</summary>
    private void SyncEmissionRect() => _preview?.SyncEmissionRect();

    private void OnDuplicate()
    {
        if (OnDuplicateDraft == null) return;
        var copy = _draft.Clone();
        copy.Name = $"{_draft.Name} (copia)";
        int newIndex = OnDuplicateDraft(copy);
        if (newIndex > 0) Title = $"Editar Partícula #{TargetIndex}: {_draft.Name}  (creada copia #{newIndex})";
    }

    private void OnSave()
    {
        if (Target == null) return;

        _draft.Name = _nameEdit?.Text ?? _draft.Name;
        _draft.GrhCount = _draft.GrhList.Length;

        // Commit the draft field-by-field onto the real array slot the caller
        // passed in (keeps that slot's reference identity for the map streams).
        CopyInto(_draft, Target);

        _dirty = false;
        OnSaved?.Invoke();
        QueueFree();
    }

    private void MarkDirty() => _dirty = true;

    private void RequestClose()
    {
        if (!_dirty || _forceClose)
        {
            QueueFree();
            return;
        }

        var confirm = new ConfirmationDialog
        {
            Title = "Cambios sin guardar",
            DialogText = "Descartar los cambios de esta partícula?",
            OkButtonText = "Descartar",
            CancelButtonText = "Seguir editando"
        };
        AddChild(confirm);
        confirm.Confirmed += () =>
        {
            _forceClose = true;
            QueueFree();
        };
        confirm.Canceled += confirm.QueueFree;
        confirm.CloseRequested += confirm.QueueFree;
        confirm.PopupCentered();
    }

    private static void CopyInto(ParticleStreamDef src, ParticleStreamDef dst)
    {
        dst.Name = src.Name; dst.NumParticles = src.NumParticles;
        dst.X1 = src.X1; dst.Y1 = src.Y1; dst.X2 = src.X2; dst.Y2 = src.Y2; dst.Angle = src.Angle;
        dst.VecX1 = src.VecX1; dst.VecX2 = src.VecX2; dst.VecY1 = src.VecY1; dst.VecY2 = src.VecY2;
        dst.Friction = src.Friction; dst.Speed = src.Speed;
        dst.Gravity = src.Gravity; dst.GravStrength = src.GravStrength; dst.BounceStrength = src.BounceStrength;
        dst.Spin = src.Spin; dst.SpinSpeedL = src.SpinSpeedL; dst.SpinSpeedH = src.SpinSpeedH;
        dst.XMove = src.XMove; dst.YMove = src.YMove;
        dst.MoveX1 = src.MoveX1; dst.MoveX2 = src.MoveX2; dst.MoveY1 = src.MoveY1; dst.MoveY2 = src.MoveY2;
        dst.LifeMin = src.LifeMin; dst.LifeMax = src.LifeMax; dst.LifeCounter = src.LifeCounter;
        dst.AlphaBlend = src.AlphaBlend;
        dst.GrhList = (int[])src.GrhList.Clone(); dst.GrhCount = src.GrhList.Length;
        dst.ColR1 = src.ColR1; dst.ColG1 = src.ColG1; dst.ColB1 = src.ColB1;
        dst.ColR2 = src.ColR2; dst.ColG2 = src.ColG2; dst.ColB2 = src.ColB2;
        dst.ColR3 = src.ColR3; dst.ColG3 = src.ColG3; dst.ColB3 = src.ColB3;
        dst.ColR4 = src.ColR4; dst.ColG4 = src.ColG4; dst.ColB4 = src.ColB4;
        dst.Resize = src.Resize; dst.ResizeX = src.ResizeX; dst.ResizeY = src.ResizeY;
        dst.FadeAlpha = src.FadeAlpha; dst.RotateVisual = src.RotateVisual;
        dst.ScaleOverLife = src.ScaleOverLife; dst.ColorGradient = src.ColorGradient;
        dst.Turbulence = src.Turbulence; dst.TurbulenceStrength = src.TurbulenceStrength;
        dst.AttractToPoint = src.AttractToPoint;
        dst.AttractX = src.AttractX; dst.AttractY = src.AttractY; dst.AttractStrength = src.AttractStrength;
    }

    // === UI helpers ===

    private void AddLabeledEdit(VBoxContainer parent, string label, string value, out LineEdit edit)
    {
        parent.AddChild(EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        edit = new LineEdit { Text = value };
        edit.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        edit.CustomMinimumSize = new Vector2(0, 28);
        edit.TextChanged += _ => MarkDirty();
        parent.AddChild(edit);
    }

    private LabeledSlider AddSlider(VBoxContainer parent, string label, double min, double max, double value, double step, Action<double> onChanged)
    {
        var s = new LabeledSlider();
        s.Init(label, min, max, value, step);
        s.OnValueChanged = v =>
        {
            onChanged(v);
            MarkDirty();
        };
        parent.AddChild(s);
        return s;
    }

    private CheckBox AddCheck(VBoxContainer parent, string label, bool value)
    {
        var chk = new CheckBox { Text = label, ButtonPressed = value };
        chk.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        chk.Toggled += _ => MarkDirty();
        parent.AddChild(chk);
        return chk;
    }

    private CheckBox AddCheckInline(HBoxContainer parent, string label, bool value)
    {
        var chk = new CheckBox { Text = label, ButtonPressed = value };
        chk.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        chk.Toggled += _ => MarkDirty();
        parent.AddChild(chk);
        return chk;
    }

    /// <summary>Compact checkbox for the preview toggle rows (smaller font than AddCheckInline).</summary>
    private static CheckBox MiniCheck(HBoxContainer parent, string label, bool value)
    {
        var chk = new CheckBox { Text = label, ButtonPressed = value };
        chk.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_XS);
        parent.AddChild(chk);
        return chk;
    }

    /// <summary>
    /// A collapsible section: a clickable header that toggles a content VBox.
    /// `build` populates the content once; the header expands/collapses it.
    /// When `onReset` is given, a small "↺" button appears next to the header that
    /// reverts just this section's fields to how they were when the popup opened
    /// (Target, not a blank template — a granular undo per group of settings).
    /// </summary>
    private static void AddSection(VBoxContainer parent, string title, Action<VBoxContainer> build,
        bool startCollapsed = false, Action? onReset = null)
    {
        parent.AddChild(EditorTheme.MakeHSeparator());

        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);
        parent.AddChild(headerRow);

        var header = new Button { ToggleMode = true, ButtonPressed = !startCollapsed };
        header.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        header.AddThemeStyleboxOverride("normal", EditorTheme.FlatBox(EditorTheme.BG_SECTION, 3, 6, 3));
        header.AddThemeStyleboxOverride("hover", EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 3, 6, 3));
        header.AddThemeStyleboxOverride("pressed", EditorTheme.FlatBox(EditorTheme.BG_SECTION, 3, 6, 3));
        header.AddThemeColorOverride("font_color", EditorTheme.TEXT_MUTED);
        header.Alignment = HorizontalAlignment.Left;
        header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(header);

        if (onReset != null)
        {
            var resetBtn = new Button { Text = "↺", TooltipText = $"Revertir \"{title}\" a los valores originales" };
            resetBtn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
            resetBtn.CustomMinimumSize = new Vector2(28, 0);
            resetBtn.AddThemeStyleboxOverride("normal", EditorTheme.FlatBox(EditorTheme.BG_SECTION, 3, 4, 3));
            resetBtn.AddThemeStyleboxOverride("hover", EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 3, 4, 3));
            resetBtn.Pressed += onReset;
            headerRow.AddChild(resetBtn);
        }

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 4);
        content.Visible = !startCollapsed;
        build(content);
        parent.AddChild(content);

        void UpdateHeader() => header.Text = (content.Visible ? "▼  " : "▶  ") + title.ToUpper();
        UpdateHeader();
        header.Toggled += on => { content.Visible = on; UpdateHeader(); };
    }
}

/// <summary>
/// Big live-preview control. Simulates one ParticleStreamDef via
/// ParticleEngine.UpdateSingleStream and draws it additively over an optional
/// reference character. Field edits are applied to the SAME def instance the
/// simulation reads, so changes take effect live without restarting the stream;
/// only a particle-count change resizes the pool. Also renders a draggable
/// emission rectangle (X1/Y1/X2/Y2) the user can move/resize with the mouse.
/// </summary>
public partial class ParticlePreviewControl : Control
{
    private const int PreviewWidth = 340;
    private const int PreviewHeight = 240;
    private const float CenterX = PreviewWidth / 2f;
    private const float CenterY = PreviewHeight / 2f;

    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public int[]? NpcBodyGrhs;
    /// <summary>Fires while the emission rect is dragged/resized: (x1,y1,x2,y2) in def space.</summary>
    public Action<float, float, float, float>? OnEmissionRectDragged;
    /// <summary>Fires every frame with the current live particle count, for the UI counter.</summary>
    public Action<int>? OnLiveCountChanged;

    private ParticleStreamDef? _def;
    private EditorParticleStream? _stream;
    private double _animTime;
    private BackgroundLayer? _bgLayer;
    private CharacterLayer? _charLayer;
    private PreviewOverlay? _overlay;
    private EmissionRectLayer? _rectLayer;
    private bool _showCharacter;
    private bool _showRect;
    private bool _animateCharacter;
    private bool _particlesBehindCharacter;

    // Playback controls
    private bool _paused;
    private float _simSpeed = 1f;

    // Background preset: 0=Negro, 1=Pasto, 2=Noche
    private int _backgroundMode;
    // Grass tile GRH — 1316 primary, with fallbacks tried in order at draw time.
    private static readonly int[] GrassGrhCandidates = { 1316, 1315, 1317 };

    // Reference character body index (editable via selector; defaults to body #1).
    private int _referenceBodyIndex = 1;

    public override void _Ready()
    {
        // Fixed size, NOT ExpandFill: the character anchor and emission-rect math
        // map def coords against the constant CenterX/CenterY (PreviewWidth/2,
        // PreviewHeight/2). Letting the control stretch would desync that mapping.
        CustomMinimumSize = new Vector2(PreviewWidth, PreviewHeight);
        Size = new Vector2(PreviewWidth, PreviewHeight);
        ClipContents = true;

        var bgPanel = new Panel();
        bgPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bgPanel.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_DARK, 3, 0, 0));
        bgPanel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bgPanel);

        // Tiled scene background (grass/night), drawn above the flat panel but
        // below the character — "Negro" mode just draws nothing here.
        _bgLayer = new BackgroundLayer { Owner2 = this };
        _bgLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bgLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_bgLayer);

        _charLayer = new CharacterLayer { Owner2 = this };
        _charLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _charLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_charLayer);

        _overlay = new PreviewOverlay { Owner2 = this };
        _overlay.Material = MapViewport.AdditiveBlendMaterial();
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_overlay);

        // The rect layer sits on top and DOES receive mouse input for dragging.
        _rectLayer = new EmissionRectLayer { Owner2 = this };
        _rectLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_rectLayer);
    }

    /// <summary>Bind the live def the simulation reads; edits to it show immediately.</summary>
    public void SetDef(ParticleStreamDef def)
    {
        _def = def;
        ResizePool(def.NumParticles);
        SyncEmissionRect();
    }

    /// <summary>Resize the particle pool without disturbing still-living particles.</summary>
    public void ResizePool(int count)
    {
        count = Math.Max(count, 0);
        var particles = new EditorParticle[count];
        var old = _stream?.Particles;
        for (int i = 0; i < count; i++)
            particles[i] = (old != null && i < old.Length) ? old[i] : new EditorParticle();
        _stream = new EditorParticleStream { Particles = particles, Active = true };
    }

    public void SetShowCharacter(bool show) { _showCharacter = show; _charLayer?.QueueRedraw(); }
    public void SetShowEmissionRect(bool show) { _showRect = show; _rectLayer?.QueueRedraw(); }
    public void SyncEmissionRect() => _rectLayer?.QueueRedraw();
    public void SetAnimateCharacter(bool animate) { _animateCharacter = animate; _charLayer?.QueueRedraw(); }
    public void SetBackgroundMode(int mode) { _backgroundMode = mode; _bgLayer?.QueueRedraw(); }
    public void SetReferenceBodyIndex(int index) { _referenceBodyIndex = index; _charLayer?.QueueRedraw(); }

    /// <summary>Toggle particle draw order relative to the character (view-only, editor preview).</summary>
    public void SetParticlesBehindCharacter(bool behind)
    {
        if (_particlesBehindCharacter == behind || _charLayer == null || _overlay == null) return;
        _particlesBehindCharacter = behind;
        // MoveChild reorders siblings; higher index draws later (on top).
        if (behind)
            MoveChild(_charLayer, _overlay.GetIndex() + 1); // char above particles
        else
            MoveChild(_overlay, _charLayer.GetIndex() + 1); // particles above char (default)
    }

    public void SetPaused(bool paused) => _paused = paused;
    public void SetSimSpeed(float speed) => _simSpeed = Math.Max(speed, 0f);

    /// <summary>Kill every particle so the whole pool respawns together next tick — see the burst at once.</summary>
    public void RestartBurst()
    {
        if (_stream == null) return;
        foreach (var p in _stream.Particles) p.Alive = false;
    }

    /// <summary>Recolor live particles now so a color edit shows without waiting for respawn.</summary>
    public void RecolorLive()
    {
        if (_def != null && _stream != null)
            ParticleEngine.RecolorLiveParticles(_stream, _def);
    }

    public override void _Process(double delta)
    {
        if (_paused) return;

        // Shared clock for both GRH frame animation (particles + character) and physics.
        _animTime += delta * 1000.0 * _simSpeed;
        if (_animateCharacter) _charLayer?.QueueRedraw();

        if (_def == null || _stream == null) return;
        ParticleEngine.UpdateSingleStream(_stream, _def, (float)(delta * 1000.0) * _simSpeed);
        _overlay?.QueueRedraw();

        if (OnLiveCountChanged != null)
        {
            int alive = 0;
            foreach (var p in _stream.Particles) if (p.Alive) alive++;
            OnLiveCountChanged(alive);
        }
    }

    // ── Coordinate mapping: def space (origin at character anchor) ↔ preview px ──
    // Character stands feet-on-anchor at (CenterX, CenterY).
    private Vector2 DefToPreview(float dx, float dy) => new(CenterX + dx, CenterY + dy);
    private Vector2 PreviewToDef(Vector2 p) => new(p.X - CenterX, p.Y - CenterY);

    internal void DrawParticlesOn(CanvasItem canvas)
    {
        if (_stream == null || _def == null || Grhs == null || Textures == null) return;
        foreach (var p in _stream.Particles)
        {
            if (!p.Alive || p.GrhIndex <= 0 || p.GrhIndex >= Grhs.Length) continue;
            var grh = Grhs[p.GrhIndex];
            if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
            {
                int frame = grh.Speed > 0 ? (int)(_animTime * grh.NumFrames / grh.Speed) % grh.NumFrames : 0;
                int frameIdx = grh.Frames[frame];
                if (frameIdx <= 0 || frameIdx >= Grhs.Length) continue;
                grh = Grhs[frameIdx];
            }
            if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) continue;
            var texture = Textures.GetTexture(grh.FileNum);
            if (texture == null) continue;
            int cropW = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
            int cropH = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
            if (grh.SX < 0 || grh.SY < 0 || cropW <= 0 || cropH <= 0) continue;
            var srcRect = new Rect2(grh.SX, grh.SY, cropW, cropH);
            var pos = DefToPreview(p.X, p.Y);

            // Extended motor: mirror the client's WorldRenderer collection exactly
            // (fade/rotate/scale), so the editor preview never lies about how the
            // definition will actually look in the game.
            float alpha = _def.FadeAlpha && p.MaxLife > 0
                ? p.Alpha * Math.Clamp(p.Life / p.MaxLife, 0f, 1f)
                : p.Alpha;
            var color = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, alpha);
            float angle = _def.RotateVisual ? Mathf.DegToRad(p.Angle) : 0f;
            float scale = (!_def.ScaleOverLife || p.MaxLife <= 0) ? 1f
                : _def.ResizeX + (_def.ResizeY - _def.ResizeX) * Math.Clamp(1f - p.Life / p.MaxLife, 0f, 1f);

            if (angle != 0f || scale != 1f)
            {
                var center = new Vector2(Mathf.Round(pos.X), Mathf.Round(pos.Y));
                canvas.DrawSetTransform(center, angle, new Vector2(scale, scale));
                canvas.DrawTextureRectRegion(texture, new Rect2(-cropW / 2f, -cropH / 2f, cropW, cropH), srcRect, color);
                canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                continue;
            }

            var destRect = new Rect2(pos.X - grh.PixelWidth / 2f, pos.Y - grh.PixelHeight / 2f, cropW, cropH);
            canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
        }
    }

    internal void DrawCharacterOn(CanvasItem canvas)
    {
        if (!_showCharacter || Grhs == null || Textures == null || NpcBodyGrhs == null) return;
        if (_referenceBodyIndex <= 0 || _referenceBodyIndex >= NpcBodyGrhs.Length) return;
        int bodyGrhId = NpcBodyGrhs[_referenceBodyIndex];
        if (bodyGrhId <= 0 || bodyGrhId >= Grhs.Length) return;
        var grh = Grhs[bodyGrhId];

        // Walk-south is itself an animated GRH (its Frames[] is the full walk cycle) —
        // resolve the current frame the same way DrawParticlesOn already does, so
        // "Animar personaje" makes it actually walk in place instead of a static pose.
        if (_animateCharacter && grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int walkFrame = grh.Speed > 0 ? (int)(_animTime * grh.NumFrames / grh.Speed) % grh.NumFrames : 0;
            int walkFrameIdx = grh.Frames[walkFrame];
            if (walkFrameIdx > 0 && walkFrameIdx < Grhs.Length) grh = Grhs[walkFrameIdx];
        }

        if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) return;
        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;
        int cropW = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
        int cropH = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
        if (grh.SX < 0 || grh.SY < 0 || cropW <= 0 || cropH <= 0) return;
        var srcRect = new Rect2(grh.SX, grh.SY, cropW, cropH);
        // Feet at anchor (CenterX, CenterY).
        var destRect = new Rect2(CenterX - cropW / 2f, CenterY - cropH, cropW, cropH);
        canvas.DrawTextureRectRegion(texture, destRect, srcRect, Colors.White);
    }

    /// <summary>
    /// Tile the scene background (grass/night preset) beneath the character. Uses the
    /// same GetTexture+DrawTextureRectRegion pattern the rest of the control already
    /// uses for GRH sprites — no new drawing technique. Falls back silently (leaves
    /// the flat dark panel showing through) if none of the candidate GRH IDs resolve.
    /// </summary>
    internal void DrawBackgroundOn(CanvasItem canvas)
    {
        if (_backgroundMode == 0 || Grhs == null || Textures == null) return; // 0 = Negro, nothing to draw

        int grhId = 0;
        foreach (var candidate in GrassGrhCandidates)
        {
            if (candidate > 0 && candidate < Grhs.Length && Grhs[candidate].IsSingleTile) { grhId = candidate; break; }
        }
        if (grhId == 0) return;

        var grh = Grhs[grhId];
        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;
        int cropW = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
        int cropH = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
        if (grh.SX < 0 || grh.SY < 0 || cropW <= 0 || cropH <= 0) return;
        var srcRect = new Rect2(grh.SX, grh.SY, cropW, cropH);

        // "Noche" reuses the grass tile but darkened, instead of needing a second GRH.
        var tint = _backgroundMode == 2 ? new Color(0.28f, 0.28f, 0.34f) : Colors.White;

        for (float y = 0; y < PreviewHeight; y += cropH)
            for (float x = 0; x < PreviewWidth; x += cropW)
                canvas.DrawTextureRectRegion(texture, new Rect2(x, y, cropW, cropH), srcRect, tint);
    }

    // ── Draggable emission rectangle ──────────────────────────────────

    internal bool ShowRect => _showRect && _def != null;

    internal Rect2 EmissionRectPreview()
    {
        if (_def == null) return default;
        var a = DefToPreview(Math.Min(_def.X1, _def.X2), Math.Min(_def.Y1, _def.Y2));
        var b = DefToPreview(Math.Max(_def.X1, _def.X2), Math.Max(_def.Y1, _def.Y2));
        return new Rect2(a, b - a);
    }

    internal void ApplyEmissionRectPreview(Rect2 previewRect)
    {
        if (_def == null) return;
        var tl = PreviewToDef(previewRect.Position);
        var br = PreviewToDef(previewRect.Position + previewRect.Size);
        _def.X1 = Mathf.Round(tl.X); _def.Y1 = Mathf.Round(tl.Y);
        _def.X2 = Mathf.Round(br.X); _def.Y2 = Mathf.Round(br.Y);
        OnEmissionRectDragged?.Invoke(_def.X1, _def.Y1, _def.X2, _def.Y2);
        _rectLayer?.QueueRedraw();
    }

    private sealed partial class PreviewOverlay : Control
    {
        public ParticlePreviewControl? Owner2;
        public override void _Draw() => Owner2?.DrawParticlesOn(this);
    }

    private sealed partial class BackgroundLayer : Control
    {
        public ParticlePreviewControl? Owner2;
        public override void _Draw() => Owner2?.DrawBackgroundOn(this);
    }

    private sealed partial class CharacterLayer : Control
    {
        public ParticlePreviewControl? Owner2;
        public override void _Draw() => Owner2?.DrawCharacterOn(this);
    }

    /// <summary>Interactive overlay: draws + drag/resizes the emission rectangle.</summary>
    private sealed partial class EmissionRectLayer : Control
    {
        public ParticlePreviewControl? Owner2;

        private const float HandleSize = 8f;
        private enum DragMode { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }
        private DragMode _drag = DragMode.None;
        private Vector2 _dragStart;
        private Rect2 _rectAtDragStart;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Pass;

        public override void _Draw()
        {
            if (Owner2 == null || !Owner2.ShowRect) return;
            var r = Owner2.EmissionRectPreview();
            var col = new Color(0.3f, 1f, 0.8f, 0.9f);
            DrawRect(r, new Color(0.3f, 1f, 0.8f, 0.12f), filled: true);
            DrawRect(r, col, filled: false, width: 1.5f);
            foreach (var h in Handles(r))
                DrawRect(h, col, filled: true);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (Owner2 == null || !Owner2.ShowRect) return;

            if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    var r = Owner2.EmissionRectPreview();
                    _drag = HitTest(r, mb.Position);
                    if (_drag != DragMode.None)
                    {
                        _dragStart = mb.Position;
                        _rectAtDragStart = r;
                        AcceptEvent();
                    }
                }
                else
                {
                    _drag = DragMode.None;
                }
            }
            else if (@event is InputEventMouseMotion mm && _drag != DragMode.None)
            {
                var delta = mm.Position - _dragStart;
                var r = _rectAtDragStart;
                switch (_drag)
                {
                    case DragMode.Move: r.Position += delta; break;
                    case DragMode.TopLeft: r = FromCorners(r.Position + delta, r.End); break;
                    case DragMode.TopRight: r = FromCorners(new Vector2(r.Position.X, r.Position.Y + delta.Y), new Vector2(r.End.X + delta.X, r.End.Y)); break;
                    case DragMode.BottomLeft: r = FromCorners(new Vector2(r.Position.X + delta.X, r.Position.Y), new Vector2(r.End.X, r.End.Y + delta.Y)); break;
                    case DragMode.BottomRight: r = FromCorners(r.Position, r.End + delta); break;
                }
                Owner2.ApplyEmissionRectPreview(r);
                AcceptEvent();
            }
        }

        private static Rect2 FromCorners(Vector2 a, Vector2 b)
        {
            var pos = new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
            var end = new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            return new Rect2(pos, end - pos);
        }

        private IEnumerable<Rect2> Handles(Rect2 r)
        {
            float h = HandleSize;
            yield return new Rect2(r.Position - new Vector2(h / 2, h / 2), new Vector2(h, h));
            yield return new Rect2(new Vector2(r.End.X, r.Position.Y) - new Vector2(h / 2, h / 2), new Vector2(h, h));
            yield return new Rect2(new Vector2(r.Position.X, r.End.Y) - new Vector2(h / 2, h / 2), new Vector2(h, h));
            yield return new Rect2(r.End - new Vector2(h / 2, h / 2), new Vector2(h, h));
        }

        private DragMode HitTest(Rect2 r, Vector2 p)
        {
            float h = HandleSize;
            if (r.Position.DistanceTo(p) <= h) return DragMode.TopLeft;
            if (new Vector2(r.End.X, r.Position.Y).DistanceTo(p) <= h) return DragMode.TopRight;
            if (new Vector2(r.Position.X, r.End.Y).DistanceTo(p) <= h) return DragMode.BottomLeft;
            if (r.End.DistanceTo(p) <= h) return DragMode.BottomRight;
            if (r.HasPoint(p)) return DragMode.Move;
            return DragMode.None;
        }
    }
}

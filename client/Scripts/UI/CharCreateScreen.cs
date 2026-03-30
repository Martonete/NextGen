using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// Character creation + selection + deletion UI. Extracted from Main.cs.
/// Styled with RpgTheme.
/// </summary>
public class CharCreateScreen
{
    private Control? _charCreatePanel;
    private LineEdit? _charCreateNameInput;
    private BoxContainer? _raceToggle;
    private BoxContainer? _genderToggle;
    private GridContainer? _classToggle;
    private BoxContainer? _factionToggle;
    private Label? _charCreateHeadLabel;
    private Node2D? _charCreateHeadPreview;
    private Label? _charCreateError;
    private TextureButton? _charCreateCreateBtn;
    private TextureButton? _charSelectCreateBtn;
    private TextureButton? _charSelectDeleteBtn;

    // Delete character confirmation dialog
    private Control? _deleteConfirmDialog;
    private Label? _deleteConfirmLabel;
    private LineEdit? _deleteConfirmInput;
    private int _deleteConfirmCode;

    private readonly GameState _state;
    private readonly GameData _gameData;

    public Control? Panel => _charCreatePanel;

    // Callbacks
    public Action? OnCreateCharConfirm;
    public Action? OnBack;
    public Action? OnCharSelectCreate;
    public Action? OnDeleteCharRequest;

    /// <summary>Access to the error label for external error messages.</summary>
    public Label? ErrorLabel => _charCreateError;
    /// <summary>Access to create button for enable/disable from outside.</summary>
    public TextureButton? CreateButton => _charCreateCreateBtn;

    private static readonly string[] RaceNames = { "Humano", "Elfo", "Elfo Oscuro", "Enano", "Gnomo" };
    private static readonly string[] GenderNames = { "Hombre", "Mujer" };
    private static readonly string[] ClassNames = { "Mago", "Clerigo", "Guerrero", "Asesino", "Ladron", "Bardo", "Druida", "Bandido", "Paladin", "Cazador", "Trabajador", "Pirata" };
    private static readonly string[] FactionNames = { "Armada Real", "Fuerzas del Caos" };

    public CharCreateScreen(GameState state, GameData gameData)
    {
        _state = state;
        _gameData = gameData;
    }

    /// <summary>Head ranges per race and gender from VB6 DameOpciones.</summary>
    public static (int min, int max) GetHeadRange(int race, int gender)
    {
        return (race, gender) switch
        {
            (1, 1) => (1, 30),
            (1, 2) => (70, 76),
            (2, 1) => (101, 113),
            (2, 2) => (170, 176),
            (3, 1) => (202, 209),
            (3, 2) => (270, 280),
            (4, 1) => (301, 305),
            (4, 2) => (370, 373),
            (5, 1) => (401, 406),
            (5, 2) => (470, 474),
            _ => (1, 30),
        };
    }

    public void CreatePanel(Node parent)
    {
        _charCreatePanel = new Control();
        _charCreatePanel.Size = new Vector2(580, 560);
        _charCreatePanel.CustomMinimumSize = new Vector2(580, 560);
        _charCreatePanel.Visible = false;
        _charCreatePanel.ClipContents = true;
        _charCreatePanel.MouseFilter = Control.MouseFilterEnum.Stop;
        float fs = RpgBaseForm.FormScale;
        _charCreatePanel.Scale = new Vector2(fs, fs);

        // V2 background: big_bar stretched
        var bg = new TextureRect();
        bg.Texture = RpgTheme.GetTex("big_bar.png");
        bg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bg.StretchMode = TextureRect.StretchModeEnum.Scale;
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _charCreatePanel.AddChild(bg);
        RpgTheme.FillParent(bg);

        // V2 title bar
        var titleBg = RpgTheme.CreateNinePatch("name_frame_mid_ready.png", new Vector4(30, 10, 30, 10));
        _charCreatePanel.AddChild(titleBg);
        titleBg.AnchorLeft = 0f; titleBg.AnchorRight = 1f;
        titleBg.AnchorTop = 0f;  titleBg.AnchorBottom = 0f;
        titleBg.OffsetLeft = 10; titleBg.OffsetTop = 5;
        titleBg.OffsetRight = -10; titleBg.OffsetBottom = 48;

        var titleLabel = RpgTheme.CreateTitleLabel("Crear Personaje", 18);
        titleBg.AddChild(titleLabel);
        RpgTheme.FillParent(titleLabel);
        titleLabel.OffsetTop = 4; titleLabel.OffsetBottom = -4;

        // Content area with V2 margins
        var marginC = new MarginContainer();
        marginC.AddThemeConstantOverride("margin_top", 54);
        marginC.AddThemeConstantOverride("margin_left", RpgTheme.FormMarginLeft);
        marginC.AddThemeConstantOverride("margin_right", RpgTheme.FormMarginRight);
        marginC.AddThemeConstantOverride("margin_bottom", RpgTheme.FormMarginBottom);
        marginC.MouseFilter = Control.MouseFilterEnum.Ignore;
        _charCreatePanel.AddChild(marginC);
        RpgTheme.FillParent(marginC);

        // ScrollContainer so buttons aren't clipped when content exceeds panel height
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        marginC.AddChild(scroll);

        var outerVbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        outerVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(outerVbox);

        // Title
        var title = RpgTheme.CreateTitleLabel("Crear Personaje", 16);
        outerVbox.AddChild(title);

        // Two-column body: left (main fields) + right (gender + head)
        var columnsRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        columnsRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columnsRow.Alignment = BoxContainer.AlignmentMode.Begin;
        outerVbox.AddChild(columnsRow);

        // ── Left column ─────────────────────────────────────────────────
        var leftCol = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columnsRow.AddChild(leftCol);

        // Name input
        leftCol.AddChild(RpgTheme.CreateInfoLabel("Nombre:", 11));
        _charCreateNameInput = RpgTheme.CreateRpgInput("4-15 caracteres");
        _charCreateNameInput.MaxLength = 15;
        leftCol.AddChild(_charCreateNameInput);

        // Race selector
        leftCol.AddChild(RpgTheme.CreateInfoLabel("Raza:", 11));
        _raceToggle = RpgTheme.CreateRpgToggleGroup(RaceNames, 0, OnRaceSelected);
        leftCol.AddChild(_raceToggle);

        // Class selector (grid: 4 columns)
        leftCol.AddChild(RpgTheme.CreateInfoLabel("Clase:", 11));
        _classToggle = RpgTheme.CreateRpgToggleGrid(ClassNames, 4, 0, OnClassSelected);
        leftCol.AddChild(_classToggle);

        // Faction selector
        leftCol.AddChild(RpgTheme.CreateInfoLabel("Faccion:", 11));
        _factionToggle = RpgTheme.CreateRpgToggleGroup(FactionNames, 0, OnFactionSelected);
        leftCol.AddChild(_factionToggle);

        // ── Right column ─────────────────────────────────────────────────
        var rightCol = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        rightCol.CustomMinimumSize = new Vector2(160, 0);
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        columnsRow.AddChild(rightCol);

        // Gender selector
        rightCol.AddChild(RpgTheme.CreateInfoLabel("Genero:", 11));
        _genderToggle = RpgTheme.CreateRpgToggleGroup(GenderNames, 0, OnGenderSelected);
        rightCol.AddChild(_genderToggle);

        // Head selector — arrows + centered preview
        rightCol.AddChild(RpgTheme.CreateInfoLabel("Cabeza:", 11));

        // Arrow row: [◄] [preview] [►]  — centered, symmetric
        var headRow = RpgTheme.CreateRow(0);
        headRow.Alignment = BoxContainer.AlignmentMode.Center;
        rightCol.AddChild(headRow);

        var headLeftBtn = RpgTheme.CreateMiniButton("Mini_arrow_left2.png", "Mini_arrow_left2_t.png", new Vector2(32, 32));
        headLeftBtn.Pressed += OnHeadPrev;
        headRow.AddChild(headLeftBtn);

        var headPreviewContainer = new SubViewportContainer();
        headPreviewContainer.CustomMinimumSize = new Vector2(64, 64);
        headPreviewContainer.Size = new Vector2(64, 64);
        headPreviewContainer.Stretch = true;
        headPreviewContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        headRow.AddChild(headPreviewContainer);

        var headViewport = new SubViewport();
        headViewport.Size = new Vector2I(64, 64);
        headViewport.TransparentBg = true;
        headViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        headPreviewContainer.AddChild(headViewport);

        _charCreateHeadPreview = new Node2D();
        _charCreateHeadPreview.Draw += DrawCharCreateHead;
        headViewport.AddChild(_charCreateHeadPreview);

        var headRightBtn = RpgTheme.CreateMiniButton("Mini_arrow_right2.png", "Mini_arrow_right2_t.png", new Vector2(32, 32));
        headRightBtn.Pressed += OnHeadNext;
        headRow.AddChild(headRightBtn);

        // Head index label — centered below arrows, not in the row
        _charCreateHeadLabel = RpgTheme.CreateInfoLabel("1", 11);
        _charCreateHeadLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _charCreateHeadLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.AddChild(_charCreateHeadLabel);

        // ── Bottom: error + buttons (full width, under columns) ──────────
        _charCreateError = new Label();
        _charCreateError.Text = "";
        _charCreateError.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _charCreateError.AddThemeFontSizeOverride("font_size", 11);
        _charCreateError.HorizontalAlignment = HorizontalAlignment.Center;
        _charCreateError.AutowrapMode = TextServer.AutowrapMode.Word;
        outerVbox.AddChild(_charCreateError);

        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        outerVbox.AddChild(btnRow);

        _charCreateCreateBtn = RpgTheme.CreateRpgButton("Crear", false, 14);
        _charCreateCreateBtn.CustomMinimumSize = new Vector2(100, 36);
        _charCreateCreateBtn.Pressed += OnCharCreateConfirm;
        btnRow.AddChild(_charCreateCreateBtn);

        var backBtn = RpgTheme.CreateRpgButton("Volver", false, 14);
        backBtn.CustomMinimumSize = new Vector2(100, 36);
        backBtn.Pressed += () => OnBack?.Invoke();
        btnRow.AddChild(backBtn);

        parent.AddChild(_charCreatePanel);
    }

    /// <summary>
    /// Add "Crear Personaje" and "Borrar Personaje" buttons to the CharSelect VBox.
    /// </summary>
    public void SetupCharSelectButtons(ItemList charList, Label noticeLabel)
    {
        var charSelectVBox = charList.GetParent();

        _charSelectCreateBtn = RpgTheme.CreateRpgButton("Crear Personaje", true, 14);
        _charSelectCreateBtn.CustomMinimumSize = new Vector2(0, 36);
        _charSelectCreateBtn.Pressed += () => OnCharSelectCreate?.Invoke();
        charSelectVBox.AddChild(_charSelectCreateBtn);

        _charSelectDeleteBtn = RpgTheme.CreateRpgButton("Borrar Personaje", true, 14);
        _charSelectDeleteBtn.CustomMinimumSize = new Vector2(0, 36);
        _charSelectDeleteBtn.Pressed += () => OnDeleteCharRequest?.Invoke();
        charSelectVBox.AddChild(_charSelectDeleteBtn);
    }

    public void CreateDeleteConfirmDialog(Node parent)
    {
        // --- Delete confirm dialog with NinePatch frame ---
        _deleteConfirmDialog = new Control();
        _deleteConfirmDialog.Size = new Vector2(320, 200);
        _deleteConfirmDialog.CustomMinimumSize = new Vector2(320, 200);
        _deleteConfirmDialog.Visible = false;
        _deleteConfirmDialog.ZIndex = 100;
        _deleteConfirmDialog.MouseFilter = Control.MouseFilterEnum.Stop;
        float dfs = RpgBaseForm.FormScale;
        _deleteConfirmDialog.Scale = new Vector2(dfs, dfs);

        // V2 background
        var bgTex = new TextureRect();
        bgTex.Texture = RpgTheme.GetTex("big_bar.png");
        bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
        bgTex.MouseFilter = Control.MouseFilterEnum.Ignore;
        _deleteConfirmDialog.AddChild(bgTex);
        RpgTheme.FillParent(bgTex);

        // Content area with margins
        var marginC = new MarginContainer();
        marginC.AddThemeConstantOverride("margin_top", 30);
        marginC.AddThemeConstantOverride("margin_left", 36);
        marginC.AddThemeConstantOverride("margin_right", 36);
        marginC.AddThemeConstantOverride("margin_bottom", 38);
        marginC.MouseFilter = Control.MouseFilterEnum.Ignore;
        _deleteConfirmDialog.AddChild(marginC);
        RpgTheme.FillParent(marginC);

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        marginC.AddChild(vbox);

        _deleteConfirmLabel = new Label();
        _deleteConfirmLabel.Text = "";
        _deleteConfirmLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _deleteConfirmLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _deleteConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        _deleteConfirmLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_deleteConfirmLabel);

        _deleteConfirmInput = RpgTheme.CreateRpgInput("Codigo");
        _deleteConfirmInput.Alignment = HorizontalAlignment.Center;
        _deleteConfirmInput.FocusMode = Control.FocusModeEnum.Click;
        _deleteConfirmInput.TextSubmitted += (_) => OnDeleteConfirm();
        vbox.AddChild(_deleteConfirmInput);

        var hbox = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        var confirmBtn = RpgTheme.CreateRpgButton("Confirmar", false, 12);
        confirmBtn.CustomMinimumSize = new Vector2(80, 30);
        confirmBtn.Pressed += OnDeleteConfirm;
        hbox.AddChild(confirmBtn);

        var cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 12);
        cancelBtn.CustomMinimumSize = new Vector2(80, 30);
        cancelBtn.Pressed += OnDeleteConfirmCancel;
        hbox.AddChild(cancelBtn);

        parent.AddChild(_deleteConfirmDialog);
    }

    /// <summary>Hide the delete confirm dialog (called on screen change).</summary>
    public void HideDeleteConfirm()
    {
        if (_deleteConfirmDialog != null) _deleteConfirmDialog.Visible = false;
    }

    /// <summary>
    /// Show delete confirmation dialog for the selected character.
    /// </summary>
    public void ShowDeleteConfirm(ItemList charList, Label noticeLabel)
    {
        if (!charList.IsAnythingSelected())
        {
            noticeLabel.Text = "Seleccione un personaje";
            return;
        }

        var rng = new Random();
        _deleteConfirmCode = rng.Next(1000, 10000);
        _deleteConfirmLabel!.Text = $"Esta accion no podra ser revertida.\nIngresa el codigo {_deleteConfirmCode} para confirmar.";
        _deleteConfirmInput!.Text = "";
        _deleteConfirmDialog!.Visible = true;
        _deleteConfirmInput.GrabFocus();
    }

    /// <summary>Callback for confirmed delete. Returns (charName, account, code) or null if invalid.</summary>
    public Action<string>? OnDeleteCharConfirmed;

    private void OnDeleteConfirm()
    {
        if (_deleteConfirmInput == null) return;

        if (!int.TryParse(_deleteConfirmInput.Text.Trim(), out int inputCode) || inputCode != _deleteConfirmCode)
        {
            _deleteConfirmLabel!.Text = $"Codigo incorrecto. Ingresa {_deleteConfirmCode} para confirmar.";
            _deleteConfirmLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
            _deleteConfirmInput.Text = "";
            return;
        }

        _deleteConfirmDialog!.Visible = false;
        OnDeleteCharConfirmed?.Invoke(_deleteConfirmInput.Text);
    }

    private void OnDeleteConfirmCancel()
    {
        _deleteConfirmDialog!.Visible = false;
        _deleteConfirmLabel!.AddThemeColorOverride("font_color", Colors.White);
    }

    public void ResetForm()
    {
        _state.CreateCharName = "";
        _state.CreateCharRace = 1;
        _state.CreateCharGender = 1;
        _state.CreateCharClass = 1;
        _state.CreateCharFaction = 1;
        _charCreateNameInput!.Text = "";
        _charCreateError!.Text = "";
        RpgTheme.SetToggleGroupActive(_raceToggle!, 0);
        RpgTheme.SetToggleGroupActive(_genderToggle!, 0);
        RpgTheme.SetToggleGroupActive(_classToggle!, 0);
        RpgTheme.SetToggleGroupActive(_factionToggle!, 0);
        UpdateHeadRange();
    }

    public void OnCharCreateConfirm()
    {
        string name = _charCreateNameInput!.Text.Trim();
        if (name.Length < 4 || name.Length > 15)
        {
            _charCreateError!.Text = "El nombre debe tener entre 4 y 15 caracteres.";
            return;
        }

        bool lastWasSpace = false;
        foreach (char c in name)
        {
            if (c == ' ')
            {
                if (lastWasSpace)
                {
                    _charCreateError!.Text = "El nombre no puede tener espacios consecutivos.";
                    return;
                }
                lastWasSpace = true;
            }
            else if (!char.IsLetter(c))
            {
                _charCreateError!.Text = "El nombre solo puede contener letras y espacios.";
                return;
            }
            else
            {
                lastWasSpace = false;
            }
        }

        if (name.StartsWith(' ') || name.EndsWith(' '))
        {
            _charCreateError!.Text = "El nombre no puede empezar o terminar con espacio.";
            return;
        }

        _charCreateError!.Text = "";
        _charCreateCreateBtn!.Disabled = true;
        _state.CreateCharName = name;

        OnCreateCharConfirm?.Invoke();
    }

    private void UpdateHeadRange()
    {
        var (min, max) = GetHeadRange(_state.CreateCharRace, _state.CreateCharGender);
        _state.CreateCharHeadMin = min;
        _state.CreateCharHeadMax = max;
        _state.CreateCharHead = min;
        UpdateHeadPreview();
    }

    private void UpdateHeadPreview()
    {
        if (_charCreateHeadLabel != null)
            _charCreateHeadLabel.Text = _state.CreateCharHead.ToString();
        _charCreateHeadPreview?.QueueRedraw();
    }

    private void DrawCharCreateHead()
    {
        if (_charCreateHeadPreview == null) return;
        int headIdx = _state.CreateCharHead;
        if (headIdx <= 0 || headIdx >= _gameData.Heads.Length) return;

        var head = _gameData.Heads[headIdx];
        if (head.Head[3] == 0) return;

        CharRenderer.DrawGrh(_charCreateHeadPreview, _gameData, head.Head[3], 0,
            new Vector2(32, 32), true);
    }

    private void OnRaceSelected(int idx)
    {
        _state.CreateCharRace = idx + 1;
        UpdateHeadRange();
    }

    private void OnGenderSelected(int idx)
    {
        _state.CreateCharGender = idx + 1;
        UpdateHeadRange();
    }

    private void OnClassSelected(int idx)
    {
        _state.CreateCharClass = idx + 1;
    }

    private void OnFactionSelected(int idx)
    {
        _state.CreateCharFaction = idx + 1;
    }

    private void OnHeadPrev()
    {
        if (_state.CreateCharHead > _state.CreateCharHeadMin)
            _state.CreateCharHead--;
        else
            _state.CreateCharHead = _state.CreateCharHeadMax;
        UpdateHeadPreview();
    }

    private void OnHeadNext()
    {
        if (_state.CreateCharHead < _state.CreateCharHeadMax)
            _state.CreateCharHead++;
        else
            _state.CreateCharHead = _state.CreateCharHeadMin;
        UpdateHeadPreview();
    }
}

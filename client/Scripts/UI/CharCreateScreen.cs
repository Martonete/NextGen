using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// Character creation + selection + deletion UI. Extracted from Main.cs.
/// </summary>
public class CharCreateScreen
{
    private PanelContainer? _charCreatePanel;
    private LineEdit? _charCreateNameInput;
    private Button[]? _raceButtons;
    private Button[]? _genderButtons;
    private Button[]? _classButtons;
    private Button[]? _factionButtons;
    private Label? _charCreateHeadLabel;
    private Node2D? _charCreateHeadPreview;
    private Label? _charCreateError;
    private Button? _charCreateCreateBtn;
    private Button? _charSelectCreateBtn;
    private Button? _charSelectDeleteBtn;

    // Delete character confirmation dialog
    private PanelContainer? _deleteConfirmDialog;
    private Label? _deleteConfirmLabel;
    private LineEdit? _deleteConfirmInput;
    private int _deleteConfirmCode;

    private readonly GameState _state;
    private readonly GameData _gameData;

    public PanelContainer? Panel => _charCreatePanel;

    // Callbacks
    public Action? OnCreateCharConfirm;
    public Action? OnBack;
    public Action? OnCharSelectCreate;
    public Action? OnDeleteCharRequest;

    /// <summary>Access to the error label for external error messages.</summary>
    public Label? ErrorLabel => _charCreateError;
    /// <summary>Access to create button for enable/disable from outside.</summary>
    public Button? CreateButton => _charCreateCreateBtn;

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
        _charCreatePanel = new PanelContainer();
        _charCreatePanel.Size = new Vector2(420, 520);
        _charCreatePanel.Position = new Vector2(190, 40);
        _charCreatePanel.Visible = false;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        bg.BorderColor = new Color(0.4f, 0.35f, 0.2f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(12);
        _charCreatePanel.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _charCreatePanel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "Crear Personaje";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        title.AddThemeFontSizeOverride("font_size", 16);
        UIHelpers.ApplyFont(title);
        vbox.AddChild(title);

        // Name input
        var nameLabel = new Label();
        nameLabel.Text = "Nombre:";
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(nameLabel);

        _charCreateNameInput = new LineEdit();
        _charCreateNameInput.PlaceholderText = "4-15 caracteres";
        _charCreateNameInput.MaxLength = 15;
        _charCreateNameInput.CustomMinimumSize = new Vector2(0, 28);
        _charCreateNameInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_charCreateNameInput);

        // Race selector
        AddSectionLabel(vbox, "Raza:");
        var raceBox = new HBoxContainer();
        raceBox.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(raceBox);
        _raceButtons = CreateToggleGroup(raceBox, RaceNames, OnRaceSelected);

        // Gender selector
        AddSectionLabel(vbox, "Genero:");
        var genderBox = new HBoxContainer();
        genderBox.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(genderBox);
        _genderButtons = CreateToggleGroup(genderBox, GenderNames, OnGenderSelected);

        // Class selector (2 rows of 4)
        AddSectionLabel(vbox, "Clase:");
        var classGrid = new GridContainer();
        classGrid.Columns = 4;
        classGrid.AddThemeConstantOverride("h_separation", 3);
        classGrid.AddThemeConstantOverride("v_separation", 3);
        vbox.AddChild(classGrid);
        _classButtons = CreateToggleGroup(classGrid, ClassNames, OnClassSelected);

        // Faction selector
        AddSectionLabel(vbox, "Faccion:");
        var factionBox = new HBoxContainer();
        factionBox.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(factionBox);
        _factionButtons = CreateToggleGroup(factionBox, FactionNames, OnFactionSelected);

        // Head selector with preview
        AddSectionLabel(vbox, "Cabeza:");
        var headRow = new HBoxContainer();
        headRow.AddThemeConstantOverride("separation", 6);
        headRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(headRow);

        var headLeftBtn = new Button();
        headLeftBtn.Text = "<";
        headLeftBtn.CustomMinimumSize = new Vector2(32, 32);
        headLeftBtn.Pressed += OnHeadPrev;
        headRow.AddChild(headLeftBtn);

        var headPreviewContainer = new SubViewportContainer();
        headPreviewContainer.CustomMinimumSize = new Vector2(64, 64);
        headPreviewContainer.Stretch = true;
        headRow.AddChild(headPreviewContainer);

        var headViewport = new SubViewport();
        headViewport.Size = new Vector2I(64, 64);
        headViewport.TransparentBg = true;
        headViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        headPreviewContainer.AddChild(headViewport);

        _charCreateHeadPreview = new Node2D();
        _charCreateHeadPreview.Draw += DrawCharCreateHead;
        headViewport.AddChild(_charCreateHeadPreview);

        var headRightBtn = new Button();
        headRightBtn.Text = ">";
        headRightBtn.CustomMinimumSize = new Vector2(32, 32);
        headRightBtn.Pressed += OnHeadNext;
        headRow.AddChild(headRightBtn);

        _charCreateHeadLabel = new Label();
        _charCreateHeadLabel.Text = "1";
        _charCreateHeadLabel.AddThemeColorOverride("font_color", Colors.White);
        _charCreateHeadLabel.AddThemeFontSizeOverride("font_size", 11);
        headRow.AddChild(_charCreateHeadLabel);

        // Error label
        _charCreateError = new Label();
        _charCreateError.Text = "";
        _charCreateError.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _charCreateError.AddThemeFontSizeOverride("font_size", 11);
        _charCreateError.HorizontalAlignment = HorizontalAlignment.Center;
        _charCreateError.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(_charCreateError);

        // Buttons row
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _charCreateCreateBtn = new Button();
        _charCreateCreateBtn.Text = "Crear";
        _charCreateCreateBtn.CustomMinimumSize = new Vector2(100, 32);
        _charCreateCreateBtn.Pressed += OnCharCreateConfirm;
        btnRow.AddChild(_charCreateCreateBtn);

        var backBtn = new Button();
        backBtn.Text = "Volver";
        backBtn.CustomMinimumSize = new Vector2(100, 32);
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

        _charSelectCreateBtn = new Button();
        _charSelectCreateBtn.Text = "Crear Personaje";
        _charSelectCreateBtn.CustomMinimumSize = new Vector2(0, 32);
        _charSelectCreateBtn.Pressed += () => OnCharSelectCreate?.Invoke();
        charSelectVBox.AddChild(_charSelectCreateBtn);

        _charSelectDeleteBtn = new Button();
        _charSelectDeleteBtn.Text = "Borrar Personaje";
        _charSelectDeleteBtn.CustomMinimumSize = new Vector2(0, 32);
        _charSelectDeleteBtn.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _charSelectDeleteBtn.Pressed += () => OnDeleteCharRequest?.Invoke();
        charSelectVBox.AddChild(_charSelectDeleteBtn);
    }

    public void CreateDeleteConfirmDialog(Node parent)
    {
        _deleteConfirmDialog = new PanelContainer();
        _deleteConfirmDialog.Size = new Vector2(280, 140);
        _deleteConfirmDialog.Position = new Vector2(127, 258);
        _deleteConfirmDialog.Visible = false;
        _deleteConfirmDialog.ZIndex = 100;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.15f, 0.08f, 0.08f, 0.95f);
        bg.BorderColor = new Color(0.8f, 0.2f, 0.2f);
        bg.SetBorderWidthAll(1);
        bg.SetContentMarginAll(10);
        _deleteConfirmDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _deleteConfirmDialog.AddChild(vbox);

        _deleteConfirmLabel = new Label();
        _deleteConfirmLabel.Text = "";
        _deleteConfirmLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _deleteConfirmLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _deleteConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        _deleteConfirmLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_deleteConfirmLabel);

        _deleteConfirmInput = new LineEdit();
        _deleteConfirmInput.PlaceholderText = "Codigo";
        _deleteConfirmInput.Alignment = HorizontalAlignment.Center;
        _deleteConfirmInput.FocusMode = Control.FocusModeEnum.Click;
        _deleteConfirmInput.AddThemeFontSizeOverride("font_size", 12);
        _deleteConfirmInput.TextSubmitted += (_) => OnDeleteConfirm();
        vbox.AddChild(_deleteConfirmInput);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        var confirmBtn = new Button();
        confirmBtn.Text = "Confirmar";
        confirmBtn.CustomMinimumSize = new Vector2(80, 28);
        confirmBtn.AddThemeFontSizeOverride("font_size", 11);
        confirmBtn.Pressed += OnDeleteConfirm;
        hbox.AddChild(confirmBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.CustomMinimumSize = new Vector2(80, 28);
        cancelBtn.AddThemeFontSizeOverride("font_size", 11);
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
        SetToggleSelection(_raceButtons!, 0);
        SetToggleSelection(_genderButtons!, 0);
        SetToggleSelection(_classButtons!, 0);
        SetToggleSelection(_factionButtons!, 0);
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
        SetToggleSelection(_raceButtons!, idx);
        UpdateHeadRange();
    }

    private void OnGenderSelected(int idx)
    {
        _state.CreateCharGender = idx + 1;
        SetToggleSelection(_genderButtons!, idx);
        UpdateHeadRange();
    }

    private void OnClassSelected(int idx)
    {
        _state.CreateCharClass = idx + 1;
        SetToggleSelection(_classButtons!, idx);
    }

    private void OnFactionSelected(int idx)
    {
        _state.CreateCharFaction = idx + 1;
        SetToggleSelection(_factionButtons!, idx);
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

    private static void AddSectionLabel(VBoxContainer vbox, string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        label.AddThemeFontSizeOverride("font_size", 10);
        vbox.AddChild(label);
    }

    private static Button[] CreateToggleGroup(Container parent, string[] labels, Action<int> onSelected)
    {
        var buttons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = labels[i];
            btn.ToggleMode = true;
            btn.CustomMinimumSize = new Vector2(0, 26);
            btn.AddThemeFontSizeOverride("font_size", 10);
            btn.Pressed += () => onSelected(idx);
            parent.AddChild(btn);
            buttons[i] = btn;
        }
        return buttons;
    }

    private static void SetToggleSelection(Button[] buttons, int selectedIndex)
    {
        for (int i = 0; i < buttons.Length; i++)
            buttons[i].ButtonPressed = (i == selectedIndex);
    }
}

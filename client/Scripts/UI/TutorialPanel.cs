using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// First-time player tutorial overlay.
/// Step-by-step instruction panels covering movement, combat, inventory, chat, and skills.
/// "Next" / "Skip" buttons. Saves tutorial completion state to config.
/// Styled with RpgTheme.
/// </summary>
public partial class TutorialPanel : Control
{
    private GameState? _state;
    private string _dataPath = "";

    // Controls
    private Control? _panel;
    private Label? _titleLabel;
    private Label? _bodyLabel;
    private Label? _stepLabel;
    private TextureButton? _nextBtn;
    private TextureButton? _prevBtn;
    private TextureButton? _skipBtn;

    // State
    private int _currentStep;
    private bool _completed;
    private bool _dragging;
    private Vector2 _dragOffset;

    // Tutorial step data
    private static readonly (string title, string body)[] Steps = new[]
    {
        (
            "Bienvenido a Argentum Nextgen!",
            "Este tutorial te guiara por los controles basicos del juego.\n\n" +
            "Puedes saltear este tutorial en cualquier momento presionando 'Saltar'."
        ),
        (
            "Movimiento",
            "Usa las flechas del teclado para moverte por el mundo.\n\n" +
            "- Flecha Arriba: caminar al norte\n" +
            "- Flecha Abajo: caminar al sur\n" +
            "- Flecha Izquierda: caminar al oeste\n" +
            "- Flecha Derecha: caminar al este\n\n" +
            "Manten presionada la tecla para caminar continuamente."
        ),
        (
            "Combate",
            "Para atacar, presiona Ctrl. Tu personaje atacara en la direccion que esta mirando.\n\n" +
            "Para lanzar hechizos:\n" +
            "1. Selecciona un hechizo de la lista de hechizos (tab Hechizos)\n" +
            "2. Presiona 'Lanzar'\n" +
            "3. Haz click en el objetivo\n\n" +
            "Presiona 'S' para activar/desactivar el Seguro (previene atacar ciudadanos)."
        ),
        (
            "Inventario y Objetos",
            "Tu inventario se muestra a la derecha de la pantalla.\n\n" +
            "- Doble click en un item para usarlo o equiparlo\n" +
            "- Arrastra items para reordenar\n" +
            "- Presiona 'R' para recoger objetos del suelo\n" +
            "- Puedes tirar objetos activando DyD (Drag & Drop) con el boton correspondiente\n\n" +
            "Los objetos equipados se muestran resaltados."
        ),
        (
            "Chat y Comunicacion",
            "Presiona Enter para abrir el chat. Escribe tu mensaje y presiona Enter para enviarlo.\n\n" +
            "Comandos utiles:\n" +
            "- /ONLINE — ver jugadores conectados\n" +
            "- /PM nombre mensaje — mensaje privado\n" +
            "- /GLOBAL mensaje — chat global\n" +
            "- /GM mensaje — pedir ayuda a un GM\n" +
            "- /EST — ver tus estadisticas\n\n" +
            "Usa las pestanas del chat para filtrar mensajes."
        ),
        (
            "Skills y Progresion",
            "Al subir de nivel, ganas puntos de skill para mejorar tus habilidades.\n\n" +
            "Presiona F5 o el boton 'Estadisticas' para ver tu panel de stats.\n" +
            "En la pestana 'Skills' puedes distribuir tus puntos libres.\n\n" +
            "Habilidades importantes:\n" +
            "- Armas/Defensa: mejoran tu combate\n" +
            "- Magia: mejora tus hechizos\n" +
            "- Pesca/Mineria/Talar: recursos\n" +
            "- Comerciar: mejores precios\n\n" +
            "Ya estas listo para explorar Argentum Nextgen!"
        ),
    };

    public void Init(GameState state, string dataPath)
    {
        _state = state;
        _dataPath = dataPath;
    }

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 150; // Above game, below loading screen

        MouseFilter = MouseFilterEnum.Ignore;

        // Central panel with NinePatch frame
        _panel = new Control();
        _panel.Size = new Vector2(500, 400);
        _panel.CustomMinimumSize = new Vector2(500, 400);
        _panel.Position = new Vector2(
            (ResolutionManager.WindowWidth - 500) / 2f,
            (ResolutionManager.WindowHeight - 400) / 2f);
        _panel.ClipContents = true;
        _panel.MouseFilter = MouseFilterEnum.Stop;

        // V2 background
        var bg = new TextureRect();
        bg.Texture = RpgTheme.GetTex("big_bar.png");
        bg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bg.StretchMode = TextureRect.StretchModeEnum.Scale;
        bg.MouseFilter = MouseFilterEnum.Ignore;
        _panel.AddChild(bg);
        RpgTheme.FillParent(bg);

        var titleBg = RpgTheme.CreateNinePatch("name_frame_mid_ready.png", new Vector4(30, 10, 30, 10));
        _panel.AddChild(titleBg);
        titleBg.AnchorLeft = 0f; titleBg.AnchorRight = 1f;
        titleBg.AnchorTop = 0f;  titleBg.AnchorBottom = 0f;
        titleBg.OffsetLeft = 10; titleBg.OffsetTop = 5;
        titleBg.OffsetRight = -10; titleBg.OffsetBottom = 48;

        var titleLabel = RpgTheme.CreateTitleLabel("Tutorial", 18);
        titleBg.AddChild(titleLabel);
        RpgTheme.FillParent(titleLabel);

        // Close X button (top-right)
        var closeBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(28, 28));
        closeBtn.Pressed += CompleteTutorial;
        _panel.AddChild(closeBtn);
        closeBtn.AnchorLeft = 1.0f; closeBtn.AnchorRight = 1.0f;
        closeBtn.AnchorTop = 0.0f;  closeBtn.AnchorBottom = 0.0f;
        closeBtn.OffsetLeft = -38;   closeBtn.OffsetTop = 13;
        closeBtn.OffsetRight = -8;   closeBtn.OffsetBottom = 36;

        AddChild(_panel);

        // Content area with V2 margins
        var marginC = new MarginContainer();
        marginC.AddThemeConstantOverride("margin_top", 54);
        marginC.AddThemeConstantOverride("margin_left", RpgTheme.FormMarginLeft);
        marginC.AddThemeConstantOverride("margin_right", RpgTheme.FormMarginRight);
        marginC.AddThemeConstantOverride("margin_bottom", RpgTheme.FormMarginBottom);
        marginC.MouseFilter = MouseFilterEnum.Ignore;
        _panel.AddChild(marginC);
        RpgTheme.FillParent(marginC);

        var root = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        marginC.AddChild(root);

        // Title
        _titleLabel = RpgTheme.CreateTitleLabel("", 16);
        root.AddChild(_titleLabel);

        // Separator
        root.AddChild(RpgTheme.CreateSeparator());

        // Body text (scrollable)
        var scrollArea = RpgTheme.CreateScrollArea();
        root.AddChild(scrollArea);
        var scrollContent = scrollArea.GetMeta("content").As<VBoxContainer>();

        _bodyLabel = RpgTheme.CreateInfoLabel("", 12);
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scrollContent.AddChild(_bodyLabel);

        // Bottom bar: step indicator + buttons
        var bottomBar = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        root.AddChild(bottomBar);

        _stepLabel = RpgTheme.CreateInfoLabel("", 10);
        _stepLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottomBar.AddChild(_stepLabel);

        _prevBtn = RpgTheme.CreateRpgButton("Anterior", false, 12);
        _prevBtn.CustomMinimumSize = new Vector2(80, 30);
        _prevBtn.Pressed += OnPrevPressed;
        bottomBar.AddChild(_prevBtn);

        _nextBtn = RpgTheme.CreateRpgButton("Siguiente", false, 12);
        _nextBtn.CustomMinimumSize = new Vector2(80, 30);
        _nextBtn.Pressed += OnNextPressed;
        bottomBar.AddChild(_nextBtn);

        _skipBtn = RpgTheme.CreateRpgButton("Saltar", false, 12);
        _skipBtn.CustomMinimumSize = new Vector2(60, 30);
        _skipBtn.Pressed += OnSkipPressed;
        bottomBar.AddChild(_skipBtn);
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || _panel == null) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // Only drag from the title bar area (top 48px of the panel)
                var titleRect = new Rect2(_panel.GlobalPosition, new Vector2(_panel.Size.X, 48));
                if (titleRect.HasPoint(mb.GlobalPosition))
                {
                    _dragging = true;
                    _dragOffset = mb.GlobalPosition - _panel.GlobalPosition;
                }
            }
            else
            {
                _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            _panel.GlobalPosition = mm.GlobalPosition - _dragOffset;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Show the tutorial from the beginning.
    /// </summary>
    public void Open()
    {
        if (_completed) return; // Already completed, don't show again
        _currentStep = 0;
        RefreshStep();
        Visible = true;
    }

    /// <summary>
    /// Check if the tutorial has been completed (from saved config).
    /// </summary>
    public bool IsCompleted()
    {
        return _completed;
    }

    /// <summary>
    /// Mark the tutorial as completed (loaded from config at startup).
    /// </summary>
    public void SetCompleted(bool completed)
    {
        _completed = completed;
    }

    private void RefreshStep()
    {
        if (_currentStep < 0 || _currentStep >= Steps.Length) return;
        var step = Steps[_currentStep];
        if (_titleLabel != null) _titleLabel.Text = step.title;
        if (_bodyLabel != null) _bodyLabel.Text = step.body;
        if (_stepLabel != null) _stepLabel.Text = $"Paso {_currentStep + 1} de {Steps.Length}";

        if (_prevBtn != null) _prevBtn.Visible = _currentStep > 0;
        if (_nextBtn != null)
        {
            // Update the button label text for last step
            var label = _nextBtn.GetChildOrNull<Label>(0);
            if (label != null)
                label.Text = _currentStep == Steps.Length - 1 ? "Finalizar" : "Siguiente";
        }
    }

    private void OnNextPressed()
    {
        if (_currentStep < Steps.Length - 1)
        {
            _currentStep++;
            RefreshStep();
        }
        else
        {
            CompleteTutorial();
        }
    }

    private void OnPrevPressed()
    {
        if (_currentStep > 0)
        {
            _currentStep--;
            RefreshStep();
        }
    }

    private void OnSkipPressed()
    {
        CompleteTutorial();
    }

    private void CompleteTutorial()
    {
        _completed = true;
        Visible = false;
        SaveCompletion();
    }

    private void SaveCompletion()
    {
        // Save tutorial completion to config file
        if (string.IsNullOrEmpty(_dataPath)) return;
        try
        {
            string configDir = System.IO.Path.Combine(_dataPath, "INIT");
            if (!System.IO.Directory.Exists(configDir))
                System.IO.Directory.CreateDirectory(configDir);
            string path = System.IO.Path.Combine(configDir, "Tutorial.cfg");
            System.IO.File.WriteAllText(path, "completed=1\n");
            GD.Print("[Tutorial] Completion state saved.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Tutorial] Failed to save completion: {ex.Message}");
        }
    }

    /// <summary>
    /// Load tutorial completion state from config file.
    /// </summary>
    public void LoadCompletion()
    {
        if (string.IsNullOrEmpty(_dataPath)) return;
        try
        {
            string path = System.IO.Path.Combine(_dataPath, "INIT", "Tutorial.cfg");
            if (System.IO.File.Exists(path))
            {
                string content = System.IO.File.ReadAllText(path);
                _completed = content.Contains("completed=1");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Tutorial] Failed to load completion: {ex.Message}");
        }
    }
}

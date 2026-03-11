using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// First-time player tutorial overlay.
/// Step-by-step instruction panels covering movement, combat, inventory, chat, and skills.
/// "Next" / "Skip" buttons. Saves tutorial completion state to config.
/// </summary>
public partial class TutorialPanel : Control
{
    private GameState? _state;
    private string _dataPath = "";

    // Controls
    private ColorRect? _overlay;
    private PanelContainer? _panel;
    private Label? _titleLabel;
    private Label? _bodyLabel;
    private Label? _stepLabel;
    private Button? _nextBtn;
    private Button? _prevBtn;
    private Button? _skipBtn;

    // State
    private int _currentStep;
    private bool _completed;

    // Tutorial step data
    private static readonly (string title, string body)[] Steps = new[]
    {
        (
            "Bienvenido a Tierras Sagradas!",
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
            "Ya estas listo para explorar Tierras Sagradas!"
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

        // Semi-transparent overlay
        _overlay = new ColorRect();
        _overlay.Color = new Color(0, 0, 0, 0.5f);
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.Size = new Vector2(800, 600);
        _overlay.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_overlay);

        // Central panel
        _panel = new PanelContainer();
        _panel.Position = new Vector2(150, 100);
        _panel.Size = new Vector2(500, 400);
        _panel.CustomMinimumSize = new Vector2(500, 400);

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        panelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 1f);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.SetCornerRadiusAll(6);
        panelStyle.SetContentMarginAll(16);
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_panel);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(root);

        // Title
        _titleLabel = new Label();
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(_titleLabel);

        // Separator
        var sep = new HSeparator();
        root.AddChild(sep);

        // Body text (scrollable)
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        root.AddChild(scroll);

        _bodyLabel = new Label();
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _bodyLabel.AddThemeColorOverride("font_color", Colors.White);
        _bodyLabel.AddThemeFontSizeOverride("font_size", 12);
        scroll.AddChild(_bodyLabel);

        // Bottom bar: step indicator + buttons
        var bottomBar = new HBoxContainer();
        bottomBar.AddThemeConstantOverride("separation", 8);
        root.AddChild(bottomBar);

        _stepLabel = new Label();
        _stepLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stepLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        _stepLabel.AddThemeFontSizeOverride("font_size", 10);
        bottomBar.AddChild(_stepLabel);

        _prevBtn = new Button();
        _prevBtn.Text = "Anterior";
        _prevBtn.CustomMinimumSize = new Vector2(80, 28);
        _prevBtn.Pressed += OnPrevPressed;
        bottomBar.AddChild(_prevBtn);

        _nextBtn = new Button();
        _nextBtn.Text = "Siguiente";
        _nextBtn.CustomMinimumSize = new Vector2(80, 28);
        _nextBtn.Pressed += OnNextPressed;
        bottomBar.AddChild(_nextBtn);

        _skipBtn = new Button();
        _skipBtn.Text = "Saltar";
        _skipBtn.CustomMinimumSize = new Vector2(60, 28);
        _skipBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        _skipBtn.Pressed += OnSkipPressed;
        bottomBar.AddChild(_skipBtn);
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
            _nextBtn.Text = _currentStep == Steps.Length - 1 ? "Finalizar" : "Siguiente";
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

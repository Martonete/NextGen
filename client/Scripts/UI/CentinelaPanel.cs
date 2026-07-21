using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

public partial class CentinelaPanel : Control
{
    private const int PanelW = 320;
    private const int PanelH = 210;

    private GameState? _state;
    private AoTcpClient? _tcp;

    private Label? _numberLabel;
    private Label? _timerLabel;
    private Label? _attemptsLabel;
    private LineEdit? _inputField;
    private ProgressBar? _timerBar;

    private float _secondsLeft;
    private float _totalSeconds;
    private int _challengeNumber;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 32;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = RpgBaseForm.ZDialog + 10;

        BuildUI();
    }

    private void BuildUI()
    {
        var bg = new ColorRect();
        bg.Color = new Color(0.05f, 0.08f, 0.05f, 0.97f);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);
        RpgTheme.FillParent(bg);

        var frame = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(frame);
        RpgTheme.FillParent(frame);

        var titleBar = new ColorRect();
        titleBar.Color = new Color(0f, 0.35f, 0f, 0.9f);
        titleBar.Position = new Vector2(2, 2);
        titleBar.Size = new Vector2(PanelW - 4, TitleBarH);
        titleBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(titleBar);

        var titleLabel = RpgTheme.CreateTitleLabel("VERIFICACION ANTI-BOT", 13);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.Position = new Vector2(0, 6);
        titleLabel.Size = new Vector2(PanelW, 22);
        titleLabel.MouseFilter = MouseFilterEnum.Ignore;
        titleLabel.Modulate = new Color(0.8f, 1f, 0.8f);
        AddChild(titleLabel);

        var margin = new MarginContainer();
        margin.Position = new Vector2(0, TitleBarH + 4);
        margin.Size = new Vector2(PanelW, PanelH - TitleBarH - 4);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var col = RpgTheme.CreateColumn(6);
        margin.AddChild(col);

        var promptLabel = RpgTheme.CreateInfoLabel("Escribi el siguiente numero:", 11);
        promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        promptLabel.Modulate = new Color(0.85f, 0.85f, 0.85f);
        col.AddChild(promptLabel);

        _numberLabel = RpgTheme.CreateTitleLabel("????", 28);
        _numberLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _numberLabel.Modulate = new Color(0.2f, 1f, 0.2f);
        _numberLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddChild(_numberLabel);

        _timerBar = new ProgressBar();
        _timerBar.MinValue = 0;
        _timerBar.MaxValue = 1;
        _timerBar.Value = 1;
        _timerBar.CustomMinimumSize = new Vector2(0, 8);
        _timerBar.ShowPercentage = false;
        var timerStyle = new StyleBoxFlat();
        timerStyle.BgColor = new Color(0f, 0.6f, 0f);
        _timerBar.AddThemeStyleboxOverride("fill", timerStyle);
        col.AddChild(_timerBar);

        var infoRow = RpgTheme.CreateRow(8);
        col.AddChild(infoRow);

        _timerLabel = RpgTheme.CreateInfoLabel("120s", 10);
        _timerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        infoRow.AddChild(_timerLabel);

        _attemptsLabel = RpgTheme.CreateInfoLabel("", 10);
        _attemptsLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _attemptsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        infoRow.AddChild(_attemptsLabel);

        var inputRow = RpgTheme.CreateRow(6);
        col.AddChild(inputRow);

        _inputField = RpgTheme.CreateRpgInput("ingresa el numero", PanelW - 130);
        _inputField.MaxLength = 4;
        _inputField.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _inputField.TextSubmitted += (_) => Submit();
        inputRow.AddChild(_inputField);

        var confirmBtn = RpgTheme.CreateRpgButton("Verificar", false, 14);
        confirmBtn.Pressed += Submit;
        inputRow.AddChild(confirmBtn);
    }

    public void ShowChallenge(int number, int seconds, int attemptsLeft)
    {
        _challengeNumber = number;
        _totalSeconds = seconds;
        _secondsLeft = seconds;

        if (_numberLabel != null) _numberLabel.Text = number.ToString();
        if (_timerBar != null) { _timerBar.MaxValue = seconds; _timerBar.Value = seconds; }
        if (_timerLabel != null) _timerLabel.Text = $"{seconds}s";
        if (_attemptsLabel != null)
            _attemptsLabel.Text = attemptsLeft > 0 ? $"Intentos: {attemptsLeft}" : "";
        if (_inputField != null) { _inputField.Text = ""; _inputField.GrabFocus(); }

        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        _secondsLeft -= (float)delta;
        if (_secondsLeft < 0f) _secondsLeft = 0f;

        if (_timerBar != null) _timerBar.Value = _secondsLeft;
        if (_timerLabel != null)
        {
            int secs = Mathf.CeilToInt(_secondsLeft);
            _timerLabel.Text = $"{secs}s";
            _timerLabel.Modulate = secs <= 30 ? new Color(1f, 0.3f, 0.3f) : new Color(0.85f, 0.85f, 0.85f);
        }

        if (_timerBar != null && _totalSeconds > 0)
        {
            float ratio = _secondsLeft / _totalSeconds;
            var fill = new StyleBoxFlat();
            fill.BgColor = ratio > 0.5f
                ? new Color(0f, 0.6f, 0f)
                : ratio > 0.25f
                    ? new Color(0.8f, 0.6f, 0f)
                    : new Color(0.8f, 0.1f, 0.1f);
            _timerBar.AddThemeStyleboxOverride("fill", fill);
        }

        if (_dragging && Input.IsMouseButtonPressed(MouseButton.Left))
            GlobalPosition = GetGlobalMousePosition() - _dragOffset;
        else if (_dragging)
            _dragging = false;
    }

    private void Submit()
    {
        if (_tcp == null || _inputField == null) return;
        string input = _inputField.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        _tcp.SendPacket(ClientPackets.WriteTalk($"/CENTINELA {input}"));
        Visible = false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && mb.Position.Y <= TitleBarH)
            {
                _dragging = true;
                _dragOffset = mb.Position;
                AcceptEvent();
                return;
            }
            if (!mb.Pressed) _dragging = false;
        }
    }
}

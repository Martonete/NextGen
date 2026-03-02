using Godot;
using System;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmBanco — Personal bank panel (gold + open vault).
/// ScaleWidth=165, ScaleHeight=196 (VB6 pixel mode).
/// 3 buttons: Abrir Bóveda, Depositar Oro, Retirar Oro.
/// Gold display label at top. Close button (Label1) at top-right.
/// </summary>
public partial class BankPanel : Control
{
    private const int PanelW = 165;
    private const int PanelH = 196;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // VB6 control positions (converted from twips÷15 to pixels)
    // Text1: gold display — Left=255/15=17, Top=720/15=48, Width=1920/15=128, Height=375/15=25
    // Image1(0): Bóveda — Left=360/15=24, Top=1200/15=80, Width=1770/15=118, Height=420/15=28
    // Image1(1): Depositar — Left=360/15=24, Top=1740/15=116, Width=1770/15=118, Height=420/15=28
    // Image1(2): Retirar — Left=360/15=24, Top=2280/15=152, Width=1770/15=118, Height=420/15=28
    // Label1: Close — Left=2085/15=139, Top=0, Width=375/15=25, Height=375/15=25

    private Label? _goldLabel;
    private Button? _bovedaBtn;
    private Button? _depositarBtn;
    private Button? _retirarBtn;
    private Button? _closeBtn;

    /// <summary>Fired when user clicks "Abrir Bóveda" — Main.cs opens VaultPanel.</summary>
    public event Action? OnOpenVault;

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;

        // Gold display
        _goldLabel = new Label();
        _goldLabel.Position = new Vector2(17, 48);
        _goldLabel.Size = new Vector2(128, 25);
        _goldLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _goldLabel.AddThemeColorOverride("font_color", Colors.White);
        _goldLabel.AddThemeFontSizeOverride("font_size", 12);
        var boldFont = new SystemFont();
        boldFont.FontWeight = 700;
        _goldLabel.AddThemeFontOverride("font", boldFont);
        _goldLabel.Text = "0";
        AddChild(_goldLabel);

        // Abrir Bóveda
        _bovedaBtn = CreateButton("Abrir Bóveda", 24, 80, 118, 28);
        _bovedaBtn.Pressed += OnBovedaPressed;
        AddChild(_bovedaBtn);

        // Depositar Oro
        _depositarBtn = CreateButton("Depositar Oro", 24, 116, 118, 28);
        _depositarBtn.Pressed += OnDepositarPressed;
        AddChild(_depositarBtn);

        // Retirar Oro
        _retirarBtn = CreateButton("Retirar Oro", 24, 152, 118, 28);
        _retirarBtn.Pressed += OnRetirarPressed;
        AddChild(_retirarBtn);

        // Close (X)
        _closeBtn = CreateButton("X", 139, 0, 25, 25);
        _closeBtn.Pressed += OnClosePressed;
        AddChild(_closeBtn);
    }

    private static Button CreateButton(string text, int x, int y, int w, int h)
    {
        var btn = new Button();
        btn.Text = text;
        btn.Position = new Vector2(x, y);
        btn.Size = new Vector2(w, h);
        btn.FocusMode = Control.FocusModeEnum.None;
        btn.MouseDefaultCursorShape = CursorShape.PointingHand;
        return btn;
    }

    public void OpenBank()
    {
        Visible = true;
    }

    public void CloseBank()
    {
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;
        _goldLabel!.Text = _state.BankGold.ToString("N0");
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Background
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.08f, 0.06f, 0.12f, 0.96f));
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.5f, 0.4f, 0.3f, 0.8f), false, 2f);

        var font = _data?.Fonts?[1];
        font?.DrawText(this, PanelW / 2, 8, "Banco", new Color(1f, 0.85f, 0.4f), center: true);
        font?.DrawText(this, PanelW / 2, 30, "Oro en bóveda:", Colors.White, center: true);
    }

    private void OnBovedaPressed()
    {
        // VB6: SendData("INIBOV") then Unload Me
        _tcp?.SendPacket("INIBOV");
        // Signal Main.cs to open VaultPanel
        _state!.BovedaAbierta = true;
        OnOpenVault?.Invoke();
        Visible = false; // Close bank panel, vault opens
    }

    private void OnDepositarPressed()
    {
        if (_state == null || _tcp == null) return;
        // VB6 uses InputBox — we'll use a simple prompt via chat message
        // For now, show a LineEdit dialog or use a fixed approach
        // Since we can't do VB6 InputBox in Godot easily, we'll add an inline input
        ShowGoldInputDialog(true);
    }

    private void OnRetirarPressed()
    {
        if (_state == null || _tcp == null) return;
        ShowGoldInputDialog(false);
    }

    private void OnClosePressed()
    {
        _tcp?.SendPacket("FINBAN");
    }

    // ── Inline gold input dialog ────────────────────────────────

    private LineEdit? _goldInput;
    private Label? _goldInputLabel;
    private Button? _goldInputOk;
    private Button? _goldInputCancel;
    private bool _isDepositing;

    private void ShowGoldInputDialog(bool deposit)
    {
        _isDepositing = deposit;

        if (_goldInput == null)
        {
            _goldInputLabel = new Label();
            _goldInputLabel.Position = new Vector2(10, 80);
            _goldInputLabel.Size = new Vector2(145, 16);
            _goldInputLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _goldInputLabel.AddThemeColorOverride("font_color", Colors.White);
            _goldInputLabel.AddThemeFontSizeOverride("font_size", 10);
            AddChild(_goldInputLabel);

            _goldInput = new LineEdit();
            _goldInput.Position = new Vector2(24, 100);
            _goldInput.Size = new Vector2(118, 20);
            _goldInput.Text = "0";
            _goldInput.Alignment = HorizontalAlignment.Center;
            _goldInput.FocusMode = FocusModeEnum.Click;
            _goldInput.AddThemeFontSizeOverride("font_size", 10);
            _goldInput.TextSubmitted += (_) => OnGoldInputOk();
            AddChild(_goldInput);

            _goldInputOk = CreateButton("OK", 24, 125, 55, 20);
            _goldInputOk.Pressed += OnGoldInputOk;
            AddChild(_goldInputOk);

            _goldInputCancel = CreateButton("Cancelar", 84, 125, 58, 20);
            _goldInputCancel.Pressed += OnGoldInputCancel;
            AddChild(_goldInputCancel);
        }

        _goldInputLabel!.Text = deposit ? "Depositar oro:" : "Retirar oro:";
        _goldInput!.Text = "0";
        _goldInput.Visible = true;
        _goldInputLabel.Visible = true;
        _goldInputOk!.Visible = true;
        _goldInputCancel!.Visible = true;
        _goldInput.GrabFocus();
        _goldInput.SelectAll();

        // Hide main buttons while dialog is open
        _bovedaBtn!.Visible = false;
        _depositarBtn!.Visible = false;
        _retirarBtn!.Visible = false;
    }

    private void HideGoldInputDialog()
    {
        if (_goldInput != null) _goldInput.Visible = false;
        if (_goldInputLabel != null) _goldInputLabel.Visible = false;
        if (_goldInputOk != null) _goldInputOk.Visible = false;
        if (_goldInputCancel != null) _goldInputCancel.Visible = false;

        _bovedaBtn!.Visible = true;
        _depositarBtn!.Visible = true;
        _retirarBtn!.Visible = true;
    }

    private void OnGoldInputOk()
    {
        if (_goldInput == null || _tcp == null || _state == null) return;
        if (!long.TryParse(_goldInput.Text.Trim(), out long amount) || amount <= 0)
        {
            HideGoldInputDialog();
            return;
        }

        if (_isDepositing)
        {
            // VB6: SendData("/DEPOSITAR " & cantidad)
            _tcp.SendPacket($"/DEPOSITAR {amount}");
        }
        else
        {
            // VB6: SendData("/RETIRAR " & cantidad)
            _tcp.SendPacket($"/RETIRAR {amount}");
        }

        HideGoldInputDialog();
    }

    private void OnGoldInputCancel()
    {
        HideGoldInputDialog();
    }
}

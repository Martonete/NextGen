using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmBanco — Personal bank panel (gold + open vault).
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class BankPanel : RpgBaseForm
{
    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    private Label? _goldLabel;
    private TextureButton? _bovedaBtn;
    private TextureButton? _depositarBtn;
    private TextureButton? _retirarBtn;

    // Gold input dialog controls
    private VBoxContainer? _mainContent;
    private VBoxContainer? _goldInputContent;
    private Label? _goldInputLabel;
    private LineEdit? _goldInput;
    private bool _isDepositing;

    private long _lastBankGold = -1;

    public event Action? OnOpenVault;

    public BankPanel() : base("Banco", new Vector2(240, 260), "v2") { }

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        // Main content (shown by default)
        _mainContent = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        ContentContainer.AddChild(_mainContent);

        // Gold display
        var goldRow = RpgTheme.CreateRow();
        goldRow.Alignment = BoxContainer.AlignmentMode.Center;
        _mainContent.AddChild(goldRow);

        var goldTitle = RpgTheme.CreateInfoLabel("Oro en bóveda:", 13);
        goldRow.AddChild(goldTitle);

        _goldLabel = RpgTheme.CreateTitleLabel("0", 16);
        _goldLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.4f));
        _mainContent.AddChild(_goldLabel);

        _mainContent.AddChild(RpgTheme.CreateSpacer(4));

        // Buttons
        _bovedaBtn = RpgTheme.CreateRpgButton("Abrir Bóveda", true, 13);
        _bovedaBtn.CustomMinimumSize = new Vector2(0, 34);
        _bovedaBtn.Pressed += OnBovedaPressed;
        _mainContent.AddChild(_bovedaBtn);

        _depositarBtn = RpgTheme.CreateRpgButton("Depositar Oro", true, 13);
        _depositarBtn.CustomMinimumSize = new Vector2(0, 34);
        _depositarBtn.Pressed += () => ShowGoldInputDialog(true);
        _mainContent.AddChild(_depositarBtn);

        _retirarBtn = RpgTheme.CreateRpgButton("Retirar Oro", true, 13);
        _retirarBtn.CustomMinimumSize = new Vector2(0, 34);
        _retirarBtn.Pressed += () => ShowGoldInputDialog(false);
        _mainContent.AddChild(_retirarBtn);

        // Gold input dialog (hidden by default)
        _goldInputContent = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        _goldInputContent.Visible = false;
        ContentContainer.AddChild(_goldInputContent);

        _goldInputLabel = RpgTheme.CreateInfoLabel("Depositar oro:", 13);
        _goldInputLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _goldInputContent.AddChild(_goldInputLabel);

        _goldInput = RpgTheme.CreateRpgInput("0");
        _goldInput.Text = "0";
        _goldInput.Alignment = HorizontalAlignment.Center;
        _goldInput.TextSubmitted += (_) => OnGoldInputOk();
        _goldInputContent.AddChild(_goldInput);

        var inputBtnRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        inputBtnRow.Alignment = BoxContainer.AlignmentMode.Center;
        _goldInputContent.AddChild(inputBtnRow);

        var okBtn = RpgTheme.CreateRpgButton("OK", false, 12);
        okBtn.CustomMinimumSize = new Vector2(70, 28);
        okBtn.Pressed += OnGoldInputOk;
        inputBtnRow.AddChild(okBtn);

        var cancelInputBtn = RpgTheme.CreateRpgButton("Cancelar", false, 12);
        cancelInputBtn.CustomMinimumSize = new Vector2(80, 28);
        cancelInputBtn.Pressed += HideGoldInputDialog;
        inputBtnRow.AddChild(cancelInputBtn);
    }

    public void OpenBank()
    {
        ShowForm();
    }

    public override void HideForm()
    {
        base.HideForm();
        // Clear bank flag when panel is closed (by X button, Escape, or server).
        // Skip if vault is taking over (BovedaAbierta=true means vault manages the session).
        if (_state != null && !_state.BovedaAbierta)
        {
            _state.Banqueando = false;
            _tcp?.SendPacket(ClientPackets.WriteBankClose());
        }
    }

    public void CloseBank()
    {
        HideForm();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;
        if (_state.BankGold != _lastBankGold)
        {
            _lastBankGold = _state.BankGold;
            _goldLabel!.Text = _state.BankGold.ToString("N0");
        }
    }

    private void OnBovedaPressed()
    {
        _tcp?.SendPacket(new byte[] { ClientPacketId.GuildBankOpen });
        _state!.BovedaAbierta = true;
        OnOpenVault?.Invoke();
        HideForm();
    }

    private void ShowGoldInputDialog(bool deposit)
    {
        _isDepositing = deposit;
        _goldInputLabel!.Text = deposit ? "Depositar oro:" : "Retirar oro:";
        _goldInput!.Text = "0";
        _mainContent!.Visible = false;
        _goldInputContent!.Visible = true;
        _goldInput.GrabFocus();
        _goldInput.SelectAll();
    }

    private void HideGoldInputDialog()
    {
        _mainContent!.Visible = true;
        _goldInputContent!.Visible = false;
    }

    private void OnGoldInputOk()
    {
        if (_goldInput == null || _tcp == null || _state == null) return;
        if (!long.TryParse(_goldInput.Text.Trim(), out long amount) || amount <= 0)
        { HideGoldInputDialog(); return; }

        if (_isDepositing)
            _tcp.SendPacket(ClientPackets.WriteTalk($"/DEPOSITAR {amount}"));
        else
            _tcp.SendPacket(ClientPackets.WriteTalk($"/RETIRAR {amount}"));

        HideGoldInputDialog();
    }
}

using Godot;
using System;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;
using TierrasSagradasAO.Rendering;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmNuevoBancoObj — Item vault (bóveda) panel.
/// VB6 ScaleWidth=450, ScaleHeight=527.
/// Two item lists: vault items (left, List1[0]) and player inventory (right, List1[1]).
/// Buttons: Retirar, Depositar, Retirar Oro, Depositar Oro, Salir.
/// Quantity input, gold displays, item preview icon.
/// </summary>
public partial class VaultPanel : Control
{
    // VB6 form dimensions (ScaleMode=3 Pixel): 450×527
    private const int PanelW = 450;
    private const int PanelH = 527;

    // VB6 List positions (twips÷15):
    // List1(0) vault: Left=540/15=36, Top=1600/15=107, Width=2715/15=181, Height=3735/15=249
    // List1(1) inventory: Left=3495/15=233, Top=1600/15=107, Width=2730/15=182, Height=3735/15=249
    private const int VaultListX = 36, VaultListY = 107;
    private const int VaultListW = 181, VaultListH = 249;
    private const int InvListX = 233, InvListY = 107;
    private const int InvListW = 182, InvListH = 249;

    // Item preview picture: Left=555/15=37, Top=600/15=40, Width/Height=510/15=34
    private const int PreviewX = 37, PreviewY = 40, PreviewSize = 34;

    // Quantity input: Left=2940/15=196, Top=5745/15=383, Width=915/15=61, Height=225/15=15
    private const int QtyX = 196, QtyY = 383, QtyW = 61, QtyH = 15;

    // Gold displays:
    // OroBove: Left=2925/15=195, Top=6330/15=422, Width=1785/15=119
    // MiOro: Left=2925/15=195, Top=6690/15=446, Width=1785/15=119
    private const int GoldDisplayX = 195, GoldDisplayW = 119;
    private const int VaultGoldY = 422, MyGoldY = 446;

    // Buttons (twips÷15):
    // Retirar: Left=810/15=54, Top=5610/15=374, Width=1695/15=113, Height=375/15=25
    // Depositar: Left=4230/15=282, Top=5610/15=374, Width=1695/15=113, Height=375/15=25
    // RetirarOro: Left=4815/15=321, Top=6330/15=422, Width=1305/15=87, Height=225/15=15
    // DepositarOro: Left=4815/15=321, Top=6690/15=446, Width=1305/15=87, Height=225/15=15
    // Salir: Left=2640/15=176, Top=7215/15=481, Width=1470/15=98, Height=360/15=24
    private const int ItemRowH = 16;

    // VB6 colors
    private static readonly Color ListBg = new(19f / 255f, 21f / 255f, 22f / 255f);
    private static readonly Color ListFg = new(145f / 255f, 123f / 255f, 85f / 255f);
    private static readonly Color GoldInputBg = new(19f / 255f, 21f / 255f, 22f / 255f);
    private static readonly Color GoldInputFg = new(145f / 255f, 123f / 255f, 85f / 255f);

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 30;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Selection state
    private int _selectedVaultIdx = -1;  // index into BankItems
    private int _selectedInvIdx = -1;    // index into filtered inventory
    private int _vaultScrollOffset;
    private int _invScrollOffset;

    // Filtered user inventory (non-empty, non-equipped)
    private int[] _userSlots = new int[25];
    private int _userSlotCount;

    // UI controls
    private LineEdit? _qtyInput;
    private Label? _vaultGoldLabel;
    private Label? _myGoldLabel;
    private Button? _retirarBtn;
    private Button? _depositarBtn;
    private Button? _retirarOroBtn;
    private Button? _depositarOroBtn;
    private Button? _salirBtn;

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

        // Quantity input
        _qtyInput = new LineEdit();
        _qtyInput.Position = new Vector2(QtyX, QtyY);
        _qtyInput.Size = new Vector2(QtyW, QtyH);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        _qtyInput.FocusMode = FocusModeEnum.Click;
        _qtyInput.AddThemeColorOverride("font_color", GoldInputFg);
        _qtyInput.AddThemeFontSizeOverride("font_size", 9);
        AddChild(_qtyInput);

        // Vault gold label
        _vaultGoldLabel = CreateLabel("0", GoldDisplayX, VaultGoldY, GoldDisplayW, 15);
        AddChild(_vaultGoldLabel);

        // My gold label
        _myGoldLabel = CreateLabel("0", GoldDisplayX, MyGoldY, GoldDisplayW, 15);
        AddChild(_myGoldLabel);

        // Retirar (withdraw item from vault)
        _retirarBtn = CreateButton("Retirar", 54, 374, 113, 25);
        _retirarBtn.Pressed += OnRetirarPressed;
        AddChild(_retirarBtn);

        // Depositar (deposit item to vault)
        _depositarBtn = CreateButton("Depositar", 282, 374, 113, 25);
        _depositarBtn.Pressed += OnDepositarPressed;
        AddChild(_depositarBtn);

        // Retirar Oro
        _retirarOroBtn = CreateButton("Retirar", 321, VaultGoldY, 87, 15);
        _retirarOroBtn.Pressed += OnRetirarOroPressed;
        AddChild(_retirarOroBtn);

        // Depositar Oro
        _depositarOroBtn = CreateButton("Depositar", 321, MyGoldY, 87, 15);
        _depositarOroBtn.Pressed += OnDepositarOroPressed;
        AddChild(_depositarOroBtn);

        // Salir
        _salirBtn = CreateButton("Salir", 176, 481, 98, 24);
        _salirBtn.Pressed += OnSalirPressed;
        AddChild(_salirBtn);
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

    private static Label CreateLabel(string text, int x, int y, int w, int h)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.Position = new Vector2(x, y);
        lbl.Size = new Vector2(w, h);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.AddThemeColorOverride("font_color", GoldInputFg);
        lbl.AddThemeFontSizeOverride("font_size", 9);
        var boldFont = new SystemFont();
        boldFont.FontWeight = 700;
        lbl.AddThemeFontOverride("font", boldFont);
        return lbl;
    }

    public void OpenVault()
    {
        _selectedVaultIdx = -1;
        _selectedInvIdx = -1;
        _vaultScrollOffset = 0;
        _invScrollOffset = 0;
        _qtyInput!.Text = "1";
        Visible = true;
    }

    public void CloseVault()
    {
        Visible = false;
        _selectedVaultIdx = -1;
        _selectedInvIdx = -1;
        HideGoldInputDialog();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Rebuild filtered user inventory
        _userSlotCount = 0;
        for (int i = 0; i < 25; i++)
        {
            if (_state.Inventory[i].ObjIndex > 0)
                _userSlots[_userSlotCount++] = i;
        }

        // Update gold displays
        _vaultGoldLabel!.Text = _state.BankGold.ToString("N0");
        _myGoldLabel!.Text = _state.Gold.ToString("N0");

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null || _data == null) return;

        // Background
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.08f, 0.06f, 0.12f, 0.96f));
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.5f, 0.4f, 0.3f, 0.8f), false, 2f);

        var font = _data.Fonts?[1];

        // Title
        font?.DrawText(this, PanelW / 2, 8, "Bóveda", new Color(1f, 0.85f, 0.4f), center: true);

        // List headers
        font?.DrawText(this, VaultListX + VaultListW / 2, VaultListY - 14, "Bóveda", Colors.White, center: true);
        font?.DrawText(this, InvListX + InvListW / 2, InvListY - 14, "Inventario", Colors.White, center: true);

        // Vault list background
        DrawRect(new Rect2(VaultListX, VaultListY, VaultListW, VaultListH), ListBg);
        DrawRect(new Rect2(VaultListX, VaultListY, VaultListW, VaultListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Inventory list background
        DrawRect(new Rect2(InvListX, InvListY, InvListW, InvListH), ListBg);
        DrawRect(new Rect2(InvListX, InvListY, InvListW, InvListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw vault items
        int maxVaultVisible = VaultListH / ItemRowH;
        for (int i = 0; i < maxVaultVisible && (i + _vaultScrollOffset) < _state.BankItemCount; i++)
        {
            int idx = i + _vaultScrollOffset;
            var item = _state.BankItems[idx];
            if (item.ObjIndex <= 0) continue;

            int rowY = VaultListY + i * ItemRowH;

            if (idx == _selectedVaultIdx)
                DrawRect(new Rect2(VaultListX + 1, rowY, VaultListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            string displayName = item.Name.Length > 13 ? item.Name[..13] : item.Name;
            font?.DrawText(this, VaultListX + 3, rowY + 1, $"{displayName} x{item.Amount}", ListFg);
        }

        // Draw inventory items
        int maxInvVisible = InvListH / ItemRowH;
        for (int i = 0; i < maxInvVisible && (i + _invScrollOffset) < _userSlotCount; i++)
        {
            int idx = i + _invScrollOffset;
            int slotIdx = _userSlots[idx];
            var inv = _state.Inventory[slotIdx];

            int rowY = InvListY + i * ItemRowH;

            if (idx == _selectedInvIdx)
                DrawRect(new Rect2(InvListX + 1, rowY, InvListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            string equip = inv.Equipped ? "(E)" : "";
            string displayName = inv.Name.Length > 12 ? inv.Name[..12] : inv.Name;
            font?.DrawText(this, InvListX + 3, rowY + 1, $"{displayName} x{inv.Amount}{equip}", ListFg);
        }

        // Item preview (selected from either list)
        DrawItemPreview(font);

        // Labels
        font?.DrawText(this, 145, QtyY + 1, "Cant:", Colors.White);
        font?.DrawText(this, 140, VaultGoldY + 1, "Oro Bóveda:", Colors.White);
        font?.DrawText(this, 155, MyGoldY + 1, "Mi Oro:", Colors.White);
    }

    private void DrawItemPreview(AoFont? font)
    {
        if (_state == null || _data == null) return;

        int grhIndex = 0;
        string name = "";

        if (_selectedVaultIdx >= 0 && _selectedVaultIdx < _state.BankItemCount)
        {
            var item = _state.BankItems[_selectedVaultIdx];
            grhIndex = item.GrhIndex;
            name = item.Name;
        }
        else if (_selectedInvIdx >= 0 && _selectedInvIdx < _userSlotCount)
        {
            int slotIdx = _userSlots[_selectedInvIdx];
            var inv = _state.Inventory[slotIdx];
            grhIndex = inv.GrhIndex;
            name = inv.Name;
        }

        // Preview box
        DrawRect(new Rect2(PreviewX, PreviewY, PreviewSize, PreviewSize), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(PreviewX, PreviewY, PreviewSize, PreviewSize), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        if (grhIndex > 0)
            CharRenderer.DrawGrh(this, _data, grhIndex, 0, new Vector2(PreviewX, PreviewY));

        if (!string.IsNullOrEmpty(name))
            font?.DrawText(this, PreviewX + PreviewSize + 8, PreviewY + 10, name, new Color(1f, 0.9f, 0.5f));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_state == null || _tcp == null) return;

        // Dragging by title bar
        if (@event is InputEventMouseButton dragMb)
        {
            if (dragMb.ButtonIndex == MouseButton.Left)
            {
                if (dragMb.Pressed && dragMb.Position.Y <= TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = dragMb.GlobalPosition - GlobalPosition;
                    AcceptEvent();
                    return;
                }
                if (!dragMb.Pressed && _dragging)
                    _dragging = false;
            }
        }
        if (@event is InputEventMouseMotion dragMm && _dragging)
        {
            GlobalPosition = dragMm.GlobalPosition - _dragOffset;
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            float mx = mb.Position.X;
            float my = mb.Position.Y;

            // Click in vault list
            if (mx >= VaultListX && mx < VaultListX + VaultListW && my >= VaultListY && my < VaultListY + VaultListH)
            {
                int row = (int)(my - VaultListY) / ItemRowH;
                int idx = row + _vaultScrollOffset;
                if (idx >= 0 && idx < _state.BankItemCount)
                {
                    _selectedVaultIdx = idx;
                    _selectedInvIdx = -1;
                }
                AcceptEvent();
                return;
            }

            // Click in inventory list
            if (mx >= InvListX && mx < InvListX + InvListW && my >= InvListY && my < InvListY + InvListH)
            {
                int row = (int)(my - InvListY) / ItemRowH;
                int idx = row + _invScrollOffset;
                if (idx >= 0 && idx < _userSlotCount)
                {
                    _selectedInvIdx = idx;
                    _selectedVaultIdx = -1;
                }
                AcceptEvent();
                return;
            }

            AcceptEvent();
        }
        else if (@event is InputEventMouseButton mbScroll)
        {
            float mx = mbScroll.Position.X;
            float my = mbScroll.Position.Y;

            // Scroll vault list
            if (mx >= VaultListX && mx < VaultListX + VaultListW && my >= VaultListY && my < VaultListY + VaultListH)
            {
                int maxVisible = VaultListH / ItemRowH;
                if (mbScroll.ButtonIndex == MouseButton.WheelDown)
                    _vaultScrollOffset = Math.Min(_vaultScrollOffset + 1, Math.Max(0, _state.BankItemCount - maxVisible));
                else if (mbScroll.ButtonIndex == MouseButton.WheelUp)
                    _vaultScrollOffset = Math.Max(0, _vaultScrollOffset - 1);
                AcceptEvent();
                return;
            }

            // Scroll inventory list
            if (mx >= InvListX && mx < InvListX + InvListW && my >= InvListY && my < InvListY + InvListH)
            {
                int maxVisible = InvListH / ItemRowH;
                if (mbScroll.ButtonIndex == MouseButton.WheelDown)
                    _invScrollOffset = Math.Min(_invScrollOffset + 1, Math.Max(0, _userSlotCount - maxVisible));
                else if (mbScroll.ButtonIndex == MouseButton.WheelUp)
                    _invScrollOffset = Math.Max(0, _invScrollOffset - 1);
                AcceptEvent();
                return;
            }
        }
    }

    private void OnRetirarPressed()
    {
        // Withdraw item from vault to inventory
        if (_state == null || _tcp == null) return;
        if (_selectedVaultIdx < 0 || _selectedVaultIdx >= _state.BankItemCount) return;

        var item = _state.BankItems[_selectedVaultIdx];
        if (item.ObjIndex <= 0) return;

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > item.Amount) qty = item.Amount;

        // VB6: SendData("RETI," & slot & "," & cantidad)
        _tcp.SendPacket(ClientPackets.WriteBankWithdraw((byte)item.Slot, (short)qty));
    }

    private void OnDepositarPressed()
    {
        // Deposit item from inventory to vault
        if (_state == null || _tcp == null) return;
        if (_selectedInvIdx < 0 || _selectedInvIdx >= _userSlotCount) return;

        int slotIdx = _userSlots[_selectedInvIdx];
        var inv = _state.Inventory[slotIdx];

        if (inv.Equipped)
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "No podes depositar el item porque lo estas usando.",
                Color = "FF0000"
            });
            return;
        }

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > inv.Amount) qty = inv.Amount;

        // VB6: SendData("DEPO," & (slot+1) & "," & cantidad)
        _tcp.SendPacket(ClientPackets.WriteBankDeposit((byte)(slotIdx + 1), (short)qty));
    }

    private void OnRetirarOroPressed()
    {
        ShowGoldInputDialog(false);
    }

    private void OnDepositarOroPressed()
    {
        ShowGoldInputDialog(true);
    }

    private void OnSalirPressed()
    {
        // VB6: SendData("FINBAN") then Unload Me
        _tcp?.SendPacket(ClientPackets.WriteBankClose());
    }

    private int GetQuantity()
    {
        if (_qtyInput == null) return 1;
        return int.TryParse(_qtyInput.Text.Trim(), out int v) && v > 0 ? v : 1;
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
            _goldInputLabel.Position = new Vector2(130, 400);
            _goldInputLabel.Size = new Vector2(190, 16);
            _goldInputLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _goldInputLabel.AddThemeColorOverride("font_color", Colors.White);
            _goldInputLabel.AddThemeFontSizeOverride("font_size", 10);
            AddChild(_goldInputLabel);

            _goldInput = new LineEdit();
            _goldInput.Position = new Vector2(150, 418);
            _goldInput.Size = new Vector2(100, 20);
            _goldInput.Text = "0";
            _goldInput.Alignment = HorizontalAlignment.Center;
            _goldInput.FocusMode = FocusModeEnum.Click;
            _goldInput.AddThemeFontSizeOverride("font_size", 10);
            _goldInput.TextSubmitted += (_) => OnGoldInputOk();
            AddChild(_goldInput);

            _goldInputOk = CreateButton("OK", 260, 418, 40, 20);
            _goldInputOk.Pressed += OnGoldInputOk;
            AddChild(_goldInputOk);

            _goldInputCancel = CreateButton("X", 305, 418, 25, 20);
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
    }

    private void HideGoldInputDialog()
    {
        if (_goldInput != null) _goldInput.Visible = false;
        if (_goldInputLabel != null) _goldInputLabel.Visible = false;
        if (_goldInputOk != null) _goldInputOk.Visible = false;
        if (_goldInputCancel != null) _goldInputCancel.Visible = false;
    }

    private void OnGoldInputOk()
    {
        if (_goldInput == null || _tcp == null) return;
        if (!long.TryParse(_goldInput.Text.Trim(), out long amount) || amount <= 0)
        {
            HideGoldInputDialog();
            return;
        }

        if (_isDepositing)
        {
            // VB6: SendData("CCDO" & cantidad) — but this is guild bank
            // Personal bank uses /DEPOSITAR via chat command
            _tcp.SendPacket(ClientPackets.WriteTalk($"/DEPOSITAR {amount}"));
        }
        else
        {
            _tcp.SendPacket(ClientPackets.WriteTalk($"/RETIRAR {amount}"));
        }

        HideGoldInputDialog();
    }

    private void OnGoldInputCancel()
    {
        HideGoldInputDialog();
    }
}

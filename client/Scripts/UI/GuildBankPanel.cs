using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmBovClan — Guild bank (bóveda de clan) panel.
/// Same layout as VaultPanel (450x527) but operates on GuildBankItems.
/// Leader/sublider can withdraw; all members can deposit.
/// </summary>
public partial class GuildBankPanel : Control
{
    private const int PanelW = 450;
    private const int PanelH = 527;

    private const int BankListX = 36, BankListY = 107;
    private const int BankListW = 181, BankListH = 249;
    private const int InvListX = 233, InvListY = 107;
    private const int InvListW = 182, InvListH = 249;

    private const int PreviewX = 37, PreviewY = 40, PreviewSize = 34;
    private const int QtyX = 196, QtyY = 383, QtyW = 61, QtyH = 15;
    private const int GoldDisplayX = 195, GoldDisplayW = 119;
    private const int BankGoldY = 422, MyGoldY = 446;
    private const int ItemRowH = 16;

    private static readonly Color ListBg = new(19f / 255f, 21f / 255f, 22f / 255f);
    private static readonly Color ListFg = new(145f / 255f, 123f / 255f, 85f / 255f);
    private static readonly Color GoldInputFg = new(145f / 255f, 123f / 255f, 85f / 255f);
    private static readonly Color DisabledFg = new(0.5f, 0.5f, 0.5f, 0.6f);

    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 30;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    private int _selectedBankIdx = -1;
    private int _selectedInvIdx = -1;
    private int _bankScrollOffset;
    private int _invScrollOffset;

    private int[] _userSlots = new int[25];
    private int _userSlotCount;
    private int _bankItemCount;

    private LineEdit? _qtyInput;
    private Label? _bankGoldLabel;
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

        _qtyInput = new LineEdit();
        _qtyInput.Position = new Vector2(QtyX, QtyY);
        _qtyInput.Size = new Vector2(QtyW, QtyH);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        _qtyInput.FocusMode = FocusModeEnum.Click;
        _qtyInput.AddThemeColorOverride("font_color", GoldInputFg);
        _qtyInput.AddThemeFontSizeOverride("font_size", 9);
        AddChild(_qtyInput);

        _bankGoldLabel = CreateLabel("0", GoldDisplayX, BankGoldY, GoldDisplayW, 15);
        AddChild(_bankGoldLabel);

        _myGoldLabel = CreateLabel("0", GoldDisplayX, MyGoldY, GoldDisplayW, 15);
        AddChild(_myGoldLabel);

        _retirarBtn = CreateButton("Retirar", 54, 374, 113, 25);
        _retirarBtn.Pressed += OnRetirarPressed;
        AddChild(_retirarBtn);

        _depositarBtn = CreateButton("Depositar", 282, 374, 113, 25);
        _depositarBtn.Pressed += OnDepositarPressed;
        AddChild(_depositarBtn);

        _retirarOroBtn = CreateButton("Retirar", 321, BankGoldY, 87, 15);
        _retirarOroBtn.Pressed += OnRetirarOroPressed;
        AddChild(_retirarOroBtn);

        _depositarOroBtn = CreateButton("Depositar", 321, MyGoldY, 87, 15);
        _depositarOroBtn.Pressed += OnDepositarOroPressed;
        AddChild(_depositarOroBtn);

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

    public void OpenGuildBank()
    {
        _selectedBankIdx = -1;
        _selectedInvIdx = -1;
        _bankScrollOffset = 0;
        _invScrollOffset = 0;
        _qtyInput!.Text = "1";
        Visible = true;
    }

    public void CloseGuildBank()
    {
        Visible = false;
        _selectedBankIdx = -1;
        _selectedInvIdx = -1;
        HideGoldInputDialog();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Count non-empty guild bank items
        _bankItemCount = 0;
        for (int i = 0; i < _state.GuildBankItems.Length; i++)
        {
            if (_state.GuildBankItems[i] != null && _state.GuildBankItems[i].ObjIndex > 0)
                _bankItemCount = i + 1; // track highest used slot
        }

        // Rebuild filtered user inventory
        _userSlotCount = 0;
        for (int i = 0; i < 25; i++)
        {
            if (_state.Inventory[i].ObjIndex > 0)
                _userSlots[_userSlotCount++] = i;
        }

        // Update gold displays
        _bankGoldLabel!.Text = _state.GuildBankGold.ToString("N0");
        _myGoldLabel!.Text = _state.Gold.ToString("N0");

        // Disable withdraw buttons if no permission
        _retirarBtn!.Disabled = !_state.GuildBankCanObj;
        _retirarOroBtn!.Disabled = !_state.GuildBankCanGold;

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null || _data == null) return;

        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.08f, 0.06f, 0.12f, 0.96f));
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.5f, 0.4f, 0.3f, 0.8f), false, 2f);

        var font = _data.Fonts?[1];

        font?.DrawText(this, PanelW / 2, 8, "Bóveda de Clan", new Color(1f, 0.85f, 0.4f), center: true);

        font?.DrawText(this, BankListX + BankListW / 2, BankListY - 14, "Bóveda Clan", Colors.White, center: true);
        font?.DrawText(this, InvListX + InvListW / 2, InvListY - 14, "Inventario", Colors.White, center: true);

        DrawRect(new Rect2(BankListX, BankListY, BankListW, BankListH), ListBg);
        DrawRect(new Rect2(BankListX, BankListY, BankListW, BankListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        DrawRect(new Rect2(InvListX, InvListY, InvListW, InvListH), ListBg);
        DrawRect(new Rect2(InvListX, InvListY, InvListW, InvListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw guild bank items
        int maxBankVisible = BankListH / ItemRowH;
        for (int i = 0; i < maxBankVisible && (i + _bankScrollOffset) < _bankItemCount; i++)
        {
            int idx = i + _bankScrollOffset;
            var item = _state.GuildBankItems[idx];
            if (item == null || item.ObjIndex <= 0) continue;

            int rowY = BankListY + i * ItemRowH;

            if (idx == _selectedBankIdx)
                DrawRect(new Rect2(BankListX + 1, rowY, BankListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            string displayName = item.Name.Length > 13 ? item.Name[..13] : item.Name;
            font?.DrawText(this, BankListX + 3, rowY + 1, $"{displayName} x{item.Amount}", ListFg);
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

        DrawItemPreview(font);

        font?.DrawText(this, 145, QtyY + 1, "Cant:", Colors.White);
        font?.DrawText(this, 120, BankGoldY + 1, "Oro Bóveda Clan:", Colors.White);
        font?.DrawText(this, 155, MyGoldY + 1, "Mi Oro:", Colors.White);

        // Permission indicator
        if (!_state.GuildBankCanObj)
            font?.DrawText(this, PanelW / 2, 365, "(Solo depósito — sin permiso de retiro)", DisabledFg, center: true);
    }

    private void DrawItemPreview(AoFont? font)
    {
        if (_state == null || _data == null) return;

        int grhIndex = 0;
        string name = "";

        if (_selectedBankIdx >= 0 && _selectedBankIdx < _state.GuildBankItems.Length)
        {
            var item = _state.GuildBankItems[_selectedBankIdx];
            if (item != null)
            {
                grhIndex = item.GrhIndex;
                name = item.Name;
            }
        }
        else if (_selectedInvIdx >= 0 && _selectedInvIdx < _userSlotCount)
        {
            int slotIdx = _userSlots[_selectedInvIdx];
            var inv = _state.Inventory[slotIdx];
            grhIndex = inv.GrhIndex;
            name = inv.Name;
        }

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

            if (mx >= BankListX && mx < BankListX + BankListW && my >= BankListY && my < BankListY + BankListH)
            {
                int row = (int)(my - BankListY) / ItemRowH;
                int idx = row + _bankScrollOffset;
                if (idx >= 0 && idx < _bankItemCount)
                {
                    _selectedBankIdx = idx;
                    _selectedInvIdx = -1;
                }
                AcceptEvent();
                return;
            }

            if (mx >= InvListX && mx < InvListX + InvListW && my >= InvListY && my < InvListY + InvListH)
            {
                int row = (int)(my - InvListY) / ItemRowH;
                int idx = row + _invScrollOffset;
                if (idx >= 0 && idx < _userSlotCount)
                {
                    _selectedInvIdx = idx;
                    _selectedBankIdx = -1;
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

            if (mx >= BankListX && mx < BankListX + BankListW && my >= BankListY && my < BankListY + BankListH)
            {
                int maxVisible = BankListH / ItemRowH;
                if (mbScroll.ButtonIndex == MouseButton.WheelDown)
                    _bankScrollOffset = Math.Min(_bankScrollOffset + 1, Math.Max(0, _bankItemCount - maxVisible));
                else if (mbScroll.ButtonIndex == MouseButton.WheelUp)
                    _bankScrollOffset = Math.Max(0, _bankScrollOffset - 1);
                AcceptEvent();
                return;
            }

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
        if (_state == null || _tcp == null) return;
        if (!_state.GuildBankCanObj) return;
        if (_selectedBankIdx < 0 || _selectedBankIdx >= _state.GuildBankItems.Length) return;

        var item = _state.GuildBankItems[_selectedBankIdx];
        if (item == null || item.ObjIndex <= 0) return;

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > item.Amount) qty = item.Amount;

        _tcp.SendPacket(ClientPackets.WriteGuildBankWithdrawItem((byte)(_selectedBankIdx + 1), (short)qty));
    }

    private void OnDepositarPressed()
    {
        if (_state == null || _tcp == null) return;
        if (_selectedInvIdx < 0 || _selectedInvIdx >= _userSlotCount) return;

        int slotIdx = _userSlots[_selectedInvIdx];
        var inv = _state.Inventory[slotIdx];

        if (inv.Equipped)
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "No podés depositar un objeto equipado.",
                Color = "FF0000"
            });
            return;
        }

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > inv.Amount) qty = inv.Amount;

        _tcp.SendPacket(ClientPackets.WriteGuildBankDepositItem((byte)(slotIdx + 1), (short)qty));
    }

    private void OnRetirarOroPressed()
    {
        if (!_state!.GuildBankCanGold) return;
        ShowGoldInputDialog(false);
    }

    private void OnDepositarOroPressed()
    {
        ShowGoldInputDialog(true);
    }

    private void OnSalirPressed()
    {
        _tcp?.SendPacket(ClientPackets.WriteGuildBankClose());
        CloseGuildBank();
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

        _goldInputLabel!.Text = deposit ? "Depositar oro en clan:" : "Retirar oro del clan:";
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
        if (!int.TryParse(_goldInput.Text.Trim(), out int amount) || amount <= 0)
        {
            HideGoldInputDialog();
            return;
        }

        if (_isDepositing)
            _tcp.SendPacket(ClientPackets.WriteGuildBankDepositGold(amount));
        else
            _tcp.SendPacket(ClientPackets.WriteGuildBankWithdrawGold(amount));

        HideGoldInputDialog();
    }

    private void OnGoldInputCancel()
    {
        HideGoldInputDialog();
    }
}

using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmBovClan — Guild bank (boveda de clan) panel.
/// Same layout as VaultPanel but operates on GuildBankItems.
/// Leader/sublider can withdraw; all members can deposit.
/// Now uses RpgBaseForm for consistent RPG chrome (frame, title, close, drag).
/// Custom _Draw() rendering for item lists is preserved inside a child Control.
/// </summary>
public partial class GuildBankPanel : RpgBaseForm
{
    // Content area dimensions
    private const int ContentW = 390;

    // List layout — relative to _drawArea origin
    private const int BankListX = 0, BankListY = 72;
    private const int BankListW = 175, BankListH = 249;
    private const int InvListX = 200, InvListY = 72;
    private const int InvListW = 175, InvListH = 249;

    private const int PreviewX = 0, PreviewY = 4, PreviewSize = 34;
    private const int ItemRowH = 16;

    // Draw area total height
    private const int DrawAreaH = BankListY + BankListH + 4; // 325

    private static readonly Color ListBg = new(19f / 255f, 21f / 255f, 22f / 255f);
    private static readonly Color ListFg = new(145f / 255f, 123f / 255f, 85f / 255f);
    private static readonly Color DisabledFg = new(0.5f, 0.5f, 0.5f, 0.6f);

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

    // UI controls
    private Control? _drawArea;
    private LineEdit? _qtyInput;
    private Label? _bankGoldLabel;
    private Label? _myGoldLabel;
    private TextureButton? _retirarBtn;
    private TextureButton? _depositarBtn;
    private TextureButton? _retirarOroBtn;
    private TextureButton? _depositarOroBtn;
    private TextureButton? _salirBtn;

    // Rich tooltip panel (set by Main.cs)
    public TooltipPanel? RichTooltip;

    public GuildBankPanel() : base("Boveda de Clan", new Vector2(450, 560), "v3") { }

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // Custom draw area for lists, preview, and headers
        _drawArea = new Control();
        _drawArea.CustomMinimumSize = new Vector2(ContentW, DrawAreaH);
        _drawArea.MouseFilter = MouseFilterEnum.Stop;
        _drawArea.Draw += OnDrawArea;
        _drawArea.GuiInput += OnDrawAreaInput;
        vbox.AddChild(_drawArea);

        // Button row: Retirar / Depositar
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _retirarBtn = RpgTheme.CreateRpgButton("Retirar", false, 14);
        _retirarBtn.CustomMinimumSize = new Vector2(140, 28);
        _retirarBtn.Pressed += OnRetirarPressed;
        btnRow.AddChild(_retirarBtn);

        // Quantity input
        var qtyLabel = RpgTheme.CreateInfoLabel("Cant:", 11);
        btnRow.AddChild(qtyLabel);

        _qtyInput = RpgTheme.CreateRpgInput("1", 60);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        btnRow.AddChild(_qtyInput);

        _depositarBtn = RpgTheme.CreateRpgButton("Depositar", false, 14);
        _depositarBtn.CustomMinimumSize = new Vector2(140, 28);
        _depositarBtn.Pressed += OnDepositarPressed;
        btnRow.AddChild(_depositarBtn);

        // Gold display rows
        var bankGoldRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        vbox.AddChild(bankGoldRow);

        var bankGoldTitle = RpgTheme.CreateInfoLabel("Oro Boveda Clan:", 11);
        bankGoldRow.AddChild(bankGoldTitle);

        _bankGoldLabel = RpgTheme.CreateTitleLabel("0", 12);
        _bankGoldLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.4f));
        _bankGoldLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _bankGoldLabel.HorizontalAlignment = HorizontalAlignment.Left;
        bankGoldRow.AddChild(_bankGoldLabel);

        _retirarOroBtn = RpgTheme.CreateRpgButton("Retirar", false, 11);
        _retirarOroBtn.CustomMinimumSize = new Vector2(87, 22);
        _retirarOroBtn.Pressed += OnRetirarOroPressed;
        bankGoldRow.AddChild(_retirarOroBtn);

        var myGoldRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        vbox.AddChild(myGoldRow);

        var myGoldTitle = RpgTheme.CreateInfoLabel("Mi Oro:", 11);
        myGoldRow.AddChild(myGoldTitle);

        _myGoldLabel = RpgTheme.CreateTitleLabel("0", 12);
        _myGoldLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.4f));
        _myGoldLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _myGoldLabel.HorizontalAlignment = HorizontalAlignment.Left;
        myGoldRow.AddChild(_myGoldLabel);

        _depositarOroBtn = RpgTheme.CreateRpgButton("Depositar", false, 11);
        _depositarOroBtn.CustomMinimumSize = new Vector2(87, 22);
        _depositarOroBtn.Pressed += OnDepositarOroPressed;
        myGoldRow.AddChild(_depositarOroBtn);

        // Salir button
        var footerRow = RpgTheme.CreateRow();
        footerRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(footerRow);

        _salirBtn = RpgTheme.CreateRpgButton("Salir", false, 14);
        _salirBtn.CustomMinimumSize = new Vector2(120, 28);
        _salirBtn.Pressed += OnSalirPressed;
        footerRow.AddChild(_salirBtn);
    }

    public void OpenGuildBank()
    {
        _selectedBankIdx = -1;
        _selectedInvIdx = -1;
        _bankScrollOffset = 0;
        _invScrollOffset = 0;
        _qtyInput!.Text = "1";
        ShowForm();
    }

    public void CloseGuildBank()
    {
        HideForm();
        _selectedBankIdx = -1;
        _selectedInvIdx = -1;
        HideGoldInputDialog();
        RichTooltip?.Hide();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
            RichTooltip?.Hide();
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

        _drawArea?.QueueRedraw();
    }

    private void OnDrawArea()
    {
        if (_state == null || _data == null || _drawArea == null) return;

        var font = _data.Fonts?[1];

        // List headers
        font?.DrawText(_drawArea, BankListX + BankListW / 2, BankListY - 14, "Boveda Clan", Colors.White, center: true);
        font?.DrawText(_drawArea, InvListX + InvListW / 2, InvListY - 14, "Inventario", Colors.White, center: true);

        // Bank list background
        _drawArea.DrawRect(new Rect2(BankListX, BankListY, BankListW, BankListH), ListBg);
        _drawArea.DrawRect(new Rect2(BankListX, BankListY, BankListW, BankListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Inventory list background
        _drawArea.DrawRect(new Rect2(InvListX, InvListY, InvListW, InvListH), ListBg);
        _drawArea.DrawRect(new Rect2(InvListX, InvListY, InvListW, InvListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw guild bank items
        int maxBankVisible = BankListH / ItemRowH;
        for (int i = 0; i < maxBankVisible && (i + _bankScrollOffset) < _bankItemCount; i++)
        {
            int idx = i + _bankScrollOffset;
            var item = _state.GuildBankItems[idx];
            if (item == null || item.ObjIndex <= 0) continue;

            int rowY = BankListY + i * ItemRowH;

            if (idx == _selectedBankIdx)
                _drawArea.DrawRect(new Rect2(BankListX + 1, rowY, BankListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            string displayName = item.Name.Length > 13 ? item.Name[..13] : item.Name;
            font?.DrawText(_drawArea, BankListX + 3, rowY + 1, $"{displayName} x{item.Amount}", ListFg);
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
                _drawArea.DrawRect(new Rect2(InvListX + 1, rowY, InvListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            string equip = inv.Equipped ? "(E)" : "";
            string displayName = inv.Name.Length > 12 ? inv.Name[..12] : inv.Name;
            font?.DrawText(_drawArea, InvListX + 3, rowY + 1, $"{displayName} x{inv.Amount}{equip}", ListFg);
        }

        DrawItemPreview(font);

        // Permission indicator
        if (!_state.GuildBankCanObj)
            font?.DrawText(_drawArea, ContentW / 2, BankListY + BankListH + 8, "(Solo deposito -- sin permiso de retiro)", DisabledFg, center: true);
    }

    private void DrawItemPreview(AoFont? font)
    {
        if (_state == null || _data == null || _drawArea == null) return;

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

        _drawArea.DrawRect(new Rect2(PreviewX, PreviewY, PreviewSize, PreviewSize), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        _drawArea.DrawRect(new Rect2(PreviewX, PreviewY, PreviewSize, PreviewSize), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        if (grhIndex > 0)
            CharRenderer.DrawGrh(_drawArea, _data, grhIndex, 0, new Vector2(PreviewX, PreviewY));

        if (!string.IsNullOrEmpty(name))
            font?.DrawText(_drawArea, PreviewX + PreviewSize + 8, PreviewY + 10, name, new Color(1f, 0.9f, 0.5f));
    }

    private void OnDrawAreaInput(InputEvent @event)
    {
        if (_state == null || _tcp == null || _drawArea == null) return;

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
                _drawArea.AcceptEvent();
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
                _drawArea.AcceptEvent();
                return;
            }

            _drawArea.AcceptEvent();
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
                _drawArea.AcceptEvent();
                return;
            }

            if (mx >= InvListX && mx < InvListX + InvListW && my >= InvListY && my < InvListY + InvListH)
            {
                int maxVisible = InvListH / ItemRowH;
                if (mbScroll.ButtonIndex == MouseButton.WheelDown)
                    _invScrollOffset = Math.Min(_invScrollOffset + 1, Math.Max(0, _userSlotCount - maxVisible));
                else if (mbScroll.ButtonIndex == MouseButton.WheelUp)
                    _invScrollOffset = Math.Max(0, _invScrollOffset - 1);
                _drawArea.AcceptEvent();
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
                Text = "No podes depositar un objeto equipado.",
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

    // -- Inline gold input dialog --

    private LineEdit? _goldInput;
    private Label? _goldInputLabel;
    private TextureButton? _goldInputOk;
    private TextureButton? _goldInputCancel;
    private HBoxContainer? _goldInputRow;
    private bool _isDepositing;

    private void ShowGoldInputDialog(bool deposit)
    {
        _isDepositing = deposit;

        if (_goldInputRow == null)
        {
            _goldInputRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
            _goldInputRow.Alignment = BoxContainer.AlignmentMode.Center;

            _goldInputLabel = RpgTheme.CreateInfoLabel("", 11);
            _goldInputRow.AddChild(_goldInputLabel);

            _goldInput = RpgTheme.CreateRpgInput("0", 100);
            _goldInput.Text = "0";
            _goldInput.Alignment = HorizontalAlignment.Center;
            _goldInput.TextSubmitted += (_) => OnGoldInputOk();
            _goldInputRow.AddChild(_goldInput);

            _goldInputOk = RpgTheme.CreateRpgButton("OK", false, 11);
            _goldInputOk.CustomMinimumSize = new Vector2(50, 22);
            _goldInputOk.Pressed += OnGoldInputOk;
            _goldInputRow.AddChild(_goldInputOk);

            _goldInputCancel = RpgTheme.CreateRpgButton("X", false, 11);
            _goldInputCancel.CustomMinimumSize = new Vector2(30, 22);
            _goldInputCancel.Pressed += OnGoldInputCancel;
            _goldInputRow.AddChild(_goldInputCancel);

            // Add to the vbox (parent of _drawArea)
            var vbox = _drawArea?.GetParent();
            vbox?.AddChild(_goldInputRow);
        }

        _goldInputLabel!.Text = deposit ? "Depositar oro en clan:" : "Retirar oro del clan:";
        _goldInput!.Text = "0";
        _goldInputRow!.Visible = true;
        _goldInput.GrabFocus();
        _goldInput.SelectAll();
    }

    private void HideGoldInputDialog()
    {
        if (_goldInputRow != null) _goldInputRow.Visible = false;
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

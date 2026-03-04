using Godot;
using System;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;
using TierrasSagradasAO.Rendering;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmComerciar — NPC commerce panel.
/// Shows NPC items (buy) and player inventory (sell), with buy/sell/selectall buttons.
/// Layout converted from VB6 twips÷15 → pixels: 445×486.
/// </summary>
public partial class CommercePanel : Control
{
    // Panel dimensions (VB6 form = 445×486 px)
    private const int PanelW = 445;
    private const int PanelH = 486;

    // NPC item list area
    private const int NpcListX = 26, NpcListY = 133;
    private const int NpcListW = 169, NpcListH = 266;

    // User item list area
    private const int UserListX = 243, UserListY = 133;
    private const int UserListW = 168, UserListH = 266;

    // Item preview area
    private const int PreviewIconX = 19, PreviewIconY = 63;
    private const int PreviewNameX = 62, PreviewNameY = 60;
    private const int PriceLabelX = 155, PriceLabelY = 86;
    private const int AmountLabelX = 59, AmountLabelY = 85;
    private const int Stat1X = 278, Stat1Y = 80;
    private const int Stat2X = 278, Stat2Y = 96;

    // Buttons
    private const int BuyBtnX = 28, BuyBtnY = 412, BtnW = 166, BtnH = 23;
    private const int SellBtnX = 244, SellBtnY = 412;
    private const int SelectAllBtnX = 244, SelectAllBtnY = 443;
    private const int QtyInputX = 78, QtyInputY = 442, QtyInputW = 66, QtyInputH = 13;
    private const int CloseBtnX = 408, CloseBtnY = 0, CloseBtnW = 33, CloseBtnH = 33;

    // List item height
    private const int ItemRowH = 16;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Selection state
    private int _selectedNpcIdx = -1;   // index into NpcShopItems (0-based)
    private int _selectedUserIdx = -1;  // index into filtered user items (0-based)
    private int _npcScrollOffset;
    private int _userScrollOffset;

    // Filtered user inventory (non-empty slots)
    private int[] _userSlots = new int[25]; // indices into state.Inventory
    private int _userSlotCount;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 30;

    // Quantity input
    private LineEdit? _qtyInput;

    // Buttons (Godot controls for click handling)
    private Button? _buyBtn;
    private Button? _sellBtn;
    private Button? _selectAllBtn;
    private Button? _closeBtn;

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

        // Quantity LineEdit
        _qtyInput = new LineEdit();
        _qtyInput.Position = new Vector2(QtyInputX, QtyInputY);
        _qtyInput.Size = new Vector2(QtyInputW, QtyInputH);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        _qtyInput.FocusMode = FocusModeEnum.Click;
        _qtyInput.AddThemeColorOverride("font_color", Colors.White);
        _qtyInput.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_qtyInput);

        // Buy button
        _buyBtn = CreateButton("Comprar", BuyBtnX, BuyBtnY, BtnW, BtnH);
        _buyBtn.Pressed += OnBuyPressed;
        AddChild(_buyBtn);

        // Sell button
        _sellBtn = CreateButton("Vender", SellBtnX, SellBtnY, BtnW, BtnH);
        _sellBtn.Pressed += OnSellPressed;
        AddChild(_sellBtn);

        // SelectAll button
        _selectAllBtn = CreateButton("Todo", SelectAllBtnX, SelectAllBtnY, BtnW, BtnH);
        _selectAllBtn.Pressed += OnSelectAllPressed;
        AddChild(_selectAllBtn);

        // Close button (X)
        _closeBtn = CreateButton("X", CloseBtnX, CloseBtnY, CloseBtnW, CloseBtnH);
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

    public void OpenShop()
    {
        _selectedNpcIdx = -1;
        _selectedUserIdx = -1;
        _npcScrollOffset = 0;
        _userScrollOffset = 0;
        _qtyInput!.Text = "1";
        Visible = true;
    }

    public void CloseShop()
    {
        Visible = false;
        _selectedNpcIdx = -1;
        _selectedUserIdx = -1;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Rebuild filtered user inventory each frame (cheap: 25 items)
        _userSlotCount = 0;
        for (int i = 0; i < 25; i++)
        {
            if (_state.Inventory[i].ObjIndex > 0)
                _userSlots[_userSlotCount++] = i;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null || _data == null) return;

        // Background
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.08f, 0.06f, 0.12f, 0.96f));

        // Panel border
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.5f, 0.4f, 0.3f, 0.8f), false, 2f);

        var font = _data.Fonts?[1];

        // Title
        font?.DrawText(this, PanelW / 2, 8, "Comerciar", new Color(1f, 0.85f, 0.4f), center: true);

        // List headers
        font?.DrawText(this, NpcListX + NpcListW / 2, NpcListY - 14, "NPC", Colors.White, center: true);
        font?.DrawText(this, UserListX + UserListW / 2, UserListY - 14, "Inventario", Colors.White, center: true);

        // NPC list background
        DrawRect(new Rect2(NpcListX, NpcListY, NpcListW, NpcListH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(NpcListX, NpcListY, NpcListW, NpcListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // User list background
        DrawRect(new Rect2(UserListX, UserListY, UserListW, UserListH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(UserListX, UserListY, UserListW, UserListH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw NPC items
        int maxNpcVisible = NpcListH / ItemRowH;
        for (int i = 0; i < maxNpcVisible && (i + _npcScrollOffset) < _state.NpcShopCount; i++)
        {
            int idx = i + _npcScrollOffset;
            var item = _state.NpcShopItems[idx];
            if (string.IsNullOrEmpty(item.Name)) continue;

            int rowY = NpcListY + i * ItemRowH;

            // Selection highlight
            if (idx == _selectedNpcIdx)
                DrawRect(new Rect2(NpcListX + 1, rowY, NpcListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            // Item name (truncate to fit)
            string displayName = item.Name.Length > 16 ? item.Name[..16] : item.Name;
            font?.DrawText(this, NpcListX + 3, rowY + 1, displayName, Colors.White);
        }

        // Draw User items
        int maxUserVisible = UserListH / ItemRowH;
        for (int i = 0; i < maxUserVisible && (i + _userScrollOffset) < _userSlotCount; i++)
        {
            int idx = i + _userScrollOffset;
            int slotIdx = _userSlots[idx];
            var inv = _state.Inventory[slotIdx];

            int rowY = UserListY + i * ItemRowH;

            // Selection highlight
            if (idx == _selectedUserIdx)
                DrawRect(new Rect2(UserListX + 1, rowY, UserListW - 2, ItemRowH), new Color(0.3f, 0.3f, 0.6f, 0.7f));

            // Item name + amount
            string equip = inv.Equipped ? "(E)" : "";
            string displayName = inv.Name.Length > 12 ? inv.Name[..12] : inv.Name;
            font?.DrawText(this, UserListX + 3, rowY + 1, $"{displayName} x{inv.Amount}{equip}", Colors.White);
        }

        // Preview area — show selected item info
        DrawPreview(font);

        // Labels for buttons area
        font?.DrawText(this, 28, QtyInputY + 1, "Cant:", Colors.White);
    }

    private void DrawPreview(AoFont? font)
    {
        if (_state == null || _data == null) return;

        // Determine which item to preview
        string name = "";
        long price = 0;
        int amount = 0;
        int grhIndex = 0;
        int objType = 0;
        int minHit = 0, maxHit = 0, maxDef = 0;

        if (_selectedNpcIdx >= 0 && _selectedNpcIdx < _state.NpcShopCount)
        {
            var item = _state.NpcShopItems[_selectedNpcIdx];
            name = item.Name;
            price = item.Price;
            amount = item.Amount;
            grhIndex = item.GrhIndex;
            objType = item.ObjType;
            minHit = item.MinHit;
            maxHit = item.MaxHit;
            maxDef = item.MaxDef;
        }
        else if (_selectedUserIdx >= 0 && _selectedUserIdx < _userSlotCount)
        {
            int slotIdx = _userSlots[_selectedUserIdx];
            var inv = _state.Inventory[slotIdx];
            name = inv.Name;
            price = inv.Value;
            amount = inv.Amount;
            grhIndex = inv.GrhIndex;
            objType = inv.ObjType;
            minHit = inv.MinHit;
            maxHit = inv.MaxHit;
            maxDef = inv.MaxDef;
        }

        if (string.IsNullOrEmpty(name)) return;

        // Preview border
        DrawRect(new Rect2(14, 50, 270, 60), new Color(0.12f, 0.1f, 0.15f, 0.9f));
        DrawRect(new Rect2(14, 50, 270, 60), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        // Item icon
        if (grhIndex > 0)
            CharRenderer.DrawGrh(this, _data, grhIndex, 0, new Vector2(PreviewIconX, PreviewIconY));

        // Item name
        font?.DrawText(this, PreviewNameX, PreviewNameY, name, new Color(1f, 0.9f, 0.5f));

        // Price
        font?.DrawText(this, PriceLabelX, PriceLabelY, $"${price}", new Color(1f, 1f, 0f));

        // Amount
        font?.DrawText(this, AmountLabelX, AmountLabelY, $"x{amount}", Colors.White);

        // Stats (type 2=weapon, 3=armor, 16=shield, 17=helmet)
        if (objType == 2)
        {
            font?.DrawText(this, Stat1X, Stat1Y, $"Daño: {minHit}/{maxHit}", Colors.White);
        }
        else if (objType == 3 || objType == 16 || objType == 17)
        {
            font?.DrawText(this, Stat1X, Stat1Y, $"Defensa: {maxDef}", Colors.White);
        }
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
                {
                    _dragging = false;
                }
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

            // Click in NPC list
            if (mx >= NpcListX && mx < NpcListX + NpcListW && my >= NpcListY && my < NpcListY + NpcListH)
            {
                int row = (int)(my - NpcListY) / ItemRowH;
                int idx = row + _npcScrollOffset;
                if (idx >= 0 && idx < _state.NpcShopCount)
                {
                    _selectedNpcIdx = idx;
                    _selectedUserIdx = -1; // deselect user
                }
                AcceptEvent();
                return;
            }

            // Click in User list
            if (mx >= UserListX && mx < UserListX + UserListW && my >= UserListY && my < UserListY + UserListH)
            {
                int row = (int)(my - UserListY) / ItemRowH;
                int idx = row + _userScrollOffset;
                if (idx >= 0 && idx < _userSlotCount)
                {
                    _selectedUserIdx = idx;
                    _selectedNpcIdx = -1; // deselect NPC
                }
                AcceptEvent();
                return;
            }

            // Right-click anywhere → close
            if (mb.ButtonIndex == MouseButton.Right)
            {
                OnClosePressed();
                AcceptEvent();
                return;
            }

            AcceptEvent();
        }
        else if (@event is InputEventMouseButton mbScroll)
        {
            float mx = mbScroll.Position.X;
            float my = mbScroll.Position.Y;

            // Scroll NPC list
            if (mx >= NpcListX && mx < NpcListX + NpcListW && my >= NpcListY && my < NpcListY + NpcListH)
            {
                int maxVisible = NpcListH / ItemRowH;
                if (mbScroll.ButtonIndex == MouseButton.WheelDown)
                    _npcScrollOffset = Math.Min(_npcScrollOffset + 1, Math.Max(0, _state.NpcShopCount - maxVisible));
                else if (mbScroll.ButtonIndex == MouseButton.WheelUp)
                    _npcScrollOffset = Math.Max(0, _npcScrollOffset - 1);
                AcceptEvent();
                return;
            }

            // Scroll User list
            if (mx >= UserListX && mx < UserListX + UserListW && my >= UserListY && my < UserListY + UserListH)
            {
                int maxVisible = UserListH / ItemRowH;
                if (mbScroll.ButtonIndex == MouseButton.WheelDown)
                    _userScrollOffset = Math.Min(_userScrollOffset + 1, Math.Max(0, _userSlotCount - maxVisible));
                else if (mbScroll.ButtonIndex == MouseButton.WheelUp)
                    _userScrollOffset = Math.Max(0, _userScrollOffset - 1);
                AcceptEvent();
                return;
            }
        }
    }

    private void OnBuyPressed()
    {
        GD.Print($"[COMMERCE] Buy pressed: state={_state != null} tcp={_tcp != null} selNpc={_selectedNpcIdx} shopCount={_state?.NpcShopCount ?? 0}");
        if (_state == null || _tcp == null) return;
        if (_selectedNpcIdx < 0 || _selectedNpcIdx >= _state.NpcShopCount) return;

        int qty = GetQuantity();
        if (qty <= 0) return;

        var item = _state.NpcShopItems[_selectedNpcIdx];
        long totalCost = item.Price * qty;
        GD.Print($"[COMMERCE] Buying: '{item.Name}' slot={item.Slot} qty={qty} unitPrice={item.Price} total={totalCost} gold={_state.Gold}");

        if (_state.Gold < totalCost)
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = "No tienes suficiente oro.", Color = "FF0000" });
            return;
        }

        if (qty > item.Amount && item.Amount > 0)
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = "El NPC no tiene esa cantidad.", Color = "FF0000" });
            return;
        }

        // VB6: SendData "COMP," & slot & "," & qty
        GD.Print($"[COMMERCE] Sending: COMP,{item.Slot},{qty}");
        _tcp.SendPacket(ClientPackets.WriteCommerceBuy((byte)item.Slot, (short)qty));
    }

    private void OnSellPressed()
    {
        GD.Print($"[COMMERCE] Sell pressed: state={_state != null} tcp={_tcp != null} selUser={_selectedUserIdx} userSlotCount={_userSlotCount}");
        if (_state == null || _tcp == null) return;
        if (_selectedUserIdx < 0 || _selectedUserIdx >= _userSlotCount) return;

        int slotIdx = _userSlots[_selectedUserIdx];
        var inv = _state.Inventory[slotIdx];

        if (inv.Equipped)
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = "No puedes vender un objeto equipado.", Color = "FF0000" });
            return;
        }

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > inv.Amount) qty = inv.Amount;

        // VB6: SendData "VEND," & (slot+1) & "," & qty (1-indexed)
        GD.Print($"[COMMERCE] Sending: VEND,{slotIdx + 1},{qty}");
        _tcp.SendPacket(ClientPackets.WriteCommerceSell((byte)(slotIdx + 1), (short)qty));
    }

    private void OnSelectAllPressed()
    {
        if (_qtyInput == null) return;

        if (_selectedNpcIdx >= 0 && _selectedNpcIdx < (_state?.NpcShopCount ?? 0))
        {
            _qtyInput.Text = _state!.NpcShopItems[_selectedNpcIdx].Amount.ToString();
        }
        else if (_selectedUserIdx >= 0 && _selectedUserIdx < _userSlotCount)
        {
            int slotIdx = _userSlots[_selectedUserIdx];
            _qtyInput.Text = _state!.Inventory[slotIdx].Amount.ToString();
        }
    }

    private void OnClosePressed()
    {
        _tcp?.SendPacket(ClientPackets.WriteCommerceClose());
    }

    private int GetQuantity()
    {
        if (_qtyInput == null) return 1;
        return int.TryParse(_qtyInput.Text.Trim(), out int v) && v > 0 ? v : 1;
    }
}

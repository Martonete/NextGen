using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// NPC commerce panel — two icon grids (NPC items / player inventory)
/// with buy/sell buttons, quantity input, and item preview.
/// </summary>
public partial class CommercePanel : Control
{
    // Panel dimensions
    private const int PanelW = 445;
    private const int PanelH = 486;

    // Grid layout — 5 columns, 34×34 cells
    private const int GridCols = 5;
    private const int CellSize = 34;

    // NPC grid area (left side)
    private const int NpcGridX = 14, NpcGridY = 120;
    private const int NpcGridW = GridCols * CellSize; // 170
    private const int NpcGridRows = 7; // visible rows
    private const int NpcGridH = NpcGridRows * CellSize; // 238

    // User grid area (right side)
    private const int UserGridX = 243, UserGridY = 120;
    private const int UserGridW = GridCols * CellSize;
    private const int UserGridRows = 7;
    private const int UserGridH = UserGridRows * CellSize;

    // Item preview area
    private const int PreviewIconX = 19, PreviewIconY = 50;
    private const int PreviewNameX = 62, PreviewNameY = 48;
    private const int PriceLabelX = 62, PriceLabelY = 64;
    private const int AmountLabelX = 62, AmountLabelY = 80;
    private const int Stat1X = 62, Stat1Y = 96;

    // Buttons
    private const int BuyBtnX = 14, BuyBtnY = 370, BtnW = 170, BtnH = 23;
    private const int SellBtnX = 243, SellBtnY = 370;
    private const int SelectAllBtnX = 243, SelectAllBtnY = 443;
    private const int QtyInputX = 78, QtyInputY = 442, QtyInputW = 66, QtyInputH = 15;
    private const int CloseBtnX = 408, CloseBtnY = 0, CloseBtnW = 33, CloseBtnH = 33;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Selection state
    private int _selectedNpcIdx = -1;
    private int _selectedUserIdx = -1;
    private int _npcScrollRow;
    private int _userScrollRow;

    // Filtered user inventory (non-empty slots)
    private int[] _userSlots = new int[25];
    private int _userSlotCount;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 30;

    // UI controls
    private LineEdit? _qtyInput;
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

        _qtyInput = new LineEdit();
        _qtyInput.Position = new Vector2(QtyInputX, QtyInputY);
        _qtyInput.Size = new Vector2(QtyInputW, QtyInputH);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        _qtyInput.FocusMode = FocusModeEnum.Click;
        _qtyInput.AddThemeColorOverride("font_color", Colors.White);
        _qtyInput.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_qtyInput);

        _buyBtn = CreateButton("Comprar", BuyBtnX, BuyBtnY, BtnW, BtnH);
        _buyBtn.Pressed += OnBuyPressed;
        AddChild(_buyBtn);

        _sellBtn = CreateButton("Vender", SellBtnX, SellBtnY, BtnW, BtnH);
        _sellBtn.Pressed += OnSellPressed;
        AddChild(_sellBtn);

        _selectAllBtn = CreateButton("Todo", SelectAllBtnX, SelectAllBtnY, BtnW, BtnH);
        _selectAllBtn.Pressed += OnSelectAllPressed;
        AddChild(_selectAllBtn);

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
        _npcScrollRow = 0;
        _userScrollRow = 0;
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
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.5f, 0.4f, 0.3f, 0.8f), false, 2f);

        var font = _data.Fonts?[1];

        // Title
        font?.DrawText(this, PanelW / 2, 8, "Comerciar", new Color(1f, 0.85f, 0.4f), center: true);

        // Grid headers
        font?.DrawText(this, NpcGridX + NpcGridW / 2, NpcGridY - 14, "NPC", Colors.White, center: true);
        font?.DrawText(this, UserGridX + UserGridW / 2, UserGridY - 14, "Inventario", Colors.White, center: true);

        // NPC grid background
        DrawRect(new Rect2(NpcGridX, NpcGridY, NpcGridW, NpcGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(NpcGridX, NpcGridY, NpcGridW, NpcGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // User grid background
        DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw NPC items as icon grid
        int npcStartIdx = _npcScrollRow * GridCols;
        int maxNpcCells = NpcGridRows * GridCols;
        for (int i = 0; i < maxNpcCells; i++)
        {
            int idx = npcStartIdx + i;
            if (idx >= _state.NpcShopCount) break;

            var item = _state.NpcShopItems[idx];
            if (string.IsNullOrEmpty(item.Name)) continue;

            int col = i % GridCols;
            int row = i / GridCols;
            float cx = NpcGridX + col * CellSize;
            float cy = NpcGridY + row * CellSize;

            // Selection highlight
            if (idx == _selectedNpcIdx)
            {
                DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));
            }

            // Item icon
            if (item.GrhIndex > 0)
                CharRenderer.DrawGrh(this, _data, item.GrhIndex, 0, new Vector2(cx + 1, cy));

            // Amount overlay
            if (item.Amount > 0 && font != null)
                font.DrawText(this, (int)cx, (int)cy + 3, item.Amount.ToString(), Colors.White);

            // Cell border
            DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(0.4f, 0.4f, 0.5f, 0.4f), false, 1f);
        }

        // Draw User items as icon grid
        int userStartIdx = _userScrollRow * GridCols;
        int maxUserCells = UserGridRows * GridCols;
        for (int i = 0; i < maxUserCells; i++)
        {
            int idx = userStartIdx + i;
            if (idx >= _userSlotCount) break;

            int slotIdx = _userSlots[idx];
            var inv = _state.Inventory[slotIdx];

            int col = i % GridCols;
            int row = i / GridCols;
            float cx = UserGridX + col * CellSize;
            float cy = UserGridY + row * CellSize;

            // Selection highlight
            if (idx == _selectedUserIdx)
            {
                DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));
            }

            // Item icon
            if (inv.GrhIndex > 0)
                CharRenderer.DrawGrh(this, _data, inv.GrhIndex, 0, new Vector2(cx + 1, cy));

            // Amount overlay
            if (inv.Amount > 0 && font != null)
                font.DrawText(this, (int)cx, (int)cy + 3, inv.Amount.ToString(), Colors.White);

            // Equipped marker
            if (inv.Equipped && font != null)
                font.DrawText(this, (int)cx + 23, (int)cy + 20, "E", new Color(1f, 1f, 0f));

            // Cell border
            DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(0.4f, 0.4f, 0.5f, 0.4f), false, 1f);
        }

        // Preview area
        DrawPreview(font);

        // Labels
        font?.DrawText(this, 14, QtyInputY + 1, "Cant:", Colors.White);
    }

    private void DrawPreview(AoFont? font)
    {
        if (_state == null || _data == null) return;

        string name = "";
        long price = 0;
        int amount = 0;
        int grhIndex = 0;
        int objType = 0;
        int minHit = 0, maxHit = 0, maxDef = 0;

        if (_selectedNpcIdx >= 0 && _selectedNpcIdx < _state.NpcShopCount)
        {
            var item = _state.NpcShopItems[_selectedNpcIdx];
            name = item.Name; price = item.Price; amount = item.Amount;
            grhIndex = item.GrhIndex; objType = item.ObjType;
            minHit = item.MinHit; maxHit = item.MaxHit; maxDef = item.MaxDef;
        }
        else if (_selectedUserIdx >= 0 && _selectedUserIdx < _userSlotCount)
        {
            int slotIdx = _userSlots[_selectedUserIdx];
            var inv = _state.Inventory[slotIdx];
            name = inv.Name; price = inv.Value; amount = inv.Amount;
            grhIndex = inv.GrhIndex; objType = inv.ObjType;
            minHit = inv.MinHit; maxHit = inv.MaxHit; maxDef = inv.MaxDef;
        }

        if (string.IsNullOrEmpty(name)) return;

        // Preview border
        DrawRect(new Rect2(14, 38, PanelW - 28, 66), new Color(0.12f, 0.1f, 0.15f, 0.9f));
        DrawRect(new Rect2(14, 38, PanelW - 28, 66), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        if (grhIndex > 0)
            CharRenderer.DrawGrh(this, _data, grhIndex, 0, new Vector2(PreviewIconX, PreviewIconY));

        font?.DrawText(this, PreviewNameX, PreviewNameY, name, new Color(1f, 0.9f, 0.5f));
        font?.DrawText(this, PriceLabelX, PriceLabelY, $"Precio: {price}", new Color(1f, 1f, 0f));
        font?.DrawText(this, AmountLabelX, AmountLabelY, $"x{amount}", Colors.White);

        if (objType == 2)
            font?.DrawText(this, Stat1X + 120, Stat1Y - 32, $"Daño: {minHit}/{maxHit}", Colors.White);
        else if (objType == 3 || objType == 16 || objType == 17)
            font?.DrawText(this, Stat1X + 120, Stat1Y - 32, $"Def: {maxDef}", Colors.White);
    }

    private int HitTestGrid(Vector2 pos, int gridX, int gridY, int gridW, int gridH, int scrollRow, int itemCount)
    {
        float lx = pos.X - gridX;
        float ly = pos.Y - gridY;
        if (lx < 0 || ly < 0 || lx >= gridW || ly >= gridH) return -1;
        int col = (int)(lx / CellSize);
        int row = (int)(ly / CellSize);
        int idx = (scrollRow + row) * GridCols + col;
        return idx < itemCount ? idx : -1;
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
            // Click in NPC grid
            int npcIdx = HitTestGrid(mb.Position, NpcGridX, NpcGridY, NpcGridW, NpcGridH, _npcScrollRow, _state.NpcShopCount);
            if (npcIdx >= 0)
            {
                _selectedNpcIdx = npcIdx;
                _selectedUserIdx = -1;
                AcceptEvent();
                return;
            }

            // Click in User grid
            int userIdx = HitTestGrid(mb.Position, UserGridX, UserGridY, UserGridW, UserGridH, _userScrollRow, _userSlotCount);
            if (userIdx >= 0)
            {
                _selectedUserIdx = userIdx;
                _selectedNpcIdx = -1;
                AcceptEvent();
                return;
            }

            // Scroll NPC grid
            if (mb.Position.X >= NpcGridX && mb.Position.X < NpcGridX + NpcGridW &&
                mb.Position.Y >= NpcGridY && mb.Position.Y < NpcGridY + NpcGridH)
            {
                HandleScroll(mb, ref _npcScrollRow, _state.NpcShopCount, NpcGridRows);
                AcceptEvent();
                return;
            }

            // Scroll User grid
            if (mb.Position.X >= UserGridX && mb.Position.X < UserGridX + UserGridW &&
                mb.Position.Y >= UserGridY && mb.Position.Y < UserGridY + UserGridH)
            {
                HandleScroll(mb, ref _userScrollRow, _userSlotCount, UserGridRows);
                AcceptEvent();
                return;
            }

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
            // Scroll NPC grid
            if (mbScroll.Position.X >= NpcGridX && mbScroll.Position.X < NpcGridX + NpcGridW &&
                mbScroll.Position.Y >= NpcGridY && mbScroll.Position.Y < NpcGridY + NpcGridH)
            {
                HandleScroll(mbScroll, ref _npcScrollRow, _state.NpcShopCount, NpcGridRows);
                AcceptEvent();
                return;
            }

            // Scroll User grid
            if (mbScroll.Position.X >= UserGridX && mbScroll.Position.X < UserGridX + UserGridW &&
                mbScroll.Position.Y >= UserGridY && mbScroll.Position.Y < UserGridY + UserGridH)
            {
                HandleScroll(mbScroll, ref _userScrollRow, _userSlotCount, UserGridRows);
                AcceptEvent();
                return;
            }
        }
    }

    private static void HandleScroll(InputEventMouseButton mb, ref int scrollRow, int itemCount, int visibleRows)
    {
        int totalRows = (itemCount + GridCols - 1) / GridCols;
        int maxScroll = Math.Max(0, totalRows - visibleRows);
        if (mb.ButtonIndex == MouseButton.WheelDown)
            scrollRow = Math.Min(scrollRow + 1, maxScroll);
        else if (mb.ButtonIndex == MouseButton.WheelUp)
            scrollRow = Math.Max(0, scrollRow - 1);
    }

    private void OnBuyPressed()
    {
        if (_state == null || _tcp == null) return;
        if (_selectedNpcIdx < 0 || _selectedNpcIdx >= _state.NpcShopCount) return;

        int qty = GetQuantity();
        if (qty <= 0) return;

        var item = _state.NpcShopItems[_selectedNpcIdx];
        if (qty > item.Amount && item.Amount > 0)
            qty = item.Amount;

        long totalCost = item.Price * qty;
        if (_state.Gold < totalCost)
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = "No tienes suficiente oro.", Color = "FF0000" });
            return;
        }

        _tcp.SendPacket(ClientPackets.WriteCommerceBuy((byte)item.Slot, (short)qty));
    }

    private void OnSellPressed()
    {
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

        _tcp.SendPacket(ClientPackets.WriteCommerceSell((byte)(slotIdx + 1), (short)qty));
    }

    private void OnSelectAllPressed()
    {
        if (_qtyInput == null) return;
        if (_selectedNpcIdx >= 0 && _selectedNpcIdx < (_state?.NpcShopCount ?? 0))
            _qtyInput.Text = _state!.NpcShopItems[_selectedNpcIdx].Amount.ToString();
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

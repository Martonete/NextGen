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
/// Now uses RpgBaseForm for consistent RPG chrome (frame, title, close, drag).
/// Custom _Draw() rendering for item grids is preserved inside a child Control.
/// </summary>
public partial class CommercePanel : RpgBaseForm
{
    // Content area dimensions (inside the form chrome)
    private const int ContentW = 385;
    private const int ContentH = 400;

    // Grid layout — 5 columns, 34x34 cells
    private const int GridCols = 5;
    private const int CellSize = 34;

    // NPC grid area (left side) — relative to _drawArea origin
    private const int NpcGridX = 0, NpcGridY = 82;
    private const int NpcGridW = GridCols * CellSize; // 170
    private const int NpcGridRows = 7;
    private const int NpcGridH = NpcGridRows * CellSize; // 238

    // User grid area (right side) — relative to _drawArea origin
    private const int UserGridX = 215, UserGridY = 82;
    private const int UserGridW = GridCols * CellSize;
    private const int UserGridRows = 7;
    private const int UserGridH = UserGridRows * CellSize;

    // Item preview area — relative to _drawArea origin
    private const int PreviewIconX = 5, PreviewIconY = 12;
    private const int PreviewNameX = 48, PreviewNameY = 10;
    private const int PriceLabelX = 48, PriceLabelY = 26;
    private const int AmountLabelX = 48, AmountLabelY = 42;
    private const int Stat1X = 48, Stat1Y = 58;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Selection state
    private int _selectedNpcIdx = -1;
    private int _selectedUserIdx = -1;
    private int _npcScrollRow;
    private int _userScrollRow;

    // Filtered user inventory (non-empty slots)
    private int[] _userSlots = new int[GameState.MaxInventoryCapacity];
    private int _userSlotCount;
    private bool _dirty = true;

    // UI controls
    private Control? _drawArea;
    private LineEdit? _qtyInput;
    private TextureButton? _buyBtn;
    private TextureButton? _sellBtn;
    private TextureButton? _selectAllBtn;

    // Rich tooltip panel (set by Main.cs)
    public TooltipPanel? RichTooltip;

    public CommercePanel() : base("Comerciar", new Vector2(445, 486), "v3") { }

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

        // Custom draw area for grids, preview, and labels
        _drawArea = new Control();
        _drawArea.CustomMinimumSize = new Vector2(ContentW, 330);
        _drawArea.MouseFilter = MouseFilterEnum.Stop;
        _drawArea.Draw += OnDrawArea;
        _drawArea.GuiInput += OnDrawAreaInput;
        vbox.AddChild(_drawArea);

        // Grid headers (drawn labels)
        // NPC / Inventario headers are drawn in OnDrawArea

        // Button row: Buy / Sell
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _buyBtn = RpgTheme.CreateRpgButton("Comprar", false, 14);
        _buyBtn.CustomMinimumSize = new Vector2(170, 28);
        _buyBtn.Pressed += OnBuyPressed;
        btnRow.AddChild(_buyBtn);

        _sellBtn = RpgTheme.CreateRpgButton("Vender", false, 14);
        _sellBtn.CustomMinimumSize = new Vector2(170, 28);
        _sellBtn.Pressed += OnSellPressed;
        btnRow.AddChild(_sellBtn);

        // Quantity row
        var qtyRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        qtyRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(qtyRow);

        var qtyLabel = RpgTheme.CreateInfoLabel("Cant:", 12);
        qtyRow.AddChild(qtyLabel);

        _qtyInput = RpgTheme.CreateRpgInput("1", 66);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        qtyRow.AddChild(_qtyInput);

        _selectAllBtn = RpgTheme.CreateRpgButton("Todo", false, 12);
        _selectAllBtn.CustomMinimumSize = new Vector2(80, 24);
        _selectAllBtn.Pressed += OnSelectAllPressed;
        qtyRow.AddChild(_selectAllBtn);
    }

    public void OpenShop()
    {
        _dirty = true;
        _selectedNpcIdx = -1;
        _selectedUserIdx = -1;
        _npcScrollRow = 0;
        _userScrollRow = 0;
        _qtyInput!.Text = "1";
        ShowForm();
    }

    public override void HideForm()
    {
        // Send close packet so the server knows commerce ended
        if (_state != null && _state.Comerciando)
        {
            _tcp?.SendPacket(ClientPackets.WriteCommerceClose());
            _state.Comerciando = false;
        }
        base.HideForm();
    }

    public void CloseShop()
    {
        HideForm();
        _selectedNpcIdx = -1;
        _selectedUserIdx = -1;
        RichTooltip?.Hide();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
            RichTooltip?.Hide();
    }

    private void RebuildUserSlots()
    {
        _userSlotCount = 0;
        for (int i = 0; i < _state!.MaxInventorySlots; i++)
        {
            if (_state!.Inventory[i].ObjIndex > 0)
                _userSlots[_userSlotCount++] = i;
        }
    }

    public void MarkDirty()
    {
        _dirty = true;
        _drawArea?.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null || !_dirty) return;
        _dirty = false;
        RebuildUserSlots();
        _drawArea?.QueueRedraw();
    }

    private void OnDrawArea()
    {
        if (_state == null || _data == null || _drawArea == null) return;

        var font = _data.Fonts?[1];

        // Grid headers
        font?.DrawText(_drawArea, NpcGridX + NpcGridW / 2, NpcGridY - 14, "NPC", Colors.White, center: true);
        font?.DrawText(_drawArea, UserGridX + UserGridW / 2, UserGridY - 14, "Inventario", Colors.White, center: true);

        // NPC grid background
        _drawArea.DrawRect(new Rect2(NpcGridX, NpcGridY, NpcGridW, NpcGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        _drawArea.DrawRect(new Rect2(NpcGridX, NpcGridY, NpcGridW, NpcGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // User grid background
        _drawArea.DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        _drawArea.DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw NPC items as icon grid
        int npcStartIdx = _npcScrollRow * GridCols;
        int maxNpcCells = NpcGridRows * GridCols;
        for (int i = 0; i < maxNpcCells; i++)
        {
            int idx = npcStartIdx + i;
            if (idx >= _state!.NpcShopCount) break;

            var item = _state.NpcShopItems[idx];
            if (string.IsNullOrEmpty(item.Name)) continue;

            int col = i % GridCols;
            int row = i / GridCols;
            float cx = NpcGridX + col * CellSize;
            float cy = NpcGridY + row * CellSize;

            // Selection highlight
            if (idx == _selectedNpcIdx)
            {
                _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));
            }

            // Item icon
            if (item.GrhIndex > 0)
                CharRenderer.DrawGrh(_drawArea, _data, item.GrhIndex, 0, new Vector2(cx + 1, cy));

            // Amount overlay
            if (item.Amount > 0 && font != null)
                font.DrawText(_drawArea, (int)cx, (int)cy + 3, item.Amount.ToString(), Colors.White);

            // Cell border
            _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(0.4f, 0.4f, 0.5f, 0.4f), false, 1f);
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
                _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));
            }

            // Item icon
            if (inv.GrhIndex > 0)
                CharRenderer.DrawGrh(_drawArea, _data, inv.GrhIndex, 0, new Vector2(cx + 1, cy));

            // Amount overlay
            if (inv.Amount > 0 && font != null)
                font.DrawText(_drawArea, (int)cx, (int)cy + 3, inv.Amount.ToString(), Colors.White);

            // Equipped marker
            if (inv.Equipped && font != null)
                font.DrawText(_drawArea, (int)cx + 23, (int)cy + 20, "E", new Color(1f, 1f, 0f));

            // Cell border
            _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(0.4f, 0.4f, 0.5f, 0.4f), false, 1f);
        }

        // Preview area
        DrawPreview(font);
    }

    private void DrawPreview(AoFont? font)
    {
        if (_state == null || _data == null || _drawArea == null) return;

        string name = "";
        long price = 0;
        int amount = 0;
        int grhIndex = 0;
        int objType = 0;
        int minHit = 0, maxHit = 0, maxDef = 0;

        if (_selectedNpcIdx >= 0 && _selectedNpcIdx < _state!.NpcShopCount)
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
        _drawArea.DrawRect(new Rect2(0, 0, ContentW, 68), new Color(0.12f, 0.1f, 0.15f, 0.9f));
        _drawArea.DrawRect(new Rect2(0, 0, ContentW, 68), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        if (grhIndex > 0)
            CharRenderer.DrawGrh(_drawArea, _data, grhIndex, 0, new Vector2(PreviewIconX, PreviewIconY));

        font?.DrawText(_drawArea, PreviewNameX, PreviewNameY, name, new Color(1f, 0.9f, 0.5f));
        font?.DrawText(_drawArea, PriceLabelX, PriceLabelY, $"Precio: {price}", new Color(1f, 1f, 0f));
        font?.DrawText(_drawArea, AmountLabelX, AmountLabelY, $"x{amount}", Colors.White);

        if (objType == 2)
            font?.DrawText(_drawArea, Stat1X + 120, Stat1Y - 32, $"Dano: {minHit}/{maxHit}", Colors.White);
        else if (objType == 3 || objType == 16 || objType == 17)
            font?.DrawText(_drawArea, Stat1X + 120, Stat1Y - 32, $"Def: {maxDef}", Colors.White);
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

    private void OnDrawAreaInput(InputEvent @event)
    {
        if (_state == null || _tcp == null) return;

        if (@event is InputEventMouseMotion mm)
        {
            // Tooltip on hover over grid items
            if (RichTooltip != null && _state != null)
            {
                int npcIdx = HitTestGrid(mm.Position, NpcGridX, NpcGridY, NpcGridW, NpcGridH, _npcScrollRow, _state!.NpcShopCount);
                int userIdx = HitTestGrid(mm.Position, UserGridX, UserGridY, UserGridW, UserGridH, _userScrollRow, _userSlotCount);

                if (npcIdx >= 0 && npcIdx < _state!.NpcShopCount)
                    RichTooltip.ShowNpcShopItem(_state.NpcShopItems[npcIdx]);
                else if (userIdx >= 0 && userIdx < _userSlotCount)
                    RichTooltip.ShowInventoryItem(_state.Inventory[_userSlots[userIdx]]);
                else
                    RichTooltip.Hide();
            }
        }

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            // Click in NPC grid
            int npcIdx = HitTestGrid(mb.Position, NpcGridX, NpcGridY, NpcGridW, NpcGridH, _npcScrollRow, _state!.NpcShopCount);
            if (npcIdx >= 0)
            {
                _selectedNpcIdx = npcIdx;
                _selectedUserIdx = -1;
                _drawArea!.AcceptEvent();
                return;
            }

            // Click in User grid
            int userIdx = HitTestGrid(mb.Position, UserGridX, UserGridY, UserGridW, UserGridH, _userScrollRow, _userSlotCount);
            if (userIdx >= 0)
            {
                _selectedUserIdx = userIdx;
                _selectedNpcIdx = -1;
                _drawArea!.AcceptEvent();
                return;
            }

            // Scroll NPC grid
            if (mb.Position.X >= NpcGridX && mb.Position.X < NpcGridX + NpcGridW &&
                mb.Position.Y >= NpcGridY && mb.Position.Y < NpcGridY + NpcGridH)
            {
                HandleScroll(mb, ref _npcScrollRow, _state!.NpcShopCount, NpcGridRows);
                _drawArea!.AcceptEvent();
                return;
            }

            // Scroll User grid
            if (mb.Position.X >= UserGridX && mb.Position.X < UserGridX + UserGridW &&
                mb.Position.Y >= UserGridY && mb.Position.Y < UserGridY + UserGridH)
            {
                HandleScroll(mb, ref _userScrollRow, _userSlotCount, UserGridRows);
                _drawArea!.AcceptEvent();
                return;
            }

            if (mb.ButtonIndex == MouseButton.Right)
            {
                OnClosePressed();
                _drawArea!.AcceptEvent();
                return;
            }

            _drawArea!.AcceptEvent();
        }
        else if (@event is InputEventMouseButton mbScroll)
        {
            // Scroll NPC grid
            if (mbScroll.Position.X >= NpcGridX && mbScroll.Position.X < NpcGridX + NpcGridW &&
                mbScroll.Position.Y >= NpcGridY && mbScroll.Position.Y < NpcGridY + NpcGridH)
            {
                HandleScroll(mbScroll, ref _npcScrollRow, _state!.NpcShopCount, NpcGridRows);
                _drawArea!.AcceptEvent();
                return;
            }

            // Scroll User grid
            if (mbScroll.Position.X >= UserGridX && mbScroll.Position.X < UserGridX + UserGridW &&
                mbScroll.Position.Y >= UserGridY && mbScroll.Position.Y < UserGridY + UserGridH)
            {
                HandleScroll(mbScroll, ref _userScrollRow, _userSlotCount, UserGridRows);
                _drawArea!.AcceptEvent();
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
        if (_selectedNpcIdx < 0 || _selectedNpcIdx >= _state!.NpcShopCount) return;

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

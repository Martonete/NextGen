using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmNuevoBancoObj — Item vault (bóveda) panel.
/// Two icon grids (vault items / player inventory) with withdraw/deposit buttons,
/// quantity input, gold displays, and item preview.
/// </summary>
public partial class VaultPanel : Control
{
    // Panel dimensions
    private const int PanelW = 450;
    private const int PanelH = 527;

    // Grid layout — 5 columns, 34×34 cells
    private const int GridCols = 5;
    private const int CellSize = 34;

    // Vault grid area (left side)
    private const int VaultGridX = 14, VaultGridY = 120;
    private const int VaultGridW = GridCols * CellSize; // 170
    private const int VaultGridRows = 7;
    private const int VaultGridH = VaultGridRows * CellSize; // 238

    // User grid area (right side)
    private const int UserGridX = 243, UserGridY = 120;
    private const int UserGridW = GridCols * CellSize;
    private const int UserGridRows = 7;
    private const int UserGridH = UserGridRows * CellSize;

    // Item preview area
    private const int PreviewIconX = 19, PreviewIconY = 50;
    private const int PreviewNameX = 62, PreviewNameY = 48;
    private const int AmountLabelX = 62, AmountLabelY = 64;

    // Buttons
    private const int RetirarBtnX = 14, RetirarBtnY = 370, BtnW = 170, BtnH = 23;
    private const int DepositarBtnX = 243, DepositarBtnY = 370;
    private const int QtyInputX = 78, QtyInputY = 400, QtyInputW = 66, QtyInputH = 15;

    // Gold displays
    private const int GoldDisplayX = 195, GoldDisplayW = 119;
    private const int VaultGoldY = 432, MyGoldY = 456;

    // Gold buttons
    private const int RetirarOroX = 321, DepositarOroX = 321;
    private const int GoldBtnW = 87, GoldBtnH = 15;

    // Salir
    private const int SalirBtnX = 176, SalirBtnY = 490, SalirBtnW = 98, SalirBtnH = 24;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Selection state
    private int _selectedVaultIdx = -1;
    private int _selectedInvIdx = -1;
    private int _vaultScrollRow;
    private int _invScrollRow;

    // Filtered user inventory (non-empty slots)
    private int[] _userSlots = new int[25];
    private int _userSlotCount;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 30;

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
        _qtyInput.Position = new Vector2(QtyInputX, QtyInputY);
        _qtyInput.Size = new Vector2(QtyInputW, QtyInputH);
        _qtyInput.Text = "1";
        _qtyInput.Alignment = HorizontalAlignment.Center;
        _qtyInput.FocusMode = FocusModeEnum.Click;
        _qtyInput.AddThemeColorOverride("font_color", Colors.White);
        _qtyInput.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_qtyInput);

        // Vault gold label
        _vaultGoldLabel = CreateLabel("0", GoldDisplayX, VaultGoldY, GoldDisplayW, 15);
        AddChild(_vaultGoldLabel);

        // My gold label
        _myGoldLabel = CreateLabel("0", GoldDisplayX, MyGoldY, GoldDisplayW, 15);
        AddChild(_myGoldLabel);

        // Retirar (withdraw item)
        _retirarBtn = CreateButton("Retirar", RetirarBtnX, RetirarBtnY, BtnW, BtnH);
        _retirarBtn.Pressed += OnRetirarPressed;
        AddChild(_retirarBtn);

        // Depositar (deposit item)
        _depositarBtn = CreateButton("Depositar", DepositarBtnX, DepositarBtnY, BtnW, BtnH);
        _depositarBtn.Pressed += OnDepositarPressed;
        AddChild(_depositarBtn);

        // Retirar Oro
        _retirarOroBtn = CreateButton("Retirar", RetirarOroX, VaultGoldY, GoldBtnW, GoldBtnH);
        _retirarOroBtn.Pressed += OnRetirarOroPressed;
        AddChild(_retirarOroBtn);

        // Depositar Oro
        _depositarOroBtn = CreateButton("Depositar", DepositarOroX, MyGoldY, GoldBtnW, GoldBtnH);
        _depositarOroBtn.Pressed += OnDepositarOroPressed;
        AddChild(_depositarOroBtn);

        // Salir
        _salirBtn = CreateButton("Salir", SalirBtnX, SalirBtnY, SalirBtnW, SalirBtnH);
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
        lbl.AddThemeColorOverride("font_color", new Color(145f / 255f, 123f / 255f, 85f / 255f));
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
        _vaultScrollRow = 0;
        _invScrollRow = 0;
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

        // Grid headers
        font?.DrawText(this, VaultGridX + VaultGridW / 2, VaultGridY - 14, "Bóveda", Colors.White, center: true);
        font?.DrawText(this, UserGridX + UserGridW / 2, UserGridY - 14, "Inventario", Colors.White, center: true);

        // Vault grid background
        DrawRect(new Rect2(VaultGridX, VaultGridY, VaultGridW, VaultGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(VaultGridX, VaultGridY, VaultGridW, VaultGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // User grid background
        DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw vault items as icon grid
        int vaultStartIdx = _vaultScrollRow * GridCols;
        int maxVaultCells = VaultGridRows * GridCols;
        for (int i = 0; i < maxVaultCells; i++)
        {
            int idx = vaultStartIdx + i;
            if (idx >= _state.BankItemCount) break;

            var item = _state.BankItems[idx];
            if (item.ObjIndex <= 0) continue;

            int col = i % GridCols;
            int row = i / GridCols;
            float cx = VaultGridX + col * CellSize;
            float cy = VaultGridY + row * CellSize;

            // Selection highlight
            if (idx == _selectedVaultIdx)
                DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));

            // Item icon
            if (item.GrhIndex > 0)
                CharRenderer.DrawGrh(this, _data, item.GrhIndex, 0, new Vector2(cx + 1, cy));

            // Amount overlay
            if (item.Amount > 0 && font != null)
                font.DrawText(this, (int)cx, (int)cy + 3, item.Amount.ToString(), Colors.White);

            // Cell border
            DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(0.4f, 0.4f, 0.5f, 0.4f), false, 1f);
        }

        // Draw user items as icon grid
        int userStartIdx = _invScrollRow * GridCols;
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
            if (idx == _selectedInvIdx)
                DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));

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
        DrawItemPreview(font);

        // Labels
        font?.DrawText(this, 14, QtyInputY + 1, "Cant:", Colors.White);
        font?.DrawText(this, 140, VaultGoldY + 1, "Oro Bóveda:", Colors.White);
        font?.DrawText(this, 155, MyGoldY + 1, "Mi Oro:", Colors.White);
    }

    private void DrawItemPreview(AoFont? font)
    {
        if (_state == null || _data == null) return;

        int grhIndex = 0;
        string name = "";
        int amount = 0;

        if (_selectedVaultIdx >= 0 && _selectedVaultIdx < _state.BankItemCount)
        {
            var item = _state.BankItems[_selectedVaultIdx];
            grhIndex = item.GrhIndex;
            name = item.Name;
            amount = item.Amount;
        }
        else if (_selectedInvIdx >= 0 && _selectedInvIdx < _userSlotCount)
        {
            int slotIdx = _userSlots[_selectedInvIdx];
            var inv = _state.Inventory[slotIdx];
            grhIndex = inv.GrhIndex;
            name = inv.Name;
            amount = inv.Amount;
        }

        if (string.IsNullOrEmpty(name)) return;

        // Preview border
        DrawRect(new Rect2(14, 38, PanelW - 28, 66), new Color(0.12f, 0.1f, 0.15f, 0.9f));
        DrawRect(new Rect2(14, 38, PanelW - 28, 66), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        if (grhIndex > 0)
            CharRenderer.DrawGrh(this, _data, grhIndex, 0, new Vector2(PreviewIconX, PreviewIconY));

        font?.DrawText(this, PreviewNameX, PreviewNameY, name, new Color(1f, 0.9f, 0.5f));
        font?.DrawText(this, AmountLabelX, AmountLabelY, $"x{amount}", Colors.White);
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
            // Click in vault grid
            int vaultIdx = HitTestGrid(mb.Position, VaultGridX, VaultGridY, VaultGridW, VaultGridH, _vaultScrollRow, _state.BankItemCount);
            if (vaultIdx >= 0)
            {
                _selectedVaultIdx = vaultIdx;
                _selectedInvIdx = -1;
                AcceptEvent();
                return;
            }

            // Click in user grid
            int userIdx = HitTestGrid(mb.Position, UserGridX, UserGridY, UserGridW, UserGridH, _invScrollRow, _userSlotCount);
            if (userIdx >= 0)
            {
                _selectedInvIdx = userIdx;
                _selectedVaultIdx = -1;
                AcceptEvent();
                return;
            }

            AcceptEvent();
        }
        else if (@event is InputEventMouseButton mbScroll)
        {
            // Scroll vault grid
            if (mbScroll.Position.X >= VaultGridX && mbScroll.Position.X < VaultGridX + VaultGridW &&
                mbScroll.Position.Y >= VaultGridY && mbScroll.Position.Y < VaultGridY + VaultGridH)
            {
                HandleScroll(mbScroll, ref _vaultScrollRow, _state.BankItemCount, VaultGridRows);
                AcceptEvent();
                return;
            }

            // Scroll user grid
            if (mbScroll.Position.X >= UserGridX && mbScroll.Position.X < UserGridX + UserGridW &&
                mbScroll.Position.Y >= UserGridY && mbScroll.Position.Y < UserGridY + UserGridH)
            {
                HandleScroll(mbScroll, ref _invScrollRow, _userSlotCount, UserGridRows);
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

    private void OnRetirarPressed()
    {
        if (_state == null || _tcp == null) return;
        if (_selectedVaultIdx < 0 || _selectedVaultIdx >= _state.BankItemCount) return;

        var item = _state.BankItems[_selectedVaultIdx];
        if (item.ObjIndex <= 0) return;

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > item.Amount) qty = item.Amount;

        _tcp.SendPacket(ClientPackets.WriteBankWithdraw((byte)item.Slot, (short)qty));
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
                Text = "No podes depositar el item porque lo estas usando.",
                Color = "FF0000"
            });
            return;
        }

        int qty = GetQuantity();
        if (qty <= 0) return;
        if (qty > inv.Amount) qty = inv.Amount;

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
            _tcp.SendPacket(ClientPackets.WriteTalk($"/DEPOSITAR {amount}"));
        else
            _tcp.SendPacket(ClientPackets.WriteTalk($"/RETIRAR {amount}"));

        HideGoldInputDialog();
    }

    private void OnGoldInputCancel()
    {
        HideGoldInputDialog();
    }
}

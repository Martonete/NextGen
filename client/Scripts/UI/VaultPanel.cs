using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmNuevoBancoObj — Item vault (boveda) panel.
/// Two icon grids (vault items / player inventory) with withdraw/deposit buttons,
/// quantity input, gold displays, and item preview.
/// Now uses RpgBaseForm for consistent RPG chrome (frame, title, close, drag).
/// Custom _Draw() rendering for item grids is preserved inside a child Control.
/// </summary>
public partial class VaultPanel : RpgBaseForm
{
    // Content area dimensions (inside the form chrome)
    private const int ContentW = 390;

    // Grid layout — 5 columns, 34x34 cells
    private const int GridCols = 5;
    private const int CellSize = 34;

    // Vault grid area (left side) — relative to _drawArea origin
    private const int VaultGridX = 0, VaultGridY = 82;
    private const int VaultGridW = GridCols * CellSize; // 170
    private const int VaultGridRows = 7;
    private const int VaultGridH = VaultGridRows * CellSize; // 238

    // User grid area (right side) — relative to _drawArea origin
    private const int UserGridX = 220, UserGridY = 82;
    private const int UserGridW = GridCols * CellSize;
    private const int UserGridRows = 7;
    private const int UserGridH = UserGridRows * CellSize;

    // Item preview area — relative to _drawArea origin
    private const int PreviewIconX = 5, PreviewIconY = 12;
    private const int PreviewNameX = 48, PreviewNameY = 10;
    private const int AmountLabelX = 48, AmountLabelY = 26;

    // Draw area total height
    private const int DrawAreaH = VaultGridY + VaultGridH + 4; // 324

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Selection state
    private int _selectedVaultIdx = -1;
    private int _selectedInvIdx = -1;
    private int _vaultScrollRow;
    private int _invScrollRow;

    // Filtered user inventory (non-empty slots)
    private int[] _userSlots = new int[GameState.MaxInventoryCapacity];
    private int _userSlotCount;

    // UI controls
    private Control? _drawArea;
    private LineEdit? _qtyInput;
    private Label? _vaultGoldLabel;
    private Label? _myGoldLabel;
    private TextureButton? _retirarBtn;
    private TextureButton? _depositarBtn;
    private TextureButton? _retirarOroBtn;
    private TextureButton? _depositarOroBtn;
    private TextureButton? _salirBtn;

    // Rich tooltip panel (set by Main.cs)
    public TooltipPanel? RichTooltip;

    public VaultPanel() : base("Boveda", new Vector2(450, 527), "v3") { }

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

        // Custom draw area for grids, preview, and grid headers
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
        _retirarBtn.CustomMinimumSize = new Vector2(170, 28);
        _retirarBtn.Pressed += OnRetirarPressed;
        btnRow.AddChild(_retirarBtn);

        _depositarBtn = RpgTheme.CreateRpgButton("Depositar", false, 14);
        _depositarBtn.CustomMinimumSize = new Vector2(170, 28);
        _depositarBtn.Pressed += OnDepositarPressed;
        btnRow.AddChild(_depositarBtn);

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

        // Gold display rows
        var vaultGoldRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        vbox.AddChild(vaultGoldRow);

        var vaultGoldTitle = RpgTheme.CreateInfoLabel("Oro Boveda:", 11);
        vaultGoldRow.AddChild(vaultGoldTitle);

        _vaultGoldLabel = RpgTheme.CreateTitleLabel("0", 12);
        _vaultGoldLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.4f));
        _vaultGoldLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _vaultGoldLabel.HorizontalAlignment = HorizontalAlignment.Left;
        vaultGoldRow.AddChild(_vaultGoldLabel);

        _retirarOroBtn = RpgTheme.CreateRpgButton("Retirar", false, 11);
        _retirarOroBtn.CustomMinimumSize = new Vector2(87, 22);
        _retirarOroBtn.Pressed += OnRetirarOroPressed;
        vaultGoldRow.AddChild(_retirarOroBtn);

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

    public void OpenVault()
    {
        _selectedVaultIdx = -1;
        _selectedInvIdx = -1;
        _vaultScrollRow = 0;
        _invScrollRow = 0;
        _qtyInput!.Text = "1";
        ShowForm();
    }

    public override void HideForm()
    {
        base.HideForm();
        // Always clear both bank flags to prevent input freeze.
        // Without this, closing via X button leaves Banqueando/BovedaAbierta=true
        // and AnyFormOpen blocks all game input permanently.
        if (_state != null)
        {
            _state.Banqueando = false;
            _state.BovedaAbierta = false;
        }
        _selectedVaultIdx = -1;
        _selectedInvIdx = -1;
        HideGoldInputDialog();
        RichTooltip?.Hide();
    }

    public void CloseVault()
    {
        HideForm();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
            RichTooltip?.Hide();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Rebuild filtered user inventory
        _userSlotCount = 0;
        for (int i = 0; i < _state.MaxInventorySlots; i++)
        {
            if (_state.Inventory[i].ObjIndex > 0)
                _userSlots[_userSlotCount++] = i;
        }

        // Update gold displays
        _vaultGoldLabel!.Text = _state.BankGold.ToString("N0");
        _myGoldLabel!.Text = _state.Gold.ToString("N0");

        _drawArea?.QueueRedraw();
    }

    private void OnDrawArea()
    {
        if (_state == null || _data == null || _drawArea == null) return;

        var font = _data.Fonts?[1];

        // Grid headers
        font?.DrawText(_drawArea, VaultGridX + VaultGridW / 2, VaultGridY - 14, "Boveda", Colors.White, center: true);
        font?.DrawText(_drawArea, UserGridX + UserGridW / 2, UserGridY - 14, "Inventario", Colors.White, center: true);

        // Vault grid background
        _drawArea.DrawRect(new Rect2(VaultGridX, VaultGridY, VaultGridW, VaultGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        _drawArea.DrawRect(new Rect2(VaultGridX, VaultGridY, VaultGridW, VaultGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // User grid background
        _drawArea.DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.05f, 0.05f, 0.08f, 0.9f));
        _drawArea.DrawRect(new Rect2(UserGridX, UserGridY, UserGridW, UserGridH), new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

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
                _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));

            // Item icon
            if (item.GrhIndex > 0)
                CharRenderer.DrawGrh(_drawArea, _data, item.GrhIndex, 0, new Vector2(cx + 1, cy));

            // Amount overlay
            if (item.Amount > 0 && font != null)
                font.DrawText(_drawArea, (int)cx, (int)cy + 3, item.Amount.ToString(), Colors.White);

            // Cell border
            _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(0.4f, 0.4f, 0.5f, 0.4f), false, 1f);
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
                _drawArea.DrawRect(new Rect2(cx, cy, CellSize, CellSize), new Color(1f, 1f, 1f, 0.15f));

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
        DrawItemPreview(font);
    }

    private void DrawItemPreview(AoFont? font)
    {
        if (_state == null || _data == null || _drawArea == null) return;

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
        _drawArea.DrawRect(new Rect2(0, 0, ContentW, 68), new Color(0.12f, 0.1f, 0.15f, 0.9f));
        _drawArea.DrawRect(new Rect2(0, 0, ContentW, 68), new Color(0.4f, 0.35f, 0.3f, 0.5f), false, 1f);

        if (grhIndex > 0)
            CharRenderer.DrawGrh(_drawArea, _data, grhIndex, 0, new Vector2(PreviewIconX, PreviewIconY));

        font?.DrawText(_drawArea, PreviewNameX, PreviewNameY, name, new Color(1f, 0.9f, 0.5f));
        font?.DrawText(_drawArea, AmountLabelX, AmountLabelY, $"x{amount}", Colors.White);
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
        if (_state == null || _tcp == null || _drawArea == null) return;

        if (@event is InputEventMouseMotion mm)
        {
            // Tooltip on hover over grid items
            if (RichTooltip != null && _state != null)
            {
                int vIdx = HitTestGrid(mm.Position, VaultGridX, VaultGridY, VaultGridW, VaultGridH, _vaultScrollRow, _state.BankItemCount);
                int iIdx = HitTestGrid(mm.Position, UserGridX, UserGridY, UserGridW, UserGridH, _invScrollRow, _userSlotCount);

                if (vIdx >= 0 && vIdx < _state.BankItemCount)
                    RichTooltip.ShowBankItem(_state.BankItems[vIdx]);
                else if (iIdx >= 0 && iIdx < _userSlotCount)
                    RichTooltip.ShowInventoryItem(_state.Inventory[_userSlots[iIdx]]);
                else
                    RichTooltip.Hide();
            }
        }

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            // Click in vault grid
            int vaultIdx = HitTestGrid(mb.Position, VaultGridX, VaultGridY, VaultGridW, VaultGridH, _vaultScrollRow, _state!.BankItemCount);
            if (vaultIdx >= 0)
            {
                _selectedVaultIdx = vaultIdx;
                _selectedInvIdx = -1;
                _drawArea.AcceptEvent();
                return;
            }

            // Click in user grid
            int userIdx = HitTestGrid(mb.Position, UserGridX, UserGridY, UserGridW, UserGridH, _invScrollRow, _userSlotCount);
            if (userIdx >= 0)
            {
                _selectedInvIdx = userIdx;
                _selectedVaultIdx = -1;
                _drawArea.AcceptEvent();
                return;
            }

            _drawArea.AcceptEvent();
        }
        else if (@event is InputEventMouseButton mbScroll)
        {
            // Scroll vault grid
            if (mbScroll.Position.X >= VaultGridX && mbScroll.Position.X < VaultGridX + VaultGridW &&
                mbScroll.Position.Y >= VaultGridY && mbScroll.Position.Y < VaultGridY + VaultGridH)
            {
                HandleScroll(mbScroll, ref _vaultScrollRow, _state!.BankItemCount, VaultGridRows);
                _drawArea.AcceptEvent();
                return;
            }

            // Scroll user grid
            if (mbScroll.Position.X >= UserGridX && mbScroll.Position.X < UserGridX + UserGridW &&
                mbScroll.Position.Y >= UserGridY && mbScroll.Position.Y < UserGridY + UserGridH)
            {
                HandleScroll(mbScroll, ref _invScrollRow, _userSlotCount, UserGridRows);
                _drawArea.AcceptEvent();
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
        HideForm(); // Clear flags locally — don't wait for server response
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

        _goldInputLabel!.Text = deposit ? "Depositar oro:" : "Retirar oro:";
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

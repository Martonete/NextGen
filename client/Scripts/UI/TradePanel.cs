using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// Player-to-player trade panel (VB6 frmComerciarUsu).
/// Two columns: your offer (left) and partner offer (right).
/// Up to 10 item slots per side, gold input, accept/cancel buttons.
/// Reads all trade data from GameState (populated by PacketHandler).
/// </summary>
public partial class TradePanel : Control
{
    // Panel dimensions
    private const int PanelW = 420;
    private const int PanelH = 420;

    // Layout constants
    private const int TitleBarH = 28;
    private const int ColW = 185;
    private const int LeftColX = 12;
    private const int RightColX = PanelW - ColW - 12; // 223
    private const int HeaderY = 34;
    private const int SlotStartY = 52;
    private const int SlotH = 22;
    private const int MaxSlots = 10;
    private const int SlotsAreaH = MaxSlots * SlotH; // 220

    // Gold area
    private const int GoldY = SlotStartY + SlotsAreaH + 8; // 280
    private const int GoldLabelH = 18;

    // Status area
    private const int StatusY = GoldY + GoldLabelH + 32; // 330

    // Buttons
    private const int BtnY = StatusY + 24; // 354
    private const int BtnW = 90;
    private const int BtnH = 26;
    private const int AcceptBtnX = PanelW / 2 - BtnW - 8; // 122
    private const int CancelBtnX = PanelW / 2 + 8; // 218
    private const int CloseBtnX = PanelW - 30;
    private const int CloseBtnY = 2;
    private const int CloseBtnW = 26;
    private const int CloseBtnH = 24;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Local acceptance state (our side — reset when offer changes)
    private bool _myAccepted;

    // Track slot/gold counts to detect changes and reset acceptance
    private int _lastMySlotCount;
    private int _lastPartnerSlotCount;
    private int _lastMyGold;
    private int _lastPartnerGold;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // UI controls
    private LineEdit? _goldInput;
    private Button? _acceptBtn;
    private Button? _cancelBtn;
    private Button? _closeBtn;
    private Button? _offerGoldBtn;

    // Rich tooltip panel (set by Main.cs)
    public TooltipPanel? RichTooltip;

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

        // Gold input for your offer
        _goldInput = new LineEdit();
        _goldInput.Position = new Vector2(LeftColX + 40, GoldY + GoldLabelH + 2);
        _goldInput.Size = new Vector2(80, 20);
        _goldInput.Text = "0";
        _goldInput.Alignment = HorizontalAlignment.Center;
        _goldInput.FocusMode = FocusModeEnum.Click;
        _goldInput.AddThemeColorOverride("font_color", Colors.White);
        _goldInput.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_goldInput);

        // Offer gold button
        _offerGoldBtn = CreateButton("Ofrecer", LeftColX + 124, GoldY + GoldLabelH + 2, 60, 20);
        _offerGoldBtn.Pressed += OnOfferGoldPressed;
        AddChild(_offerGoldBtn);

        // Accept button
        _acceptBtn = CreateButton("Aceptar", AcceptBtnX, BtnY, BtnW, BtnH);
        _acceptBtn.Pressed += OnAcceptPressed;
        AddChild(_acceptBtn);

        // Cancel button
        _cancelBtn = CreateButton("Cancelar", CancelBtnX, BtnY, BtnW, BtnH);
        _cancelBtn.Pressed += OnCancelPressed;
        AddChild(_cancelBtn);

        // Close button (X)
        _closeBtn = CreateButton("X", CloseBtnX, CloseBtnY, CloseBtnW, CloseBtnH);
        _closeBtn.Pressed += OnCancelPressed;
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

    // ── Public API (called by Main) ──────────────────────────────

    public void OpenTrade()
    {
        if (_state == null) return;
        _myAccepted = false;
        _lastMySlotCount = 0;
        _lastPartnerSlotCount = 0;
        _lastMyGold = 0;
        _lastPartnerGold = 0;
        _goldInput!.Text = "0";
        Visible = true;
    }

    public void CloseTrade()
    {
        Visible = false;
        _myAccepted = false;
        RichTooltip?.Hide();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
            RichTooltip?.Hide();
    }

    /// <summary>
    /// Called from Main/InputHandler when user clicks an inventory slot while trading.
    /// Sends the item offer packet to server.
    /// </summary>
    public void OfferInventorySlot(int invSlot, int amount)
    {
        if (_tcp == null || _state == null) return;
        if (invSlot < 0 || invSlot >= 25) return;

        var inv = _state.Inventory[invSlot];
        if (inv.ObjIndex <= 0 || inv.Amount <= 0) return;
        if (inv.Equipped)
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "No puedes ofrecer un objeto equipado.",
                Color = "FF0000"
            });
            return;
        }

        int qty = Math.Min(amount, inv.Amount);
        _tcp.SendPacket(ClientPackets.WriteTradeOfferItem((byte)(invSlot + 1), (short)qty));
    }

    // ── Process / Draw ───────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;

        // Detect offer changes to reset acceptance flags
        if (_state.MyTradeSlotCount != _lastMySlotCount
            || _state.PartnerTradeSlotCount != _lastPartnerSlotCount
            || _state.MyTradeGold != _lastMyGold
            || _state.PartnerTradeGold != _lastPartnerGold)
        {
            _myAccepted = false;
            _lastMySlotCount = _state.MyTradeSlotCount;
            _lastPartnerSlotCount = _state.PartnerTradeSlotCount;
            _lastMyGold = _state.MyTradeGold;
            _lastPartnerGold = _state.PartnerTradeGold;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null || _data == null) return;

        var font = _data.Fonts?[1];

        // Background
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.08f, 0.06f, 0.12f, 0.96f));
        DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.5f, 0.4f, 0.3f, 0.8f), false, 2f);

        // Title
        string partnerName = _state.TradePartnerName;
        string title = string.IsNullOrEmpty(partnerName)
            ? "Comercio"
            : $"Comercio con {partnerName}";
        font?.DrawText(this, PanelW / 2, 6, title, new Color(1f, 0.85f, 0.4f), center: true);

        // Divider line
        DrawLine(new Vector2(PanelW / 2, HeaderY - 4), new Vector2(PanelW / 2, StatusY - 4),
            new Color(0.5f, 0.4f, 0.3f, 0.5f), 1f);

        // Left column: Your offer
        DrawColumn(font, LeftColX, "Tu oferta",
            _state.MyTradeSlots, _state.MyTradeSlotCount, _state.MyTradeGold, true);

        // Right column: Partner offer
        string partnerHeader = string.IsNullOrEmpty(partnerName)
            ? "Su oferta"
            : $"Oferta de {partnerName}";
        DrawColumn(font, RightColX, partnerHeader,
            _state.PartnerTradeSlots, _state.PartnerTradeSlotCount, _state.PartnerTradeGold, false);

        // Status indicators
        DrawStatusIndicators(font);
    }

    private void DrawColumn(AoFont? font, int colX, string header,
        TradeOfferSlot[] slots, int slotCount, int gold, bool isLeft)
    {
        // Column header
        font?.DrawText(this, colX + ColW / 2, HeaderY, header, Colors.White, center: true);

        // Slot list background
        DrawRect(new Rect2(colX, SlotStartY, ColW, SlotsAreaH),
            new Color(0.05f, 0.05f, 0.08f, 0.9f));
        DrawRect(new Rect2(colX, SlotStartY, ColW, SlotsAreaH),
            new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw item slots
        for (int i = 0; i < MaxSlots; i++)
        {
            float sy = SlotStartY + i * SlotH;

            // Alternating row background
            if (i % 2 == 1)
                DrawRect(new Rect2(colX, sy, ColW, SlotH), new Color(1f, 1f, 1f, 0.03f));

            // Row separator
            if (i > 0)
                DrawLine(new Vector2(colX, sy), new Vector2(colX + ColW, sy),
                    new Color(0.3f, 0.3f, 0.4f, 0.3f), 1f);

            if (i < slotCount)
            {
                var slot = slots[i];
                string itemText = slot.Amount > 1
                    ? $"{slot.Name} x{slot.Amount}"
                    : slot.Name;

                // Item icon (small, if grhIndex available)
                float textX = colX + 4;
                if (slot.GrhIndex > 0)
                {
                    CharRenderer.DrawGrh(this, _data!, slot.GrhIndex, 0,
                        new Vector2(colX + 2, sy + 1));
                    textX = colX + 22;
                }

                font?.DrawText(this, (int)textX, (int)sy + 4, itemText,
                    new Color(0.9f, 0.9f, 0.8f));
            }
        }

        // Gold display
        float goldLabelY = GoldY;
        if (isLeft)
        {
            font?.DrawText(this, colX + 2, (int)goldLabelY, "Oro:", new Color(1f, 1f, 0f));
            // Gold input and offer button are child Controls (positioned in _Ready)
        }
        else
        {
            font?.DrawText(this, colX + 2, (int)goldLabelY, "Oro:", new Color(1f, 1f, 0f));
            font?.DrawText(this, colX + 40, (int)goldLabelY,
                gold.ToString("N0"), Colors.White);
        }
    }

    private void DrawStatusIndicators(AoFont? font)
    {
        // Your acceptance status
        var myColor = _myAccepted
            ? new Color(0.2f, 0.8f, 0.2f) // green
            : new Color(0.5f, 0.5f, 0.5f); // gray
        string myStatus = _myAccepted ? "Aceptado" : "Pendiente";
        DrawRect(new Rect2(LeftColX, StatusY, 10, 10), myColor);
        font?.DrawText(this, LeftColX + 14, StatusY - 2, $"Tu: {myStatus}", myColor);

        // Partner acceptance status (from GameState)
        bool partnerAccepted = _state?.TradePartnerAccepted ?? false;
        var partnerColor = partnerAccepted
            ? new Color(0.2f, 0.8f, 0.2f)
            : new Color(0.5f, 0.5f, 0.5f);
        string partnerStatus = partnerAccepted ? "Aceptado" : "Pendiente";
        string partnerName = _state?.TradePartnerName ?? "";
        DrawRect(new Rect2(RightColX, StatusY, 10, 10), partnerColor);
        font?.DrawText(this, RightColX + 14, StatusY - 2, $"{partnerName}: {partnerStatus}", partnerColor);
    }

    // ── Input handling ───────────────────────────────────────────

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
        if (@event is InputEventMouseMotion dragMm)
        {
            if (_dragging)
            {
                GlobalPosition = dragMm.GlobalPosition - _dragOffset;
                AcceptEvent();
                return;
            }

            // Tooltip on hover over trade slot list items
            if (RichTooltip != null && _state != null)
            {
                bool shown = false;
                // Left column: your offer slots
                float ly = dragMm.Position.Y - SlotStartY;
                if (dragMm.Position.X >= LeftColX && dragMm.Position.X < LeftColX + ColW
                    && ly >= 0 && ly < SlotsAreaH)
                {
                    int idx = (int)(ly / SlotH);
                    if (idx >= 0 && idx < _state.MyTradeSlotCount)
                    {
                        RichTooltip.ShowTradeItem(_state.MyTradeSlots[idx]);
                        shown = true;
                    }
                }
                // Right column: partner offer slots
                if (!shown && dragMm.Position.X >= RightColX && dragMm.Position.X < RightColX + ColW
                    && ly >= 0 && ly < SlotsAreaH)
                {
                    int idx = (int)(ly / SlotH);
                    if (idx >= 0 && idx < _state.PartnerTradeSlotCount)
                    {
                        RichTooltip.ShowTradeItem(_state.PartnerTradeSlots[idx]);
                        shown = true;
                    }
                }
                if (!shown)
                    RichTooltip.Hide();
            }
        }

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                OnCancelPressed();
                AcceptEvent();
                return;
            }
            AcceptEvent();
        }
    }

    // ── Button handlers ──────────────────────────────────────────

    private void OnOfferGoldPressed()
    {
        if (_tcp == null || _state == null || _goldInput == null) return;

        if (!int.TryParse(_goldInput.Text.Trim(), out int amount) || amount < 0)
        {
            _goldInput.Text = "0";
            return;
        }

        // Clamp to player gold
        if (amount > _state.Gold)
            amount = _state.Gold;

        _myAccepted = false;
        _tcp.SendPacket(ClientPackets.WriteTradeOfferGold(amount));
    }

    private void OnAcceptPressed()
    {
        if (_tcp == null) return;
        _myAccepted = true;
        _tcp.SendPacket(ClientPackets.WriteTradeResponse(0)); // 0 = accept
    }

    private void OnCancelPressed()
    {
        if (_tcp == null) return;
        _tcp.SendPacket(ClientPackets.WriteTradeCancel());
    }
}

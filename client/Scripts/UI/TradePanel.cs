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
/// Now uses RpgBaseForm for consistent RPG chrome (frame, title, close, drag).
/// Custom _Draw() rendering for trade slots is preserved inside a child Control.
/// </summary>
public partial class TradePanel : RpgBaseForm
{
    // Content area layout constants (relative to _drawArea origin)
    private const int ContentW = 360;
    private const int ColW = 165;
    private const int LeftColX = 0;
    private const int RightColX = ContentW - ColW; // 195
    private const int HeaderY = 0;
    private const int SlotStartY = 18;
    private const int SlotH = 22;
    private const int MaxSlots = 10;
    private const int SlotsAreaH = MaxSlots * SlotH; // 220

    // Gold area (relative to _drawArea)
    private const int GoldY = SlotStartY + SlotsAreaH + 8; // 246
    private const int GoldLabelH = 18;

    // Status area (relative to _drawArea)
    private const int StatusY = GoldY + GoldLabelH + 32; // 296
    private const int DrawAreaH = StatusY + 16; // 312

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    // Local acceptance state (our side -- reset when offer changes)
    private bool _myAccepted;

    // Track slot/gold counts to detect changes and reset acceptance
    private int _lastMySlotCount;
    private int _lastPartnerSlotCount;
    private int _lastMyGold;
    private int _lastPartnerGold;

    // UI controls
    private Control? _drawArea;
    private LineEdit? _goldInput;
    private TextureButton? _acceptBtn;
    private TextureButton? _cancelBtn;
    private TextureButton? _offerGoldBtn;

    // Rich tooltip panel (set by Main.cs)
    public TooltipPanel? RichTooltip;

    public TradePanel() : base("Comercio", new Vector2(420, 470), "v3") { }

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

        // Custom draw area for trade columns, slots, status indicators
        _drawArea = new Control();
        _drawArea.CustomMinimumSize = new Vector2(ContentW, DrawAreaH);
        _drawArea.MouseFilter = MouseFilterEnum.Stop;
        _drawArea.Draw += OnDrawArea;
        _drawArea.GuiInput += OnDrawAreaInput;
        vbox.AddChild(_drawArea);

        // Gold input row (your side)
        var goldRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(goldRow);

        var goldLabel = RpgTheme.CreateInfoLabel("Oro:", 12);
        goldRow.AddChild(goldLabel);

        _goldInput = RpgTheme.CreateRpgInput("0", 80);
        _goldInput.Text = "0";
        _goldInput.Alignment = HorizontalAlignment.Center;
        goldRow.AddChild(_goldInput);

        _offerGoldBtn = RpgTheme.CreateRpgButton("Ofrecer", false, 12);
        _offerGoldBtn.CustomMinimumSize = new Vector2(80, 24);
        _offerGoldBtn.Pressed += OnOfferGoldPressed;
        goldRow.AddChild(_offerGoldBtn);

        // Button row: Accept / Cancel
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _acceptBtn = RpgTheme.CreateRpgButton("Aceptar", false, 14);
        _acceptBtn.CustomMinimumSize = new Vector2(130, 30);
        _acceptBtn.Pressed += OnAcceptPressed;
        btnRow.AddChild(_acceptBtn);

        _cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 14);
        _cancelBtn.CustomMinimumSize = new Vector2(130, 30);
        _cancelBtn.Pressed += OnCancelPressed;
        btnRow.AddChild(_cancelBtn);
    }

    // -- Public API (called by Main) --

    public void OpenTrade()
    {
        if (_state == null) return;
        _myAccepted = false;
        _lastMySlotCount = 0;
        _lastPartnerSlotCount = 0;
        _lastMyGold = 0;
        _lastPartnerGold = 0;
        _goldInput!.Text = "0";
        ShowForm();
    }

    public override void HideForm()
    {
        // Send cancel packet and reset Trading flag so AnyFormOpen doesn't freeze input
        if (_state != null && _state.Trading)
        {
            _tcp?.SendPacket(ClientPackets.WriteTradeCancel());
            _state.Trading = false;
        }
        base.HideForm();
    }

    public void CloseTrade()
    {
        HideForm();
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
        if (invSlot < 0 || invSlot >= _state.MaxInventorySlots) return;

        var inv = _state.Inventory[invSlot];
        if (inv.ObjIndex <= 0 || inv.Amount <= 0) return;
        if (inv.Equipped)
        {
            _state.EnqueueChat(new ChatMessage
            {
                Text = "No puedes ofrecer un objeto equipado.",
                Color = "FF0000"
            });
            return;
        }

        int qty = Math.Min(amount, inv.Amount);
        _tcp.SendPacket(ClientPackets.WriteTradeOfferItem((byte)(invSlot + 1), (short)qty));
    }

    // -- Process / Draw --

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

        // Update title with partner name
        string partnerName = _state.TradePartnerName;
        if (!string.IsNullOrEmpty(partnerName))
            TitleText = $"Comercio con {partnerName}";
        else
            TitleText = "Comercio";

        _drawArea?.QueueRedraw();
    }

    private void OnDrawArea()
    {
        if (_state == null || _data == null || _drawArea == null) return;

        var font = _data.Fonts?[1];

        // Divider line
        _drawArea.DrawLine(new Vector2(ContentW / 2, HeaderY), new Vector2(ContentW / 2, StatusY - 4),
            new Color(0.5f, 0.4f, 0.3f, 0.5f), 1f);

        // Left column: Your offer
        DrawColumn(font, LeftColX, "Tu oferta",
            _state.MyTradeSlots, _state.MyTradeSlotCount, _state.MyTradeGold, true);

        // Right column: Partner offer
        string partnerName = _state.TradePartnerName;
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
        if (_drawArea == null) return;

        // Column header
        font?.DrawText(_drawArea, colX + ColW / 2, HeaderY, header, Colors.White, center: true);

        // Slot list background
        _drawArea.DrawRect(new Rect2(colX, SlotStartY, ColW, SlotsAreaH),
            new Color(0.05f, 0.05f, 0.08f, 0.9f));
        _drawArea.DrawRect(new Rect2(colX, SlotStartY, ColW, SlotsAreaH),
            new Color(0.4f, 0.35f, 0.3f, 0.6f), false, 1f);

        // Draw item slots
        for (int i = 0; i < MaxSlots; i++)
        {
            float sy = SlotStartY + i * SlotH;

            // Alternating row background
            if (i % 2 == 1)
                _drawArea.DrawRect(new Rect2(colX, sy, ColW, SlotH), new Color(1f, 1f, 1f, 0.03f));

            // Row separator
            if (i > 0)
                _drawArea.DrawLine(new Vector2(colX, sy), new Vector2(colX + ColW, sy),
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
                    CharRenderer.DrawGrh(_drawArea, _data!, slot.GrhIndex, 0,
                        new Vector2(colX + 2, sy + 1));
                    textX = colX + 22;
                }

                font?.DrawText(_drawArea, (int)textX, (int)sy + 4, itemText,
                    new Color(0.9f, 0.9f, 0.8f));
            }
        }

        // Gold display
        float goldLabelY = GoldY;
        if (isLeft)
        {
            font?.DrawText(_drawArea, colX + 2, (int)goldLabelY, "Oro:", new Color(1f, 1f, 0f));
            // Gold input and offer button are child Controls (in BuildContent)
        }
        else
        {
            font?.DrawText(_drawArea, colX + 2, (int)goldLabelY, "Oro:", new Color(1f, 1f, 0f));
            font?.DrawText(_drawArea, colX + 40, (int)goldLabelY,
                gold.ToString("N0"), Colors.White);
        }
    }

    private void DrawStatusIndicators(AoFont? font)
    {
        if (_drawArea == null || _state == null) return;

        // Your acceptance status
        var myColor = _myAccepted
            ? new Color(0.2f, 0.8f, 0.2f) // green
            : new Color(0.5f, 0.5f, 0.5f); // gray
        string myStatus = _myAccepted ? "Aceptado" : "Pendiente";
        _drawArea.DrawRect(new Rect2(LeftColX, StatusY, 10, 10), myColor);
        font?.DrawText(_drawArea, LeftColX + 14, StatusY - 2, $"Tu: {myStatus}", myColor);

        // Partner acceptance status (from GameState)
        bool partnerAccepted = _state.TradePartnerAccepted;
        var partnerColor = partnerAccepted
            ? new Color(0.2f, 0.8f, 0.2f)
            : new Color(0.5f, 0.5f, 0.5f);
        string partnerStatus = partnerAccepted ? "Aceptado" : "Pendiente";
        string partnerName = _state.TradePartnerName ?? "";
        _drawArea.DrawRect(new Rect2(RightColX, StatusY, 10, 10), partnerColor);
        font?.DrawText(_drawArea, RightColX + 14, StatusY - 2, $"{partnerName}: {partnerStatus}", partnerColor);
    }

    // -- Input handling --

    private void OnDrawAreaInput(InputEvent @event)
    {
        if (_state == null || _tcp == null || _drawArea == null) return;

        if (@event is InputEventMouseMotion mm)
        {
            // Tooltip on hover over trade slot list items
            if (RichTooltip != null && _state != null)
            {
                bool shown = false;
                // Left column: your offer slots
                float ly = mm.Position.Y - SlotStartY;
                if (mm.Position.X >= LeftColX && mm.Position.X < LeftColX + ColW
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
                if (!shown && mm.Position.X >= RightColX && mm.Position.X < RightColX + ColW
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
                _drawArea.AcceptEvent();
                return;
            }
            _drawArea.AcceptEvent();
        }
    }

    // -- Button handlers --

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

using Godot;
using System;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;
using TierrasSagradasAO.Rendering;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6-accurate 5x5 inventory grid (174x174 px).
/// Custom-drawn using _Draw() for pixel-perfect match with VB6 picInv.
/// Slot layout: x = col*34 + 1 + col, y = row*34 + row (VB6 pixel corrections).
/// </summary>
public partial class InventoryPanel : Control
{
    private const int Cols = 5;
    private const int Rows = 5;
    private const int SlotSize = 34;
    private const int TotalSlots = 25;

    // GRH IDs from VB6 client
    private const int GrhInvBackground = 31570;
    private const int GrhSelectionHighlight = 32758;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    private int _selectedSlot = -1;
    private int _hoveredSlot = -1;
    private bool _dydEnabled;
    private int _dragSourceSlot = -1;
    private bool _dragging;
    private Vector2 _dragMousePos; // local mouse pos during drag
    private Vector2 _dragStartPos; // position where press started (for drag threshold)
    private bool _dragPending;     // press happened but drag not yet activated (needs movement threshold)
    private const float DragThreshold = 6f; // pixels of movement before drag activates

    // Tooltip label (set by Main.cs)
    public Label? TooltipLabel;

    public int SelectedSlot => _selectedSlot;
    public bool DyDEnabled { get => _dydEnabled; set => _dydEnabled = value; }

    /// <summary>Cancel any in-progress drag operation (e.g. when switching tabs).</summary>
    public void CancelDrag()
    {
        _dragging = false;
        _dragPending = false;
        _dragSourceSlot = -1;
    }

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
        {
            _hoveredSlot = -1;
            if (TooltipLabel != null) TooltipLabel.Text = "";
        }
    }

    public override void _Draw()
    {
        // VB6: Device_Box_Textured_Render(31570, 0, 0, 174, 174) — inventory background
        // Fallback to dark rect if GRH not available
        DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.08f, 0.08f, 0.12f, 0.95f));

        if (_state == null || _data == null) return;

        // Keep tooltip in sync when inventory data changes (e.g. potion use)
        if (_hoveredSlot >= 0)
            UpdateTooltip(_hoveredSlot);

        // Try to draw VB6 inventory background GRH
        CharRenderer.DrawGrh(this, _data, GrhInvBackground, 0, Vector2.Zero);

        var font = _data.Fonts?[1];

        for (int slot = 0; slot < TotalSlots; slot++)
        {
            // VB6 uses 1-indexed slots (i = slot+1)
            int i = slot + 1;

            // VB6: X = ((i-1) Mod 5) * 34 + 1, then col correction per Case statement
            float x = ((i - 1) % 5) * SlotSize + 1;
            // Column correction: col 1→+1, col 2→+2, col 3→+3, col 4→+4
            int col = (i - 1) % 5;
            if (col >= 1) x += col; // cases 2,7,12,17,22→+1; 3,8,13,18,23→+2; etc.

            // VB6: Y = ((i-1) \ 5) * 34, then row correction.
            // VB6 original code uses strict inequality (i < 15, i < 20 etc.) which
            // excludes items 10/15/20/25 from the row offset, causing 1-3px misalignment.
            // We use >= instead to include those items and make rows uniform.
            float y = ((i - 1) / 5) * SlotSize;
            int row = (i - 1) / 5;
            if (row >= 1) y += row;

            // Slot background
            DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(0.15f, 0.15f, 0.2f, 0.8f));

            // VB6: Draw_GrhIndex(32758, X, Y) for selected slot
            if (slot == _selectedSlot)
            {
                // Background fill + selection GRH overlay
                DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(1f, 1f, 1f, 0.12f));
                CharRenderer.DrawGrh(this, _data, GrhSelectionHighlight, 0, new Vector2(x, y));
            }
            else if (_dragging && _dydEnabled && slot == _hoveredSlot && slot != _dragSourceSlot)
            {
                // Drag destination highlight — green tint
                DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(0f, 1f, 0f, 0.2f));
            }
            else if (slot == _hoveredSlot)
            {
                // Hover highlight
                DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(1f, 1f, 1f, 0.1f));
            }

            // Item icon — dimmed if this is the drag source
            bool isDragSource = _dragging && _dydEnabled && slot == _dragSourceSlot;
            var invSlot = _state.Inventory[slot];
            if (invSlot.ObjIndex > 0 && invSlot.GrhIndex > 0)
            {
                if (isDragSource)
                {
                    // Dim the source slot during drag
                    DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(0f, 0f, 0f, 0.4f));
                }

                // VB6: Draw_GrhIndex at (X, Y-1) for normal items
                var iconPos = new Vector2(x, y - 1);
                CharRenderer.DrawGrh(this, _data, invSlot.GrhIndex, 0, iconPos);

                // VB6: Engine_Text_Draw X-1, Y+3, amount, white
                if (invSlot.Amount > 0 && font != null)
                {
                    font.DrawText(this, (int)x - 1, (int)y + 3,
                        invSlot.Amount.ToString(), Colors.White);
                }

                // VB6: Engine_Text_Draw X+23, Y+20, "E", yellow
                if (invSlot.Equipped && font != null)
                {
                    font.DrawText(this, (int)x + 23, (int)y + 20, "E",
                        new Color(1f, 1f, 0f));
                }
            }

            // Slot border
            DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(0.4f, 0.4f, 0.5f, 0.6f), false, 1f);
        }

        // VB6: Draw dragged item at cursor position (Draw_GrhInv at MouseXInv, MouseYInv)
        if (_dragging && _dydEnabled && _dragSourceSlot >= 0 && _dragSourceSlot < TotalSlots)
        {
            var srcItem = _state.Inventory[_dragSourceSlot];
            if (srcItem.GrhIndex > 0)
            {
                // Center the icon on cursor
                var ghostPos = _dragMousePos - new Vector2(SlotSize / 2f, SlotSize / 2f);
                CharRenderer.DrawGrh(this, _data, srcItem.GrhIndex, 0, ghostPos);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_state == null || _tcp == null) return;

        if (@event is InputEventMouseMotion motion)
        {
            int slot = HitTestSlot(motion.Position);
            if (slot != _hoveredSlot)
            {
                _hoveredSlot = slot;
                UpdateTooltip(slot);
            }

            // Drag threshold: only activate drag after mouse moves enough from press point.
            // This prevents drag from blocking rapid clicks (poteo) while allowing real drags.
            if (_dragPending && _dydEnabled && _dragSourceSlot >= 0)
            {
                if (motion.Position.DistanceTo(_dragStartPos) >= DragThreshold)
                {
                    _dragging = true;
                    _dragPending = false;
                }
            }

            // Track mouse position for ghost item rendering during active drag
            if (_dragging && _dydEnabled && _dragSourceSlot >= 0)
            {
                _dragMousePos = motion.Position;
            }
        }
        else if (@event is InputEventMouseButton mb)
        {
            int slot = HitTestSlot(mb.Position);

            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    if (slot >= 0 && slot < TotalSlots)
                    {
                        if (mb.DoubleClick)
                        {
                            // VB6: double click → QSA{slot},{picInv.Visible} (1=visible)
                            // Cancel any pending drag — this is a use action, not a drag
                            _dragPending = false;
                            _dragging = false;
                            _dragSourceSlot = -1;

                            _selectedSlot = slot;
                            _state.SelectedInvSlot = slot;
                            _tcp.SendPacket(ClientPackets.WriteUseItemClick((byte)(slot + 1)));
                        }
                        else
                        {
                            _selectedSlot = slot;
                            _state.SelectedInvSlot = slot;

                            // Prepare drag (pending) — don't activate yet until mouse moves enough
                            if (_dydEnabled && _state.Inventory[slot].ObjIndex > 0)
                            {
                                _dragSourceSlot = slot;
                                _dragPending = true;
                                _dragStartPos = mb.Position;
                                // _dragging stays false until threshold is met
                            }
                        }
                    }
                }
                else // Released
                {
                    if (_dragging && _dydEnabled && _dragSourceSlot >= 0)
                    {
                        // Drag completed — check for swap
                        int destSlot = HitTestSlot(mb.Position);
                        if (destSlot >= 0 && destSlot < TotalSlots && destSlot != _dragSourceSlot)
                        {
                            // Send SWAP packet (1-indexed)
                            _tcp.SendPacket(ClientPackets.WriteSwapItems((byte)(_dragSourceSlot + 1), (byte)(destSlot + 1)));
                        }
                    }
                    // Always clear drag state on release
                    _dragSourceSlot = -1;
                    _dragging = false;
                    _dragPending = false;
                }
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                if (slot >= 0 && slot < TotalSlots && _state.Inventory[slot].ObjIndex > 0)
                {
                    _selectedSlot = slot;
                    _state.SelectedInvSlot = slot;
                    // VB6 parity: always send UseItem (QSA) — server decides equip vs use
                    _tcp.SendPacket(ClientPackets.WriteUseItemClick((byte)(slot + 1)));
                }
            }

            AcceptEvent();
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.E && _selectedSlot >= 0)
            {
                _tcp.SendPacket(ClientPackets.WriteEquipItem((byte)(_selectedSlot + 1)));
                AcceptEvent();
            }
        }
    }

    private int HitTestSlot(Vector2 pos)
    {
        // Bounds check — outside the grid area
        if (pos.X < 0 || pos.X >= Cols * SlotSize || pos.Y < 0 || pos.Y >= Rows * SlotSize)
            return -1;

        // VB6: ClickItem uses X \ 34 and Y \ 34 (integer division)
        int tempX = (int)pos.X / SlotSize;
        int tempY = (int)pos.Y / SlotSize;

        // VB6: TempItem = temp_x + temp_y * 5 + 1 (1-indexed)
        int item = tempX + tempY * Cols + 1;

        // Convert to 0-indexed and bounds check
        if (item >= 1 && item <= TotalSlots)
            return item - 1;
        return -1;
    }

    private void UpdateTooltip(int slot)
    {
        if (TooltipLabel == null) return;

        if (slot >= 0 && slot < TotalSlots)
        {
            var inv = _state!.Inventory[slot];
            if (inv.ObjIndex > 0)
            {
                TooltipLabel.Text = $"{inv.Name} - {inv.Amount}";
                return;
            }
        }
        TooltipLabel.Text = "";
    }
}

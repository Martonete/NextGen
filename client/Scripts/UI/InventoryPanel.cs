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

    // Tooltip label (set by Main.cs)
    public Label? TooltipLabel;

    public int SelectedSlot => _selectedSlot;
    public bool DyDEnabled { get => _dydEnabled; set => _dydEnabled = value; }

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

    public override void _Draw()
    {
        // VB6: Device_Box_Textured_Render(31570, 0, 0, 174, 174) — inventory background
        // Fallback to dark rect if GRH not available
        DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.08f, 0.08f, 0.12f, 0.95f));

        if (_state == null || _data == null) return;

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

            // VB6: Y = ((i-1) \ 5) * 34, then row correction with VB6's exact conditions
            float y = ((i - 1) / 5) * SlotSize;
            // Row correction — VB6 exact (note: items 10,15,20 intentionally excluded!)
            if (i > 10 && i < 15) y += 2;
            else if (i > 5 && i < 10) y += 1;
            else if (i > 15 && i < 20) y += 3;
            else if (i > 20) y += 4;

            // Slot background
            DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(0.15f, 0.15f, 0.2f, 0.8f));

            // VB6: Draw_GrhIndex(32758, X, Y) for selected slot
            if (slot == _selectedSlot)
            {
                CharRenderer.DrawGrh(this, _data, GrhSelectionHighlight, 0, new Vector2(x, y));
            }

            // Hover highlight
            if (slot == _hoveredSlot && slot != _selectedSlot)
            {
                DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(1f, 1f, 1f, 0.1f));
            }

            // Item icon
            var invSlot = _state.Inventory[slot];
            if (invSlot.ObjIndex > 0 && invSlot.GrhIndex > 0)
            {
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

            // Drag handling
            if (_dragging && _dydEnabled && _dragSourceSlot >= 0)
            {
                // Visual feedback handled by redraw
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
                            _selectedSlot = slot;
                            _state.SelectedInvSlot = slot;
                            _tcp.SendPacket($"QSA{slot + 1},{(Visible ? 1 : 0)}");
                        }
                        else
                        {
                            _selectedSlot = slot;
                            _state.SelectedInvSlot = slot;

                            // Start drag if DyD enabled
                            if (_dydEnabled && _state.Inventory[slot].ObjIndex > 0)
                            {
                                _dragSourceSlot = slot;
                                _dragging = true;
                            }
                        }
                    }
                }
                else // Released
                {
                    if (_dragging && _dydEnabled && _dragSourceSlot >= 0)
                    {
                        int destSlot = HitTestSlot(mb.Position);
                        if (destSlot >= 0 && destSlot < TotalSlots && destSlot != _dragSourceSlot)
                        {
                            // Send SWAP packet (1-indexed)
                            _tcp.SendPacket($"SWAP{destSlot + 1},{_dragSourceSlot + 1}");
                        }
                        _dragSourceSlot = -1;
                        _dragging = false;
                    }
                }
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                if (slot >= 0 && slot < TotalSlots)
                {
                    _selectedSlot = slot;
                    _state.SelectedInvSlot = slot;
                    // Right click → equip/unequip
                    _tcp.SendPacket($"EQUI{slot + 1}");
                }
            }

            AcceptEvent();
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.E && _selectedSlot >= 0)
            {
                _tcp.SendPacket($"EQUI{_selectedSlot + 1}");
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

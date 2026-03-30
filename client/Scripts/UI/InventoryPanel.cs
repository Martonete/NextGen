using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6-accurate 5x5 inventory grid (174x174 px).
/// Custom-drawn using _Draw() for pixel-perfect match with VB6 picInv.
/// Slot layout: x = col*34 + 1 + col, y = row*34 + row (VB6 pixel corrections).
/// Supports cross-panel drag & drop: inventory → ground/trade/vault.
/// </summary>
public partial class InventoryPanel : Control
{
    private const int Cols = 5;
    private const int SlotSize = 34;
    private int _totalSlots = 25;

    private int EffectiveRows => (_totalSlots + Cols - 1) / Cols;

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
    private bool _dirty = true;

    // Tooltip label (set by Main.cs) — simple name display in bottom bar
    public Label? TooltipLabel;

    // Rich tooltip panel (set by Main.cs) — floating detailed item info
    public TooltipPanel? RichTooltip;

    public int SelectedSlot => _selectedSlot;
    public bool DyDEnabled { get => _dydEnabled; set => _dydEnabled = value; }

    /// <summary>True when a drag is in progress (used by Main.cs for cross-panel routing).</summary>
    public bool IsDragging => _dragging && _dydEnabled && _dragSourceSlot >= 0;

    /// <summary>The inventory slot being dragged (0-based), or -1 if none.</summary>
    public int DragSourceSlot => _dragSourceSlot;

    /// <summary>
    /// Fired when a drag ends outside the inventory panel bounds.
    /// Args: (int slot0based, Vector2 globalDropPosition).
    /// Main.cs uses this to route to trade/vault/ground.
    /// </summary>
    public event Action<int, Vector2>? OnDropOutside;

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
        // Pixel-perfect bitmap font/icon scaling (no bilinear blur)
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void MarkDirty() => _dirty = true;

    public override void _Process(double delta)
    {
        if (!Visible) return;

        if (_state != null && _state.MaxInventorySlots != _totalSlots)
        {
            _totalSlots = _state.MaxInventorySlots;
            QueueRedraw();
        }

        if (_dirty || _hoveredSlot >= 0)
        {
            _dirty = false;
            QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
        {
            _hoveredSlot = -1;
            if (TooltipLabel != null) TooltipLabel.Text = "";
            RichTooltip?.Hide();
        }
    }

    /// <summary>
    /// Global input handler: catches mouse release outside the inventory during drag.
    /// Without this, releasing the mouse outside the panel would leave drag state stuck.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!_dragging || !_dydEnabled || _dragSourceSlot < 0) return;

        // Track mouse position globally during drag (for ghost item rendering even outside panel)
        if (@event is InputEventMouseMotion globalMotion)
        {
            _dragMousePos = globalMotion.Position - GlobalPosition;
        }
        else if (@event is InputEventMouseButton globalMb
            && globalMb.ButtonIndex == MouseButton.Left
            && !globalMb.Pressed)
        {
            // Mouse released globally — check if inside inventory or outside
            var localPos = globalMb.Position - GlobalPosition;
            int destSlot = HitTestSlot(localPos);

            if (destSlot >= 0 && destSlot < _totalSlots && destSlot != _dragSourceSlot)
            {
                // Drop inside inventory — swap
                _tcp?.SendPacket(ClientPackets.WriteSwapItems(
                    (byte)(_dragSourceSlot + 1), (byte)(destSlot + 1)));
            }
            else if (destSlot < 0)
            {
                // Drop outside inventory — cross-panel or ground drop
                OnDropOutside?.Invoke(_dragSourceSlot, globalMb.Position);
            }

            // Clear drag state
            _dragSourceSlot = -1;
            _dragging = false;
            _dragPending = false;

            // Consume event so other controls don't process this release
            GetViewport().SetInputAsHandled();
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

        for (int slot = 0; slot < _totalSlots; slot++)
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

            if (slot == _selectedSlot)
            {
                DrawRect(new Rect2(x, y, SlotSize, SlotSize), new Color(0.4f, 0.35f, 0.2f, 0.35f));
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

                // Draw item icon at native 32x32 size, centered in slot.
                // The panel has UIScale applied via Control.Scale, which stretches
                // sprites and makes them pixelated. We counteract by applying
                // inverse scale around the icon draw, keeping it crisp.
                float uiScale = ResolutionManager.UIScale;
                if (uiScale > 1.001f)
                {
                    float inv = 1f / uiScale;
                    // Center: slot center in scaled space → draw at native size
                    float cx = x + SlotSize / 2f;
                    float cy = y - 1 + SlotSize / 2f;
                    DrawSetTransform(new Vector2(cx, cy), 0f, new Vector2(inv, inv));
                    var iconPos = new Vector2(-16f, -16f); // half of 32x32
                    CharRenderer.DrawGrh(this, _data, invSlot.GrhIndex, 0, iconPos);
                    DrawSetTransform(Vector2.Zero); // reset
                }
                else
                {
                    var iconPos = new Vector2(x, y - 1);
                    CharRenderer.DrawGrh(this, _data, invSlot.GrhIndex, 0, iconPos);
                }

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
        if (_dragging && _dydEnabled && _dragSourceSlot >= 0 && _dragSourceSlot < _totalSlots)
        {
            var srcItem = _state.Inventory[_dragSourceSlot];
            if (srcItem.GrhIndex > 0)
            {
                // Center the icon on cursor at native 32x32
                float gs = ResolutionManager.UIScale;
                if (gs > 1.001f)
                {
                    float ginv = 1f / gs;
                    DrawSetTransform(_dragMousePos, 0f, new Vector2(ginv, ginv));
                    CharRenderer.DrawGrh(this, _data, srcItem.GrhIndex, 0, new Vector2(-16f, -16f));
                    DrawSetTransform(Vector2.Zero);
                }
                else
                {
                    var ghostPos = _dragMousePos - new Vector2(SlotSize / 2f, SlotSize / 2f);
                    CharRenderer.DrawGrh(this, _data, srcItem.GrhIndex, 0, ghostPos);
                }
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
                    if (slot >= 0 && slot < _totalSlots)
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
                    // Drag release is handled by _Input (global handler) for both
                    // inside and outside panel drops. Just clear pending state here.
                    _dragPending = false;
                }
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                if (slot >= 0 && slot < _totalSlots && _state.Inventory[slot].ObjIndex > 0)
                {
                    if (mb.DoubleClick)
                    {
                        _selectedSlot = slot;
                        _state.SelectedInvSlot = slot;
                        _tcp.SendPacket(ClientPackets.WriteUseItemClick((byte)(slot + 1)));
                    }
                    else
                    {
                        _selectedSlot = slot;
                        _state.SelectedInvSlot = slot;
                    }
                    AcceptEvent();
                }
            }

            // Only consume the event if we actually handled a valid slot interaction
            if (mb.ButtonIndex == MouseButton.Left && slot >= 0 && slot < _totalSlots)
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
        // Slots are drawn with 1px gaps between columns and rows, so the
        // effective stride is SlotSize+1 (35px) except for column/row 0
        // which starts at x=1, y=0.  Use the same position formula as
        // _Draw to find which slot the mouse is over.
        int stride = SlotSize + 1; // 35px per slot (34 + 1px gap)

        // Total grid extent including gaps
        int rows = EffectiveRows;
        int gridW = 1 + Cols * SlotSize + (Cols - 1); // 1 + 170 + 4 = 175
        int gridH = rows * SlotSize + (rows - 1);

        if (pos.X < 0 || pos.X >= gridW || pos.Y < 0 || pos.Y >= gridH)
            return -1;

        // Column: first slot starts at x=1, subsequent at stride intervals
        int tempX = (int)(pos.X - 1) / stride;
        int tempY = (int)pos.Y / stride;

        if (tempX < 0) tempX = 0;
        if (tempX >= Cols) tempX = Cols - 1;
        if (tempY >= rows) tempY = rows - 1;

        int item = tempX + tempY * Cols;
        if (item >= 0 && item < _totalSlots)
            return item;
        return -1;
    }

    private void UpdateTooltip(int slot)
    {
        if (!(_state?.Config?.ShowItemTooltip ?? true))
        {
            if (TooltipLabel != null) TooltipLabel.Text = "";
            RichTooltip?.Hide();
            return;
        }
        if (slot >= 0 && slot < _totalSlots)
        {
            var inv = _state!.Inventory[slot];
            if (inv.ObjIndex > 0)
            {
                if (TooltipLabel != null)
                    TooltipLabel.Text = $"{inv.Name} - {inv.Amount}";
                RichTooltip?.ShowInventoryItem(inv);
                return;
            }
        }
        if (TooltipLabel != null) TooltipLabel.Text = "";
        RichTooltip?.Hide();
    }
}

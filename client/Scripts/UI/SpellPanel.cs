using Godot;
using System;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6-accurate spell list panel (164x159 px).
/// Shows spell names in a scrollable list with selection highlight.
/// VB6 uses a standard ListBox (hlst) for spells.
/// </summary>
public partial class SpellPanel : Control
{
    private const int MaxSpells = 20;
    private const int LineHeight = 16;
    private const int VisibleLines = 10;

    private GameState? _state;
    private GameData? _data;
    private AoTcpClient? _tcp;

    private int _selectedSlot = -1;
    private int _hoveredSlot = -1;
    private int _scrollOffset;
    private bool _dragging; // VB6 ListBox: click-and-drag selects items

    public int SelectedSlot => _selectedSlot;

    public void Init(GameState state, GameData data, AoTcpClient tcp)
    {
        _state = state;
        _data = data;
        _tcp = tcp;
    }

    // Accumulated time for auto-scroll while dragging outside bounds
    private float _dragOutTimer;
    private const float DragScrollInterval = 0.08f; // seconds between auto-scroll ticks

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit)
        {
            _hoveredSlot = -1;
            // Don't cancel drag on mouse exit — let it continue scrolling
        }
    }

    public override void _Process(double delta)
    {
        // While dragging outside the panel, auto-scroll periodically
        if (_dragging)
        {
            var localMouse = GetLocalMousePosition();
            if (localMouse.Y < 0)
            {
                // Cursor above panel — scroll up
                _dragOutTimer += (float)delta;
                if (_dragOutTimer >= DragScrollInterval)
                {
                    _dragOutTimer = 0;
                    if (_scrollOffset > 0)
                    {
                        _scrollOffset--;
                        _selectedSlot = _scrollOffset; // select top visible
                    }
                }
            }
            else if (localMouse.Y > Size.Y)
            {
                // Cursor below panel — scroll down
                _dragOutTimer += (float)delta;
                if (_dragOutTimer >= DragScrollInterval)
                {
                    _dragOutTimer = 0;
                    if (_scrollOffset + VisibleLines < MaxSpells)
                    {
                        _scrollOffset++;
                        _selectedSlot = _scrollOffset + VisibleLines - 1; // select bottom visible
                    }
                }
            }
            else
            {
                _dragOutTimer = 0;
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        // Dark background
        DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.08f, 0.08f, 0.12f, 0.95f));

        if (_state == null || _data == null) return;

        var font = _data.Fonts?[1];
        if (font == null) return;

        int y = 2;
        for (int i = 0; i < VisibleLines; i++)
        {
            int slot = i + _scrollOffset;
            if (slot >= MaxSpells) break;

            var spell = _state.Spells[slot];
            float lineY = y + i * LineHeight;

            // Selection highlight
            if (slot == _selectedSlot)
            {
                DrawRect(new Rect2(2, lineY, Size.X - 4, LineHeight),
                    new Color(0.3f, 0.3f, 0.8f, 0.6f));
            }
            else if (slot == _hoveredSlot)
            {
                DrawRect(new Rect2(2, lineY, Size.X - 4, LineHeight),
                    new Color(1f, 1f, 1f, 0.1f));
            }

            // Spell name — VB6 hlst shows plain name, no numbered prefix
            if (spell.SpellId > 0)
            {
                font.DrawText(this, 4, (int)lineY + 1, spell.Name, Colors.White);
            }
            else
            {
                font.DrawText(this, 4, (int)lineY + 1, "(vacio)",
                    new Color(0.4f, 0.4f, 0.4f));
            }
        }

        // Scroll indicators
        if (_scrollOffset > 0)
        {
            font.DrawText(this, (int)Size.X - 16, 2, "^", Colors.Yellow);
        }
        if (_scrollOffset + VisibleLines < MaxSpells)
        {
            font.DrawText(this, (int)Size.X - 16, (int)Size.Y - font.CharHeight - 2, "v", Colors.Yellow);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Catch mouse release outside the panel to end drag
        if (_dragging && @event is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            _dragging = false;
            _dragOutTimer = 0;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int slot = HitTestSlot(motion.Position);
            _hoveredSlot = slot;

            // VB6 ListBox behavior: drag with left button held changes selection
            if (_dragging && slot >= 0 && slot < MaxSpells)
            {
                _selectedSlot = slot;

                // Auto-scroll when dragging near edges
                int lineIndex = (int)((motion.Position.Y - 2) / LineHeight);
                if (lineIndex <= 0 && _scrollOffset > 0)
                    _scrollOffset--;
                else if (lineIndex >= VisibleLines - 1 && _scrollOffset + VisibleLines < MaxSpells)
                    _scrollOffset++;
            }
        }
        else if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    int slot = HitTestSlot(mb.Position);
                    if (slot >= 0 && slot < MaxSpells)
                    {
                        _selectedSlot = slot;
                        _dragging = true;
                    }
                }
                else
                {
                    _dragging = false;
                }
                AcceptEvent();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                if (_scrollOffset > 0) _scrollOffset--;
                AcceptEvent();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (_scrollOffset + VisibleLines < MaxSpells) _scrollOffset++;
                AcceptEvent();
            }
        }
    }

    /// <summary>
    /// Cast selected spell — sends LH packet and sets UsingSkill for targeting.
    /// </summary>
    public void CastSelected()
    {
        if (_state == null || _tcp == null) return;
        if (_selectedSlot >= 0 && _selectedSlot < MaxSpells &&
            _state.Spells[_selectedSlot].SpellId > 0)
        {
            _tcp.SendPacket(ClientPackets.WriteCastSpell((byte)(_selectedSlot + 1)));
            // VB6: UsingSkill = Magia (constant 2, the skill type ID)
            // NOT the spell slot — the slot is stored server-side via LH packet
            _state.UsingSkill = 2;
        }
    }

    /// <summary>
    /// Request info for selected spell — sends INFS packet.
    /// </summary>
    public void InfoSelected()
    {
        if (_state == null || _tcp == null) return;
        if (_selectedSlot >= 0 && _selectedSlot < MaxSpells &&
            _state.Spells[_selectedSlot].SpellId > 0)
        {
            _tcp.SendPacket(ClientPackets.WriteSpellInfo((byte)(_selectedSlot + 1)));
        }
    }

    /// <summary>
    /// Move selected spell up or down — sends DESPHE packet.
    /// direction: 1=up, 2=down
    /// </summary>
    public void MoveSpell(int direction)
    {
        if (_tcp == null) return;
        if (_selectedSlot >= 0 && _selectedSlot < MaxSpells)
        {
            _tcp.SendPacket(ClientPackets.WriteMoveSpell((byte)direction, (byte)(_selectedSlot + 1)));
        }
    }

    private int HitTestSlot(Vector2 pos)
    {
        int lineIndex = (int)((pos.Y - 2) / LineHeight);
        if (lineIndex < 0 || lineIndex >= VisibleLines) return -1;
        int slot = lineIndex + _scrollOffset;
        return (slot >= 0 && slot < MaxSpells) ? slot : -1;
    }
}

using Godot;
using System;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.UI;

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

    public int SelectedSlot => _selectedSlot;

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

            // Spell name
            if (spell.SpellId > 0)
            {
                string label = $"{slot + 1}. {spell.Name}";
                font.DrawText(this, 4, (int)lineY + 1, label, Colors.White);
            }
            else
            {
                font.DrawText(this, 4, (int)lineY + 1, $"{slot + 1}. (vacio)",
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

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int slot = HitTestSlot(motion.Position);
            _hoveredSlot = slot;
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                int slot = HitTestSlot(mb.Position);
                if (slot >= 0 && slot < MaxSpells)
                {
                    _selectedSlot = slot;
                }
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                if (_scrollOffset > 0) _scrollOffset--;
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
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
            _tcp.SendPacket($"LH{_selectedSlot + 1}");
            _state.UsingSkill = _selectedSlot + 1;
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
            _tcp.SendPacket($"INFS{_selectedSlot + 1}");
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
            _tcp.SendPacket($"DESPHE{direction},{_selectedSlot + 1}");
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

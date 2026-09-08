using Godot;

namespace ArgentumNextgen.Game;

public sealed class QuickSlot
{
    public bool Spell;
    public int Id;
    public string Name = "";
    public Key Key;

    // Resolve every invocation: inventory and spell order can change after assignment.
    public int Resolve(GameState state)
    {
        if (Id <= 0) return -1;
        if (Spell)
        {
            for (int i = 0; i < state.Spells.Length; i++)
                if (state.Spells[i]?.SpellId == Id) return i;
        }
        else
        {
            for (int i = 0; i < state.Inventory.Length; i++)
                if (state.Inventory[i]?.ObjIndex == Id && state.Inventory[i].Amount > 0) return i;
        }
        return -1;
    }
}

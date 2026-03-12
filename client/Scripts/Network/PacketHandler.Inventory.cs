using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet handlers: Inventory / Spell slots / Equipment
/// </summary>
public partial class PacketHandler
{

    // ── Inventory / Spells ────────────────────────────────────────

    private void HandleBinChangeInventorySlot(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        short objIndex = bq.ReadInteger();
        string name = bq.ReadString();
        short amount = bq.ReadInteger();
        bool equipped = bq.ReadBoolean();
        short grhIndex = bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short maxDef = bq.ReadInteger();
        short minDef = bq.ReadInteger();
        float value = bq.ReadSingle();

        if (slot < 1 || slot > 25) return;

        if (objIndex <= 0)
        {
            _state.Inventory[slot - 1] = new InventorySlot();
        }
        else
        {
            _state.Inventory[slot - 1] = new InventorySlot
            {
                ObjIndex = objIndex,
                Name = name,
                Amount = amount,
                Equipped = equipped,
                GrhIndex = grhIndex,
                ObjType = objType,
                MaxHit = maxHit,
                MinHit = minHit,
                MaxDef = maxDef,
                MinDef = minDef,
                Value = (int)value,
            };
        }

        // VB6: Update per-equipment bottom bar labels
        // ObjType: 1=Weapon, 2=Armadura, 3=Escudo (shield), 31=Casco (helmet)
        if (equipped)
        {
            switch (objType)
            {
                case 1: // otWeapon
                    _state.WeaponEqpSlot = slot;
                    _state.WeaponLabel = $"{minHit}/{maxHit}";
                    break;
                case 2: // otArmadura
                    _state.ArmourEqpSlot = slot;
                    _state.ArmourLabel = $"{minDef}/{maxDef}";
                    break;
                case 3: // otEscudo (shield)
                    _state.ShieldEqpSlot = slot;
                    _state.ShieldLabel = $"{minDef}/{maxDef}";
                    break;
                case 31: // otCasco (helmet)
                    _state.HelmEqpSlot = slot;
                    _state.HelmLabel = $"{minDef}/{maxDef}";
                    break;
            }
        }
        else
        {
            // Unequipped — clear label if this was the equipped slot
            if (slot == _state.WeaponEqpSlot) { _state.WeaponEqpSlot = 0; _state.WeaponLabel = "0/0"; }
            if (slot == _state.ArmourEqpSlot) { _state.ArmourEqpSlot = 0; _state.ArmourLabel = "0/0"; }
            if (slot == _state.ShieldEqpSlot) { _state.ShieldEqpSlot = 0; _state.ShieldLabel = "0/0"; }
            if (slot == _state.HelmEqpSlot) { _state.HelmEqpSlot = 0; _state.HelmLabel = "0/0"; }
        }
    }


    private void HandleBinChangeBankSlot(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        short objIndex = bq.ReadInteger();
        string name = bq.ReadString();
        short amount = bq.ReadInteger();
        bool equipped = bq.ReadBoolean();
        short grhIndex = bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short maxDef = bq.ReadInteger();
        short minDef = bq.ReadInteger();
        float value = bq.ReadSingle();

        // Reset bank when first slot arrives (server sends all 40 slots before BankInit)
        if (slot == 1)
        {
            for (int i = 0; i < _state.BankItems.Length; i++)
                _state.BankItems[i] = new BankItem();
            _state.BankItemCount = 0;
        }

        // Search for existing slot to update in-place
        int existingIdx = -1;
        for (int i = 0; i < _state.BankItemCount; i++)
        {
            if (_state.BankItems[i].Slot == slot)
            {
                existingIdx = i;
                break;
            }
        }

        var item = new BankItem
        {
            Slot = slot, ObjIndex = objIndex, Name = name, Amount = amount,
            GrhIndex = grhIndex, ObjType = objType,
            MaxHit = maxHit, MinHit = minHit, MaxDef = maxDef, MinDef = minDef,
        };

        if (existingIdx >= 0)
        {
            if (objIndex <= 0 || amount <= 0)
            {
                // Slot emptied — remove by shifting
                for (int i = existingIdx; i < _state.BankItemCount - 1; i++)
                    _state.BankItems[i] = _state.BankItems[i + 1];
                _state.BankItems[_state.BankItemCount - 1] = new BankItem();
                _state.BankItemCount--;
            }
            else
            {
                _state.BankItems[existingIdx] = item;
            }
        }
        else
        {
            // Skip empty slots during init
            if (objIndex <= 0) return;

            int idx = _state.BankItemCount;
            if (idx >= 40) return;
            _state.BankItems[idx] = item;
            _state.BankItemCount++;
        }
    }


    private void HandleBinChangeSpellSlot(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        short spellIndex = bq.ReadInteger();
        string name = bq.ReadString();

        if (slot >= 1 && slot <= 20)
        {
            _state.Spells[slot - 1] = new SpellSlot
            {
                SpellId = spellIndex,
                Name = name,
            };
        }
    }

}

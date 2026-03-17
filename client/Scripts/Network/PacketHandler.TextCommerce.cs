using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Text-based packet handlers: Inventory, Spells, NPC Commerce, Bank, Trading
/// </summary>
public partial class PacketHandler
{

    // ── Inventory / Spells ────────────────────────────────────────

    private void HandleInventorySlot(string data)
    {
        // CSI<slot>,<objidx>,<name>,<amt>,<equipped>,<grh>,<type>,<maxhit>,<minhit>,<maxdef>,<valor>
        var parts = data.Split(',', 12);
        if (parts.Length < 2) return;

        int slot = ParseInt(parts[0]);
        if (slot < 1 || slot > 25) return;

        int objIndex = ParseInt(parts[1]);
        if (objIndex <= 0 || parts.Length < 5)
        {
            _state.Inventory[slot - 1] = new InventorySlot();
        }
        else if (parts.Length >= 11)
        {
            _state.Inventory[slot - 1] = new InventorySlot
            {
                ObjIndex = objIndex,
                Name = parts[2],
                Amount = ParseInt(parts[3]),
                Equipped = ParseInt(parts[4]) != 0,
                GrhIndex = ParseInt(parts[5]),
                ObjType = ParseInt(parts[6]),
                MaxHit = ParseInt(parts[7]),
                MinHit = ParseInt(parts[8]),
                MaxDef = ParseInt(parts[9]),
                Value = ParseInt(parts[10]),
            };
        }
    }

    private void HandleSpellSlot(string data)
    {
        var parts = data.Split(',', 4);
        if (parts.Length >= 3)
        {
            int slot = ParseInt(parts[0]);
            if (slot >= 1 && slot <= 20)
            {
                _state.Spells[slot - 1] = new SpellSlot
                {
                    SpellId = ParseInt(parts[1]),
                    Name = parts[2],
                };
            }
        }
    }

    private void HandleHideSpell(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            int slot = ParseInt(parts[0]);
            if (slot >= 1 && slot <= 20)
            {
                _state.Spells[slot - 1] = new SpellSlot
                {
                    SpellId = 0,
                    Name = parts[1],
                };
            }
        }
    }

    /// <summary>
    /// |S1{slot},{amount} — Partial inventory update: change item amount.
    /// </summary>
    private void HandlePartialInvAmount(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            int slot = ParseInt(parts[0]);
            int amount = ParseInt(parts[1]);
            if (slot >= 1 && slot <= 25)
            {
                _state.Inventory[slot - 1].Amount = amount;
                if (amount <= 0)
                {
                    _state.Inventory[slot - 1] = new InventorySlot();
                }
            }
        }
    }

    /// <summary>
    /// |S2{slot},{equipped} — Partial inventory update: change equip state.
    /// </summary>
    private void HandlePartialInvEquip(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            int slot = ParseInt(parts[0]);
            int equipped = ParseInt(parts[1]);
            if (slot >= 1 && slot <= 25)
            {
                _state.Inventory[slot - 1].Equipped = equipped != 0;
            }
        }
    }

    // ── Bank handlers ────────────────────────────────────────────

    private void HandleBankReset()
    {
        _state.BankItemCount = 0;
        for (int i = 0; i < 40; i++)
            _state.BankItems[i] = new BankItem();
    }

    private void HandleBankSlot(string data)
    {
        var parts = data.Split(',', 10);
        if (parts.Length < 9) return;

        int slotNum = ParseInt(parts[0]);
        int idx = _state.BankItemCount;
        if (idx >= 40) return;

        _state.BankItems[idx] = new BankItem
        {
            Slot = slotNum,
            ObjIndex = ParseInt(parts[1]),
            Name = parts[2],
            Amount = ParseInt(parts[3]),
            GrhIndex = ParseInt(parts[4]),
            ObjType = ParseInt(parts[5]),
            MaxHit = ParseInt(parts[6]),
            MinHit = ParseInt(parts[7]),
            MaxDef = ParseInt(parts[8]),
        };
        _state.BankItemCount++;
    }

    private void HandleInitBanco()
    {
        _state.Banqueando = true;
        // Mark that the next bank open should clear stale data before populating
        _bankLoadPending = true;
        GD.Print($"[PKT] INITBANCO: Bank opened ({_state.BankItemCount} items, gold={_state.BankGold})");
    }

    private void HandleBancoOk(string data)
    {
        GD.Print($"[PKT] BANCOOK: {data}");
    }

    private void HandleFinBanOk()
    {
        _state.Banqueando = false;
        _state.BovedaAbierta = false;
        GD.Print("[PKT] FINBANOK: Bank closed");
    }

    private void HandleBankGold(string data)
    {
        if (long.TryParse(data.Trim(), out long v))
            _state.BankGold = v;
    }

    // ── NPC Commerce handlers ────────────────────────────────────

    private void HandleNpcReset()
    {
        _state.NpcShopCount = 0;
        for (int i = 0; i < 50; i++)
            _state.NpcShopItems[i] = new NpcShopItem();
    }

    private void HandleNpcItem(string data)
    {
        var parts = data.Split(',', 11);
        if (parts.Length < 10) return;

        int idx = _state.NpcShopCount;
        if (idx >= 50) return;

        _state.NpcShopItems[idx] = new NpcShopItem
        {
            Name = parts[0],
            Amount = ParseInt(parts[1]),
            Price = long.TryParse(parts[2].Trim(), out long p) ? p : 0,
            GrhIndex = ParseInt(parts[3]),
            ObjIndex = ParseInt(parts[4]),
            ObjType = ParseInt(parts[5]),
            MaxHit = ParseInt(parts[6]),
            MinHit = ParseInt(parts[7]),
            MaxDef = ParseInt(parts[8]),
            Slot = ParseInt(parts[9]),
        };
        _state.NpcShopCount++;
    }

    private void HandleNpcSlotUpdate(string data)
    {
        var parts = data.Split(',', 12);
        if (parts.Length < 11) return;

        int targetSlot = ParseInt(parts[0]);
        for (int i = 0; i < _state.NpcShopCount; i++)
        {
            if (_state.NpcShopItems[i].Slot == targetSlot)
            {
                _state.NpcShopItems[i].Name = parts[1];
                _state.NpcShopItems[i].Amount = ParseInt(parts[2]);
                _state.NpcShopItems[i].Price = long.TryParse(parts[3].Trim(), out long p) ? p : 0;
                _state.NpcShopItems[i].GrhIndex = ParseInt(parts[4]);
                _state.NpcShopItems[i].ObjIndex = ParseInt(parts[5]);
                _state.NpcShopItems[i].ObjType = ParseInt(parts[6]);
                _state.NpcShopItems[i].MaxHit = ParseInt(parts[7]);
                _state.NpcShopItems[i].MinHit = ParseInt(parts[8]);
                _state.NpcShopItems[i].MaxDef = ParseInt(parts[9]);
                return;
            }
        }
    }

    private void HandleInitCom()
    {
        if (_state.Trading) _state.Trading = false; // mutual exclusion
        _state.Comerciando = true;
        GD.Print($"[PKT] INITCOM: NPC shop opened ({_state.NpcShopCount} items)");
    }

    private void HandleTransOk(string data)
    {
        GD.Print($"[PKT] TRANSOK: {data}");
    }

    private void HandleFinComOk()
    {
        _state.Comerciando = false;
        GD.Print("[PKT] FINCOMOK: NPC shop closed");
    }

    // ── Trading (player-to-player) ───────────────────────────────

    private void HandleTradeInit()
    {
        if (_state.Comerciando) _state.Comerciando = false; // mutual exclusion
        _state.Trading = true;
        GD.Print("[PKT] ICO: Trade initiated");
    }

    private void HandleTradeOk()
    {
        _state.Trading = false;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Comercio exitoso.",
            Color = "00FF00"
        });
        GD.Print("[PKT] TRADEOK: Trade completed");
    }

    private void HandleTradeCancelled()
    {
        _state.Trading = false;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Comercio cancelado.",
            Color = "FF0000"
        });
        GD.Print("[PKT] CANCELTRADE: Trade cancelled");
    }

    private void HandleTradeGold(string data)
    {
        int gold = ParseInt(data);
        GD.Print($"[PKT] IOR: Trade gold offer: {gold}");
    }

    private void HandleTradeItem(string data)
    {
        GD.Print($"[PKT] ICI: Trade item: {data}");
    }

    private void HandleTradeChat(string data)
    {
        _state.ChatMessages.Enqueue(new ChatMessage { Text = data, Color = "FFFFFF" });
    }

}

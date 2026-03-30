using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet handlers: Commerce / Bank / Trade / Crafting
/// </summary>
public partial class PacketHandler
{

    // ── Commerce / Bank ───────────────────────────────────────────

    private void HandleBinBankInit(ByteQueue bq)
    {
        int bankGold = bq.ReadLong();
        _state.BankGold = bankGold;
        _state.Banqueando = true;
        // Mark that the next bank open should clear stale data before populating
        _bankLoadPending = true;
    }


    private void HandleBinUserCommerceInit(ByteQueue bq)
    {
        string otherName = bq.ReadString();
        if (_state.Comerciando) _state.Comerciando = false; // mutual exclusion
        _state.Trading = true;
        _state.TradePartnerName = otherName;
        _state.TradeJustOpened = true;
        _state.TradePartnerAccepted = false;
        _state.MyTradeSlotCount = 0;
        _state.PartnerTradeSlotCount = 0;
        _state.MyTradeGold = 0;
        _state.PartnerTradeGold = 0;
    }


    private void HandleBinCommerceChat(ByteQueue bq)
    {
        string chat = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = "FFFFFF" });
    }

    // ── Stats ─────────────────────────────────────────────────────


    private void HandleBinChangeNpcInvSlot(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        string name = bq.ReadString();
        short amount = bq.ReadInteger();
        float value = bq.ReadSingle();
        int grhIndex = (ushort)bq.ReadInteger();
        short objIndex = bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short maxDef = bq.ReadInteger();
        short minDef = bq.ReadInteger();

        // Search for existing slot to update in-place
        int existingIdx = -1;
        for (int i = 0; i < _state.NpcShopCount; i++)
        {
            if (_state.NpcShopItems[i].Slot == slot)
            {
                existingIdx = i;
                break;
            }
        }

        var item = new NpcShopItem
        {
            Name = name, Amount = amount, Price = (long)value,
            GrhIndex = grhIndex, ObjIndex = objIndex, ObjType = objType,
            MaxHit = maxHit, MinHit = minHit, MaxDef = maxDef, MinDef = minDef, Slot = slot,
        };

        if (existingIdx >= 0)
        {
            if (amount <= 0 || objIndex <= 0)
            {
                // Item depleted — remove by shifting
                for (int i = existingIdx; i < _state.NpcShopCount - 1; i++)
                    _state.NpcShopItems[i] = _state.NpcShopItems[i + 1];
                _state.NpcShopItems[_state.NpcShopCount - 1] = new NpcShopItem();
                _state.NpcShopCount--;
            }
            else
            {
                _state.NpcShopItems[existingIdx] = item;
            }
        }
        else
        {
            // New slot (init flow) — append
            int idx = _state.NpcShopCount;
            if (idx >= 50) return;
            _state.NpcShopItems[idx] = item;
            _state.NpcShopCount++;
        }
    }

    // ── Misc stat packets ─────────────────────────────────────────


    private void HandleBinTradeOk()
    {
        _state.Trading = false;
        _state.MyTradeSlotCount = 0;
        _state.PartnerTradeSlotCount = 0;
        _state.MyTradeGold = 0;
        _state.PartnerTradeGold = 0;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = "Comercio exitoso.", Color = "00FF00" });
    }


    private void HandleBinChangeUserTradeSlot(ByteQueue bq)
    {
        byte offerSlot = bq.ReadByte();
        short objIndex = bq.ReadInteger();
        int amount = bq.ReadLong();
        int grhIndex = (ushort)bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short maxDef = bq.ReadInteger();
        short minDef = bq.ReadInteger();
        int value = bq.ReadLong();
        string name = bq.ReadString();

        // Store in GameState (offerSlot is 1-based)
        if (offerSlot >= 1 && offerSlot <= 10)
        {
            int idx = offerSlot - 1;
            _state.MyTradeSlots[idx] = new TradeOfferSlot
            {
                ObjIndex = objIndex, Amount = amount, GrhIndex = grhIndex,
                ObjType = objType, MaxHit = maxHit, MinHit = minHit,
                MaxDef = maxDef, MinDef = minDef, Value = value, Name = name
            };
            if (idx >= _state.MyTradeSlotCount)
                _state.MyTradeSlotCount = idx + 1;
            _state.TradePartnerAccepted = false;
        }
    }


    private void HandleBinCancelOfferItem(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        // Remove the slot from our offer (slot is 1-based)
        if (slot >= 1 && slot <= 10)
        {
            int idx = slot - 1;
            // Shift remaining slots down
            for (int i = idx; i < _state.MyTradeSlotCount - 1; i++)
                _state.MyTradeSlots[i] = _state.MyTradeSlots[i + 1];
            if (_state.MyTradeSlotCount > 0)
            {
                _state.MyTradeSlots[_state.MyTradeSlotCount - 1] = new TradeOfferSlot();
                _state.MyTradeSlotCount--;
            }
            _state.TradePartnerAccepted = false;
        }
    }


    /// <summary>
    /// SmithWeapons/SmithArmors/CarpItems (IDs 158/159/160) — VB6 13.3 binary craft list.
    /// Smith: count, per item: name(str), grh(i16), lingH(i16), lingP(i16), lingO(i16), objIdx(i16), upgrade(i16)
    /// Carp:  count, per item: name(str), grh(i16), madera(i16), maderaElf(i16), objIdx(i16), upgrade(i16)
    /// </summary>
    private void HandleBinCraftList(ByteQueue bq, System.Collections.Generic.List<CraftEntry> list, bool hasThreeMats)
    {
        list.Clear();
        int count = bq.ReadInteger();
        for (int i = 0; i < count; i++)
        {
            var entry = new CraftEntry();
            entry.Name = bq.ReadString();
            entry.GrhIndex = bq.ReadInteger();
            entry.Mat1 = bq.ReadInteger();
            entry.Mat2 = bq.ReadInteger();
            if (hasThreeMats) entry.Mat3 = bq.ReadInteger();
            entry.ObjIndex = bq.ReadInteger();
            entry.Upgrade = bq.ReadInteger();
            list.Add(entry);
        }
    }

    /// <summary>
    /// Navigation (ID 162) — navigation mode data string.
    /// Wire: string data
    /// </summary>


    // ── Bank (legacy) ─────────────────────────────────────────────

    /// <summary>
    /// InitBankLegacy (ID 165) — legacy bank init. Wire: string data
    /// </summary>
    private void HandleBinBankReset(ByteQueue bq)
    {
        string data = bq.ReadString(); // consume wire field
        // Clear all bank slots (same as text SBR handler)
        for (int i = 0; i < _state.BankItems.Length; i++)
            _state.BankItems[i] = new BankItem();
        _state.BankItemCount = 0;
    }


    private void HandleBinInitBankLegacy(ByteQueue bq)
    {
        string data = bq.ReadString();
        _state.Banqueando = true;
    }

    /// <summary>
    /// BankSlotLegacy (ID 166) — legacy bank slot. Wire: same as ChangeBankSlot
    /// </summary>


    /// <summary>
    /// BankSlotLegacy (ID 166) — legacy bank slot. Wire: same as ChangeBankSlot
    /// </summary>
    private void HandleBinBankSlotLegacy(ByteQueue bq)
    {
        // Same layout as ChangeBankSlot (ID 47)
        HandleBinChangeBankSlot(bq);
    }

    // ── Commerce (legacy) ─────────────────────────────────────────

    /// <summary>
    /// NpcInvItem (ID 171) — NPC inventory item. Wire: same as ChangeNPCInventorySlot
    /// </summary>


    // ── Commerce (legacy) ─────────────────────────────────────────

    /// <summary>
    /// NpcInvItem (ID 171) — NPC inventory item. Wire: same as ChangeNPCInventorySlot
    /// </summary>
    private void HandleBinNpcInvItem(ByteQueue bq)
    {
        // Same layout as ChangeNPCInventorySlot (ID 51)
        HandleBinChangeNpcInvSlot(bq);
    }

    /// <summary>
    /// TransactionOK (ID 174) — NPC commerce buy/sell confirmation.
    /// Wire: u8 slot, u8 tradeType (0=buy, 1=sell)
    /// </summary>


    /// <summary>
    /// TransactionOK (ID 174) — NPC commerce buy/sell confirmation.
    /// Wire: u8 slot, u8 tradeType (0=buy, 1=sell)
    /// </summary>
    private void HandleBinTransOk(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        byte tradeType = bq.ReadByte();
        string msg = tradeType == 0
            ? "Compra exitosa."
            : "Venta exitosa.";
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = "00FF00" });
    }

    // ── Tournament / Auction ──────────────────────────────────────


    // ── Tournament / Auction ──────────────────────────────────────

    private void HandleBinTournamentPoints(ByteQueue bq)
    {
        bq.ReadLong();
        return;
    }

    /// <summary>
    /// ResponseMsg (ID 177) — server response text (RESPUES opcode).
    /// Wire: string text, string name
    /// </summary>


    /// <summary>
    /// ResponseMsg (ID 177) — server response text (RESPUES opcode).
    /// Wire: string text, string name
    /// </summary>
    private void HandleBinResponseMsg(ByteQueue bq)
    {
        string text = bq.ReadString();
        string name = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "00FFFF" });
    }


    private void HandleBinAuctionBid(ByteQueue bq)
    {
        bq.ReadString();
        return;
    }

    // ── Trading (legacy) ──────────────────────────────────────────

    /// <summary>
    /// TradeInitLegacy (ID 180) — legacy trade init. Wire: string partnerName
    /// </summary>


    // ── Trading (legacy) ──────────────────────────────────────────

    /// <summary>
    /// TradeInitLegacy (ID 180) — legacy trade init. Wire: string partnerName
    /// </summary>
    private void HandleBinTradeInitLegacy(ByteQueue bq)
    {
        string partnerName = bq.ReadString();
        if (_state.Comerciando) _state.Comerciando = false; // mutual exclusion
        _state.Trading = true;
        _state.TradePartnerName = partnerName;
        _state.TradeJustOpened = true;
        _state.TradePartnerAccepted = false;
        _state.MyTradeSlotCount = 0;
        _state.PartnerTradeSlotCount = 0;
        _state.MyTradeGold = 0;
        _state.PartnerTradeGold = 0;
    }

    /// <summary>
    /// TradeOfferRecv (ID 181) — trade gold offer received. Wire: i32 gold
    /// </summary>


    /// <summary>
    /// TradeOfferRecv (ID 181) — trade gold offer received. Wire: i32 gold
    /// </summary>
    private void HandleBinTradeOfferRecv(ByteQueue bq)
    {
        int gold = bq.ReadLong();
        _state.PartnerTradeGold = gold;
        _state.TradePartnerAccepted = false;
    }

    /// <summary>
    /// TradeItems (ID 182) — trade item info. Wire: i16 objIndex, i16 amount, string name
    /// </summary>


    /// <summary>
    /// TradeItems (ID 182) — trade item info. Wire: i16 objIndex, i16 amount, string name
    /// </summary>
    private void HandleBinTradeItems(ByteQueue bq)
    {
        short objIndex = bq.ReadInteger();
        short amount = bq.ReadInteger();
        string name = bq.ReadString();

        // Store partner item in GameState
        if (_state.PartnerTradeSlotCount < 10)
        {
            _state.PartnerTradeSlots[_state.PartnerTradeSlotCount] = new TradeOfferSlot
            {
                ObjIndex = objIndex, Amount = amount, Name = name
            };
            _state.PartnerTradeSlotCount++;
            _state.TradePartnerAccepted = false;
        }
    }

    /// <summary>
    /// TradeChatMsgLegacy (ID 183) — legacy trade chat message. Wire: string chat
    /// </summary>


    /// <summary>
    /// TradeChatMsgLegacy (ID 183) — legacy trade chat message. Wire: string chat
    /// </summary>
    private void HandleBinTradeChatLegacy(ByteQueue bq)
    {
        string chat = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = "FFFFFF" });
    }

    // ── Guild ─────────────────────────────────────────────────────

    /// <summary>
    /// GuildInfoLeader / GuildInfoMember / GuildDetailsResp — guild data as raw string.
    /// Wire: string data
    /// </summary>

}

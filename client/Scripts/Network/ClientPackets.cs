namespace TierrasSagradasAO.Network;

/// <summary>
/// Builds binary client→server packets using ByteQueue.
/// Each method returns a byte[] ready to send via AoTcpClient.SendPacket().
/// </summary>
public static class ClientPackets
{
    // ── Pre-login ──────────────────────────────────────────────

    public static byte[] WriteKerd22(string hdSerial = "")
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.KERD22);
        bq.WriteString(hdSerial);
        return bq.ToArray();
    }

    public static byte[] WriteAlogin(string account, string password)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ALOGIN);
        bq.WriteString(account);
        bq.WriteString(password);
        return bq.ToArray();
    }

    public static byte[] WriteNlogin(string charName, byte race, byte gender, byte charClass, short head, byte homeland, string account)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.NLOGIN);
        bq.WriteString(charName);
        bq.WriteByte(race);
        bq.WriteByte(gender);
        bq.WriteByte(charClass);
        bq.WriteInteger(head);
        bq.WriteByte(homeland);
        bq.WriteString(account);
        return bq.ToArray();
    }

    public static byte[] WriteOologi(string charName, string account, string codex)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.OOLOGI);
        bq.WriteString(charName);
        bq.WriteString(account);
        bq.WriteString(codex);
        return bq.ToArray();
    }

    public static byte[] WriteThcjxd(string charName, string account, string codex)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.THCJXD);
        bq.WriteString(charName);
        bq.WriteString(account);
        bq.WriteString(codex);
        return bq.ToArray();
    }

    public static byte[] WriteNaccnt(string account, string password, string pin)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.NACCNT);
        bq.WriteString(account);
        bq.WriteString(password);
        bq.WriteString(pin);
        return bq.ToArray();
    }

    public static byte[] WriteRepass(string oldPass, string newPass)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.REPASS);
        bq.WriteString(oldPass);
        bq.WriteString(newPass);
        return bq.ToArray();
    }

    public static byte[] WriteReecuh(string account, string pin)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.REECUH);
        bq.WriteString(account);
        bq.WriteString(pin);
        return bq.ToArray();
    }

    public static byte[] WriteTirdad()
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.TIRDAD);
        return bq.ToArray();
    }

    public static byte[] WriteTbrp(string charName)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.TBRP);
        bq.WriteString(charName);
        return bq.ToArray();
    }

    // ── Movement ───────────────────────────────────────────────

    public static byte[] WriteWalk(byte heading)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.Walk);
        bq.WriteByte(heading);
        return bq.ToArray();
    }

    public static byte[] WriteChangeHeading(byte heading)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ChangeHeading);
        bq.WriteByte(heading);
        return bq.ToArray();
    }

    public static byte[] WriteRequestPos()
    {
        return new byte[] { ClientPacketId.RequestPos };
    }

    public static byte[] WriteActualizar()
    {
        return new byte[] { ClientPacketId.Actualizar };
    }

    // ── Combat ─────────────────────────────────────────────────

    public static byte[] WriteAttack()
    {
        return new byte[] { ClientPacketId.Attack };
    }

    public static byte[] WriteCastSpell(byte slot)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CastSpell);
        bq.WriteByte(slot);
        return bq.ToArray();
    }

    public static byte[] WriteLeftClick(byte x, byte y)
    {
        return new byte[] { ClientPacketId.LeftClick, x, y };
    }

    public static byte[] WriteRightClick(byte x, byte y)
    {
        return new byte[] { ClientPacketId.RightClick, x, y };
    }

    public static byte[] WriteWorkLeftClick(byte x, byte y, byte skill)
    {
        return new byte[] { ClientPacketId.WorkLeftClick, x, y, skill };
    }

    // ── Chat ───────────────────────────────────────────────────

    public static byte[] WriteTalk(string msg)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.Talk);
        bq.WriteString(msg);
        return bq.ToArray();
    }

    public static byte[] WriteYell(string msg)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.Yell);
        bq.WriteString(msg);
        return bq.ToArray();
    }

    public static byte[] WriteWhisper(string target, string msg)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.Whisper);
        bq.WriteString(target);
        bq.WriteString(msg);
        return bq.ToArray();
    }

    public static byte[] WriteSlashCommand(string cmd)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.SlashCommand);
        bq.WriteString(cmd);
        return bq.ToArray();
    }

    // ── Items ──────────────────────────────────────────────────

    public static byte[] WritePickUp()
    {
        return new byte[] { ClientPacketId.PickUp };
    }

    public static byte[] WriteDropItem(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.DropItem);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    public static byte[] WriteUseItem(byte slot)
    {
        return new byte[] { ClientPacketId.UseItem, slot };
    }

    public static byte[] WriteUseItemClick(byte slot)
    {
        return new byte[] { ClientPacketId.UseItemClick, slot };
    }

    public static byte[] WriteEquipItem(byte slot)
    {
        return new byte[] { ClientPacketId.EquipItem, slot };
    }

    public static byte[] WriteSwapItems(byte from, byte to)
    {
        return new byte[] { ClientPacketId.SwapItems, from, to };
    }

    public static byte[] WriteMouseDrop(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.MouseDrop);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    // ── Skills ─────────────────────────────────────────────────

    public static byte[] WriteUseSkill(byte skillId)
    {
        return new byte[] { ClientPacketId.UseSkill, skillId };
    }

    public static byte[] WriteSkillSet(byte[] skillPoints)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.SkillSet);
        for (int i = 0; i < 20; i++)
        {
            bq.WriteByte(i < skillPoints.Length ? skillPoints[i] : (byte)0);
        }
        return bq.ToArray();
    }

    public static byte[] WriteMeditate()
    {
        return new byte[] { ClientPacketId.Meditate };
    }

    public static byte[] WriteSafeToggle()
    {
        return new byte[] { ClientPacketId.SafeToggle };
    }

    // ── Spells ─────────────────────────────────────────────────

    public static byte[] WriteSpellInfo(byte slot)
    {
        return new byte[] { ClientPacketId.SpellInfo, slot };
    }

    public static byte[] WriteMoveSpell(byte from, byte to)
    {
        return new byte[] { ClientPacketId.MoveSpell, from, to };
    }

    // ── Commerce ───────────────────────────────────────────────

    public static byte[] WriteCommerceBuy(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CommerceBuy);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    public static byte[] WriteCommerceSell(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CommerceSell);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    public static byte[] WriteCommerceClose()
    {
        return new byte[] { ClientPacketId.CommerceClose };
    }

    // ── Banking ────────────────────────────────────────────────

    public static byte[] WriteBankDeposit(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.BankDeposit);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    public static byte[] WriteBankWithdraw(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.BankWithdraw);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    public static byte[] WriteBankClose()
    {
        return new byte[] { ClientPacketId.BankClose };
    }

    // ── Guild Bank ────────────────────────────────────────────

    public static byte[] WriteGuildBankDepositItem(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ClanBankDepositItem);
        bq.WriteString($"{slot},{amount}");
        return bq.ToArray();
    }

    public static byte[] WriteGuildBankWithdrawItem(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ClanBankWithdrawItem);
        bq.WriteString($"{slot},{amount}");
        return bq.ToArray();
    }

    public static byte[] WriteGuildBankDepositGold(int amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.GuildBankDeposit);
        bq.WriteString(amount.ToString());
        return bq.ToArray();
    }

    public static byte[] WriteGuildBankWithdrawGold(int amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.GuildBankWithdraw);
        bq.WriteString(amount.ToString());
        return bq.ToArray();
    }

    public static byte[] WriteGuildBankClose()
    {
        return new byte[] { ClientPacketId.CloseGuildBank };
    }

    // ── Crafting ───────────────────────────────────────────────

    public static byte[] WriteConstructSmith(short item)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ConstructSmith);
        bq.WriteInteger(item);
        return bq.ToArray();
    }

    public static byte[] WriteConstructCarp(short item)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ConstructCarp);
        bq.WriteInteger(item);
        return bq.ToArray();
    }

    public static byte[] WriteTrainCreature(byte pet)
    {
        return new byte[] { ClientPacketId.TrainCreature, pet };
    }

    // ── Guild ──────────────────────────────────────────────────

    public static byte[] WriteGuildInfo()
    {
        return new byte[] { ClientPacketId.GuildInfo };
    }

    /// <summary>Guild creation form data: CIG &lt;desc&gt;BF&lt;name&gt;BF&lt;url&gt;BF&lt;codexCount&gt;BF&lt;codex...&gt;</summary>
    public static byte[] WriteGuildCreate(string data)
    {
        return WriteStringPacket(ClientPacketId.GuildCreate, data);
    }

    /// <summary>Update guild codex/desc: DESCOD data</summary>
    public static byte[] WriteGuildUpdateCodex(string data)
    {
        return WriteStringPacket(ClientPacketId.GuildUpdateCodex, data);
    }

    /// <summary>Accept applicant: ACEPTARI name</summary>
    public static byte[] WriteGuildAccept(string applicantName)
    {
        return WriteStringPacket(ClientPacketId.GuildAccept, applicantName);
    }

    /// <summary>Reject applicant: RECHAZAR name,reason</summary>
    public static byte[] WriteGuildReject(string data)
    {
        return WriteStringPacket(ClientPacketId.GuildReject, data);
    }

    /// <summary>Expel member: ECHARCLA name</summary>
    public static byte[] WriteGuildExpel(string memberName)
    {
        return WriteStringPacket(ClientPacketId.GuildExpel, memberName);
    }

    /// <summary>Update guild news: ACTGNEWS text</summary>
    public static byte[] WriteGuildNews(string news)
    {
        return WriteStringPacket(ClientPacketId.GuildNewsReq, news);
    }

    /// <summary>Apply to join guild: SOLICITUD guildName,petition</summary>
    public static byte[] WriteGuildApply(string data)
    {
        return WriteStringPacket(ClientPacketId.GuildApply, data);
    }

    /// <summary>Request guild details: CLANDETAILS guildName</summary>
    public static byte[] WriteGuildDetails(string guildName)
    {
        return WriteStringPacket(ClientPacketId.GuildDetails, guildName);
    }

    public static byte[] WriteStringPacket(byte packetId, string data)
    {
        var bq = new ByteQueue();
        bq.WriteByte(packetId);
        bq.WriteString(data);
        return bq.ToArray();
    }

    // ── Friends ────────────────────────────────────────────────

    public static byte[] WriteFriendAdd(string name)
    {
        return System.Array.Empty<byte>();
    }

    public static byte[] WriteFriendRemove(string name)
    {
        return System.Array.Empty<byte>();
    }

    // ── Trade ──────────────────────────────────────────────────

    public static byte[] WriteTradeOfferGold(int amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.TradeOfferGold);
        bq.WriteLong(amount);
        return bq.ToArray();
    }

    public static byte[] WriteTradeOfferItem(byte slot, short amount)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.TradeOfferItem);
        bq.WriteByte(slot);
        bq.WriteInteger(amount);
        return bq.ToArray();
    }

    public static byte[] WriteTradeResponse(byte response)
    {
        return new byte[] { ClientPacketId.TradeResponse, response };
    }

    public static byte[] WriteTradeCancel()
    {
        return new byte[] { ClientPacketId.TradeCancel };
    }

    public static byte[] WriteTradeChat(string msg)
    {
        return WriteStringPacket(ClientPacketId.TradeChat, msg);
    }

    // ── Quest ──────────────────────────────────────────────────

    public static byte[] WriteQuestList()
    {
        return System.Array.Empty<byte>();
    }

    // ── Misc ───────────────────────────────────────────────────

    public static byte[] WriteMiniStats()
    {
        return new byte[] { ClientPacketId.MiniStatsReq };
    }

    public static byte[] WriteSendPoints()
    {
        return new byte[] { ClientPacketId.SendPoints };
    }

    public static byte[] WriteDuelArenaInfo()
    {
        return System.Array.Empty<byte>();
    }
}

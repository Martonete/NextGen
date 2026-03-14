namespace ArgentumNextgen.Network;

/// <summary>
/// Builds binary client→server packets using ByteQueue.
/// Each method returns a byte[] ready to send via AoTcpClient.SendPacket().
/// </summary>
public static class ClientPackets
{
    // ── Pre-login ──────────────────────────────────────────────

    public static byte[] WriteHardwareCheck(string hdSerial = "")
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.HardwareCheck);
        bq.WriteString(hdSerial);
        return bq.ToArray();
    }

    public static byte[] WriteAccountLogin(string account, string password)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.AccountLogin);
        bq.WriteString(account);
        bq.WriteString(password);
        return bq.ToArray();
    }

    public static byte[] WriteCreateCharacter(string charName, byte race, byte gender, byte charClass, short head, byte homeland, string account)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CreateCharacter);
        bq.WriteString(charName);
        bq.WriteByte(race);
        bq.WriteByte(gender);
        bq.WriteByte(charClass);
        bq.WriteInteger(head);
        bq.WriteByte(homeland);
        bq.WriteString(account);
        return bq.ToArray();
    }

    public static byte[] WriteCharacterLogin(string charName, string account, string codex)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CharacterLogin);
        bq.WriteString(charName);
        bq.WriteString(account);
        bq.WriteString(codex);
        return bq.ToArray();
    }

    public static byte[] WriteCharacterSelect(string charName, string account, string codex)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CharacterSelect);
        bq.WriteString(charName);
        bq.WriteString(account);
        bq.WriteString(codex);
        return bq.ToArray();
    }

    public static byte[] WriteCreateAccount(string account, string password, string pin)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.CreateAccount);
        bq.WriteString(account);
        bq.WriteString(password);
        bq.WriteString(pin);
        return bq.ToArray();
    }

    public static byte[] WriteChangePassword(string oldPass, string newPass)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ChangePassword);
        bq.WriteString(oldPass);
        bq.WriteString(newPass);
        return bq.ToArray();
    }

    public static byte[] WriteAccountRecovery(string account, string pin)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.AccountRecovery);
        bq.WriteString(account);
        bq.WriteString(pin);
        return bq.ToArray();
    }

    public static byte[] WriteRollDice()
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.RollDice);
        return bq.ToArray();
    }

    public static byte[] WriteDeleteCharacter(string charName)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.DeleteCharacter);
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

    public static byte[] WriteSyncPosition()
    {
        return new byte[] { ClientPacketId.SyncPosition };
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

    // ── Player info ───────────────────────────────────────────

    /// <summary>
    /// Request character info (DAMINF). Server responds with FullCharInfo (ID 245).
    /// </summary>
    public static byte[] WritePlayerInfo(string targetName)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.PlayerInfo);
        bq.WriteString(targetName);
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

    // ── Forum ──────────────────────────────────────────────────────

    /// <summary>
    /// ForumPost (ID 123) — post a message to a forum board.
    /// Wire: u8 opcode, u8 msgType, string title, string body
    /// msgType: 0=General, 1=GeneralSticky, 2=Caos, 3=CaosSticky, 4=Real, 5=RealSticky
    /// </summary>
    public static byte[] WriteForumPost(byte msgType, string title, string body)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.ForumPost);
        bq.WriteByte(msgType);
        bq.WriteString(title);
        bq.WriteString(body);
        return bq.ToArray();
    }

    // ── Quest ──────────────────────────────────────────────────────

    /// <summary>
    /// QuestList (ID 120) — request quest list from server.
    /// </summary>
    public static byte[] WriteQuestList()
    {
        return new byte[] { ClientPacketId.QuestList };
    }

    /// <summary>
    /// QuestInfo (ID 121) — request quest detail for a specific quest.
    /// Wire: u8 opcode, string questId
    /// </summary>
    public static byte[] WriteQuestInfo(int questId)
    {
        return WriteStringPacket(ClientPacketId.QuestInfo, questId.ToString());
    }

    /// <summary>
    /// QuestAccept (ID 122) — accept or abandon a quest.
    /// Wire: u8 opcode, string "questId|action" (action: 1=accept, 0=abandon)
    /// </summary>
    public static byte[] WriteQuestAccept(int questId, bool accept)
    {
        return WriteStringPacket(ClientPacketId.QuestAccept, $"{questId}|{(accept ? "1" : "0")}");
    }

    /// <summary>
    /// Train (wraps TrainCreature ID 92) — train a creature at trainer NPC.
    /// </summary>
    public static byte[] WriteTrain(int creatureIndex)
    {
        return new byte[] { ClientPacketId.TrainCreature, (byte)creatureIndex };
    }

    // ── Mail ──────────────────────────────────────────────────────

    /// <summary>
    /// MailSend (ID 125) — send a mail message.
    /// Wire: u8 opcode, string recipient, string subject, string body
    /// </summary>
    public static byte[] WriteMailSend(string recipient, string subject, string body)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.MailSend);
        bq.WriteString(recipient);
        bq.WriteString(subject);
        bq.WriteString(body);
        return bq.ToArray();
    }

    /// <summary>
    /// MailOpen (ID 126) — request to open mail inbox.
    /// Wire: u8 opcode
    /// </summary>
    public static byte[] WriteMailOpen()
    {
        return new byte[] { ClientPacketId.MailOpen };
    }

    /// <summary>
    /// MailExtract (ID 127) — extract attached items/gold from a mail.
    /// Wire: u8 opcode, i16 mailId
    /// </summary>
    public static byte[] WriteMailExtract(int mailId)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.MailExtract);
        bq.WriteInteger((short)mailId);
        return bq.ToArray();
    }

    /// <summary>
    /// MailDelete (ID 128) — delete a mail message.
    /// Wire: u8 opcode, i16 mailId
    /// </summary>
    public static byte[] WriteMailDelete(int mailId)
    {
        var bq = new ByteQueue();
        bq.WriteByte(ClientPacketId.MailDelete);
        bq.WriteInteger((short)mailId);
        return bq.ToArray();
    }

}

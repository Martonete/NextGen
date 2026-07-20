using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet handlers: Chat / Guild / Party / Forum / Quest
/// </summary>
public partial class PacketHandler
{

    // ── Chat ──────────────────────────────────────────────────────

    private void HandleBinChatOverHead(ByteQueue bq)
    {
        string chat = bq.ReadString();
        short charIndex = bq.ReadInteger();
        int color = bq.ReadLong();

        // Convert VB6 BGR int to hex RGB
        int r = color & 0xFF;
        int g = (color >> 8) & 0xFF;
        int b = (color >> 16) & 0xFF;
        string hexColor = $"{r:X2}{g:X2}{b:X2}";

        if (charIndex > 0)
        {
            SetCharDialog(charIndex, chat, hexColor);

            // If this is an NPC speaking, also show it in the NPC dialog panel
            if (_state.Characters.TryGetValue(charIndex, out var ch) && ch.NpcNumber > 0)
            {
                // Extract clean name (strip clan tag if present)
                string npcName = ch.Name;
                int tagPos = npcName.IndexOf('<');
                if (tagPos >= 0) npcName = npcName.Substring(0, tagPos).Trim();

                _state.NpcDialogName = npcName;
                _state.NpcDialogText = chat;
                _state.ShowNpcDialog = true;
            }
        }
        else
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = hexColor, Type = ChatType.Global });
        }
    }


    private void HandleBinConsoleMsg(ByteQueue bq)
    {
        string chat = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        // Classify by font index: 19=FIGHT, 25=PARTY, 27/31=GUILD, 3/43-45=GLOBAL
        var type = ChatType.System;
        if (fontIndex == 19) type = ChatType.Combat;
        else if (fontIndex == 25) type = ChatType.Party;
        else if (fontIndex == 27 || fontIndex == 31) type = ChatType.Clan;
        else if (fontIndex == 3 || fontIndex == 43 || fontIndex == 44 || fontIndex == 45) type = ChatType.Global;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = color, Type = type });
    }


    private void HandleBinGuildChat(ByteQueue bq)
    {
        string chat = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = "00FF00", Type = ChatType.Clan });
    }

    // ── Character ─────────────────────────────────────────────────


    // ── Console message by ID ─────────────────────────────────────

    private void HandleBinConsoleMsgId(ByteQueue bq)
    {
        short msgId = bq.ReadInteger();
        string args = bq.ReadString();

        if (msgId >= 0 && msgId < _state.TextMessages.Length)
        {
            var tmpl = _state.TextMessages[msgId];
            string text = tmpl.Text;

            // Substitute %1 through %8 with @-separated args
            if (!string.IsNullOrEmpty(args))
            {
                var parts = args.Split('@');
                for (int i = 0; i < parts.Length && i < 8; i++)
                {
                    text = text.Replace($"%{i + 1}", parts[i]);
                }
            }

            string color = FontTypes.GetHexColor(tmpl.FontId);
            var type = ChatType.System;
            if (tmpl.FontId == 19) type = ChatType.Combat;
            else if (tmpl.FontId == 25) type = ChatType.Party;
            else if (tmpl.FontId == 27 || tmpl.FontId == 31) type = ChatType.Clan;
            else if (tmpl.FontId == 3 || tmpl.FontId == 43 || tmpl.FontId == 44 || tmpl.FontId == 45) type = ChatType.Global;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color, Type = type });
        }
        else
        {
            // Fallback: show raw
            string text = !string.IsNullOrEmpty(args) ? args : $"[MSG#{msgId}]";
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "45BE9C" });
        }
    }


    private void HandleBinGmBroadcast(ByteQueue bq)
    {
        string msg = bq.ReadString();
        // GM broadcasts show in red bold in console
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = "FF0000", Type = ChatType.Global });
    }

    // ── Chat variants (binary) ──────────────────────────────────

    private void HandleBinChatTalk(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        string msg = bq.ReadString();
        int color = bq.ReadLong();

        int r = color & 0xFF;
        int g = (color >> 8) & 0xFF;
        int b = (color >> 16) & 0xFF;
        string hexColor = $"{r:X2}{g:X2}{b:X2}";

        if (charIndex > 0)
        {
            SetCharDialog(charIndex, msg, hexColor);
        }
        else
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = hexColor, Type = ChatType.Global });
        }
    }


    private void HandleBinChatYell(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        string msg = bq.ReadString();
        int color = bq.ReadLong();

        int r = color & 0xFF;
        int g = (color >> 8) & 0xFF;
        int b = (color >> 16) & 0xFF;
        string hexColor = $"{r:X2}{g:X2}{b:X2}";

        if (charIndex > 0)
        {
            SetCharDialog(charIndex, msg, hexColor);
        }
        else
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = hexColor, Type = ChatType.Global });
        }
    }


    private void HandleBinChatWhisper(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color, Type = ChatType.Whisper });
    }


    private void HandleBinChatClan(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color, Type = ChatType.Clan });
    }


    // ── Chat (continued) ──────────────────────────────────────────

    private void HandleBinChatGuild2(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color, Type = ChatType.Clan });
    }

    // ── Guild ─────────────────────────────────────────────────────

    /// <summary>
    /// GuildInfoLeader / GuildInfoMember / GuildDetailsResp — guild data as raw string.
    /// Wire: string data
    /// </summary>
    private void HandleBinGuildInfoStr(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        _state.GuildInfoData = data;
        _state.GuildInfoType = tag;
        _state.ShowGuildPanel = true;
    }

    /// <summary>
    /// GuildBankPermsResp (ID 195) — guild bank permissions.
    /// Wire: bool canObj, bool canGold
    /// </summary>
    private void HandleBinGuildBankPermsResp(ByteQueue bq)
    {
        bool canObj = bq.ReadBoolean();
        bool canGold = bq.ReadBoolean();
        _state.GuildBankCanObj = canObj;
        _state.GuildBankCanGold = canGold;
    }

    /// <summary>
    /// ClanChatResp (ID 196) — clan chat message. Wire: string msg, u8 fontIndex
    /// </summary>
    private void HandleBinClanChatResp(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color, Type = ChatType.Clan });
    }

    /// <summary>
    /// GuildBankSlotData (ID 197) — full guild bank slot (SBG opcode).
    /// Wire: u8 slot, i16 objIdx, string name, i16 amount, i16 grh,
    ///        u8 objType, i16 maxHit, i16 minHit, i16 maxDef, i32 bankGold, i32 userGold
    /// </summary>
    private void HandleBinGuildBankSlotData(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        short objIdx = bq.ReadInteger();
        string name = bq.ReadString();
        short amount = bq.ReadInteger();
        int grh = (ushort)bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short maxDef = bq.ReadInteger();
        int bankGold = bq.ReadLong();
        int userGold = bq.ReadLong();

        int idx = slot - 1;
        if (idx >= 0 && idx < _state.GuildBankItems.Length)
        {
            _state.GuildBankItems[idx] ??= new GuildBankSlot();
            _state.GuildBankItems[idx].ObjIndex = objIdx;
            _state.GuildBankItems[idx].Name = name;
            _state.GuildBankItems[idx].Amount = amount;
            _state.GuildBankItems[idx].GrhIndex = grh;
            _state.GuildBankItems[idx].ObjType = objType;
            _state.GuildBankItems[idx].MaxHit = maxHit;
            _state.GuildBankItems[idx].MinHit = minHit;
            _state.GuildBankItems[idx].MaxDef = maxDef;
        }
        _state.GuildBankGold = bankGold;
    }

    // ── Quest ─────────────────────────────────────────────────────

    private void HandleBinQuestData(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        _state.QuestDataTag = tag;
        _state.QuestDataPayload = data;
    }

    // ── Misc data ─────────────────────────────────────────────────

    /// <summary>
    /// MenuData (ID 221) — right-click context menu (MENU opcode).
    /// Wire: string name, u8 priv
    /// </summary>
    private void HandleBinMenuData(ByteQueue bq)
    {
        string name = bq.ReadString();
        byte priv = bq.ReadByte();
        _state.MenuTargetName = name;
        _state.MenuTargetPriv = priv;
        _state.ShowContextMenu = true;
    }

    /// <summary>
    /// SelectData (ID 222) — selection list data (SELE opcode).
    /// Wire: string data
    /// </summary>
    private void HandleBinSelectData(ByteQueue bq)
    {
        string data = bq.ReadString();
        _state.SelectListData = data;
        _state.ShowSelectList = true;
    }

    /// <summary>
    /// MiniTopData (ID 223) — mini ranking data (MTOP opcode).
    /// Wire: string data
    /// </summary>
    private void HandleBinMiniTopData(ByteQueue bq)
    {
        string data = bq.ReadString();
        _state.MiniTopData = data;
        _state.ShowMiniTop = true;
    }

    /// <summary>
    /// Generic single-string packets (ImageData, BkwData, GinfData,
    /// IcoData, ZsosData, SbrData, AuctionList, CosmeticImage/Pcgn/Pcss/Pccc)
    /// — read one string and log.
    /// </summary>
    private void HandleBinStringOnly(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
    }

    /// <summary>
    /// EnchatData (ID 228) — enter chat room. Wire: string name
    /// </summary>
    private void HandleBinEnchatData(ByteQueue bq)
    {
        string name = bq.ReadString();
    }

    /// <summary>
    /// IrchatData (ID 229) — IRC-style chat message. Wire: string sender, string msg
    /// </summary>
    private void HandleBinIrchatData(ByteQueue bq)
    {
        string sender = bq.ReadString();
        string msg = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = $"[{sender}] {msg}", Color = "AAFFAA" });
    }


    // ── Cosmetics ─────────────────────────────────────────────────

    /// <summary>
    /// CosmeticSurgery (ID 238) — cosmetic surgery options. Wire: u8 raza, u8 genero
    /// </summary>


    // ── Cosmetics ─────────────────────────────────────────────────

    /// <summary>
    /// CosmeticSurgery (ID 238) — cosmetic surgery options. Wire: u8 raza, u8 genero
    /// </summary>
    private void HandleBinCosmeticSurgery(ByteQueue bq)
    {
        byte raza = bq.ReadByte();
        byte genero = bq.ReadByte();
    }

    // ── Guild bank ────────────────────────────────────────────────

    /// <summary>
    /// GuildBankInitResp (ID 247) — guild bank init (INITCBANK opcode).
    /// Wire: bool canObj, bool canGold
    /// </summary>
    private void HandleBinGuildBankInitResp(ByteQueue bq)
    {
        bool canObj = bq.ReadBoolean();
        bool canGold = bq.ReadBoolean();
        _state.GuildBankCanObj = canObj;
        _state.GuildBankCanGold = canGold;
        _state.ShowGuildBank = true;
    }

    /// <summary>
    /// GuildBankSlotResp (ID 248) — guild bank slot (BANCOBK opcode).
    /// Wire: u8 slot, u8 objType
    /// </summary>
    private void HandleBinGuildBankSlotResp(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        byte objType = bq.ReadByte();
    }

    // ── Ping ──────────────────────────────────────────────────────

    /// <summary>
    /// Ping (ID 250) — server→client ping request. Respond immediately with Pong.
    /// </summary>
    private void HandleBinPingRequest()
    {
        _state.PingSentMs = Time.GetTicksMsec();
        OnSendPacket?.Invoke(ClientPackets.WritePong());
    }

    // ── Arena ─────────────────────────────────────────────────────

    private void HandleBinArenaData(ByteQueue bq)
    {
        bq.ReadString();
        return;
    }

    // ── Forum ───────────────────────────────────────────────────

    /// <summary>
    /// AddForumMsg (ID 117) — server sends a forum post to accumulate before showing the form.
    /// Wire: u8 forumType, string title, string author, string message
    /// forumType: 0=General, 1=GeneralSticky, 2=Caos, 3=CaosSticky, 4=Real, 5=RealSticky
    /// </summary>
    private void HandleBinAddForumMsg(ByteQueue bq)
    {
        byte forumType = bq.ReadByte();
        string title = bq.ReadString();
        string author = bq.ReadString();
        string message = bq.ReadString();

        var post = new Game.ForumPostEntry
        {
            ForumType = forumType,
            Title = title,
            Author = author,
            Body = message,
            IsSticky = (forumType == 1 || forumType == 3 || forumType == 5)
        };

        _state.ForumPosts.Add(post);
    }

    /// <summary>
    /// ShowForumForm (ID 118) — server signals to open the forum UI.
    /// Wire: u8 visibility (bitflags: 1=General, 2=Caos, 4=Real), u8 canMakeSticky
    /// </summary>
    private void HandleBinShowForumForm(ByteQueue bq)
    {
        byte visibility = bq.ReadByte();
        byte canMakeSticky = bq.ReadByte();

        _state.ForumVisibility = visibility;
        _state.ForumCanMakeSticky = canMakeSticky;
        _state.ShowForumPanel = true;
    }


}

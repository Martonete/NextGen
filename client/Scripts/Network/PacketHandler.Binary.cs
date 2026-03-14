using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet dispatch — handles native binary packets (non-GenericText).
/// Each case reads exact typed fields from ByteQueue matching server binary_packets.rs.
/// </summary>
public partial class PacketHandler
{
    /// <summary>
    /// Dispatch a native binary packet by opcode.
    /// Throws InvalidOperationException if not enough bytes (partial packet — caller rolls back).
    /// </summary>
    private void HandleBinaryPacket(ByteQueue bq)
    {
        byte opcode = bq.ReadByte();

        switch (opcode)
        {
            // ── Auth / Login ──────────────────────────────────────
            case ServerPacketId.Logged: // 0
                HandleBinLogged(bq);
                break;
            case ServerPacketId.Disconnect: // 1
                HandleBinDisconnect(bq);
                break;
            case ServerPacketId.ErrorMsg: // 2
                HandleBinErrorMsg(bq);
                break;
            case ServerPacketId.ShowMessageBox: // 3
                HandleBinShowMessageBox(bq);
                break;
            case ServerPacketId.UserIndexInServer: // 4
                HandleBinUserIndex(bq);
                break;
            case ServerPacketId.UserCharIndexInServer: // 5
                HandleBinUserCharIndex(bq);
                break;

            // ── Commerce / Bank / Trade ───────────────────────────
            case ServerPacketId.CommerceEnd: // 6
                _state.Comerciando = false;
                break;
            case ServerPacketId.BankEnd: // 7
                _state.Banqueando = false;
                _state.BovedaAbierta = false;
                break;
            case ServerPacketId.CommerceInit: // 8
                _state.Comerciando = true;
                break;
            case ServerPacketId.BankInit: // 9
                HandleBinBankInit(bq);
                break;
            case ServerPacketId.UserCommerceInit: // 10
                HandleBinUserCommerceInit(bq);
                break;
            case ServerPacketId.UserCommerceEnd: // 11
                _state.Trading = false;
                _state.MyTradeSlotCount = 0;
                _state.PartnerTradeSlotCount = 0;
                _state.MyTradeGold = 0;
                _state.PartnerTradeGold = 0;
                break;
            case ServerPacketId.UserOfferConfirm: // 12
                _state.TradePartnerAccepted = true;
                GD.Print("[PKT] UserOfferConfirm");
                break;
            case ServerPacketId.CommerceChat: // 13
                HandleBinCommerceChat(bq);
                break;
            case ServerPacketId.ShowBlacksmithForm: // 14
                GD.Print("[PKT] ShowBlacksmithForm");
                _state.ShowBlacksmithForm = true;
                break;
            case ServerPacketId.ShowCarpenterForm: // 15
                GD.Print("[PKT] ShowCarpenterForm");
                _state.ShowCarpenterForm = true;
                break;

            // ── Stats ─────────────────────────────────────────────
            case ServerPacketId.UpdateSta: // 16
                HandleBinUpdateSta(bq);
                break;
            case ServerPacketId.UpdateMana: // 17
                HandleBinUpdateMana(bq);
                break;
            case ServerPacketId.UpdateHP: // 18
                HandleBinUpdateHp(bq);
                break;
            case ServerPacketId.UpdateGold: // 19
                _state.Gold = bq.ReadLong();
                break;
            case ServerPacketId.UpdateExp: // 20
                HandleBinUpdateExp(bq);
                break;

            // ── Map / Position ────────────────────────────────────
            case ServerPacketId.ChangeMap: // 21
                HandleBinChangeMap(bq);
                break;
            case ServerPacketId.PosUpdate: // 22
                HandleBinPosUpdate(bq);
                break;

            // ── Chat ──────────────────────────────────────────────
            case ServerPacketId.ChatOverHead: // 23
                HandleBinChatOverHead(bq);
                break;
            case ServerPacketId.ConsoleMsg: // 24
                HandleBinConsoleMsg(bq);
                break;
            case ServerPacketId.GuildChat: // 25
                HandleBinGuildChat(bq);
                break;

            // ── Character ─────────────────────────────────────────
            case ServerPacketId.CharacterCreate: // 29
                HandleBinCharacterCreate(bq);
                break;
            case ServerPacketId.CharacterRemove: // 30
                _state.Characters.Remove(bq.ReadInteger());
                break;
            case ServerPacketId.CharacterMove: // 31
                HandleBinCharacterMove(bq);
                break;
            case ServerPacketId.ForceCharMove: // 32
                HandleBinForceCharMove(bq);
                break;
            case ServerPacketId.CharacterChange: // 33
                HandleBinCharacterChange(bq);
                break;

            // ── Objects on ground ─────────────────────────────────
            case ServerPacketId.ObjectCreate: // 34
                HandleBinObjectCreate(bq);
                break;
            case ServerPacketId.ObjectDelete: // 35
                HandleBinObjectDelete(bq);
                break;
            case ServerPacketId.BlockPosition: // 36
                HandleBinBlockPosition(bq);
                break;

            // ── Sound / Music ─────────────────────────────────────
            case ServerPacketId.PlayMusic: // 37
                HandleBinPlayMidi(bq);
                break;
            case ServerPacketId.PlayWave: // 38
                HandleBinPlayWave(bq);
                break;

            // ── Guild ─────────────────────────────────────────────
            case ServerPacketId.GuildList: // 39
                {
                    string data = bq.ReadString();
                    _state.GuildListData = data;
                    _state.ShowGuildPanel = true;
                    _state.GuildInfoType = "List";
                    GD.Print($"[PKT] GuildList (binary): {data.Length} chars");
                }
                break;

            // ── Toggles / Area ────────────────────────────────────
            case ServerPacketId.AreaChanged: // 40
                HandleBinAreaChanged(bq);
                break;
            case ServerPacketId.PauseToggle: // 41
                _state.Paused = !_state.Paused;
                break;
            case ServerPacketId.RainToggle: // 42
                _state.Raining = !_state.Raining;
                break;
            case ServerPacketId.CreateFX: // 43
                HandleBinCreateFx(bq);
                break;
            case ServerPacketId.UpdateUserStats: // 44
                HandleBinUpdateUserStats(bq);
                break;
            case ServerPacketId.WorkRequestTarget: // 45
                {
                    byte skill = bq.ReadByte();
                    _state.UsingSkill = skill;
                    // VB6: frmMain.MousePointer = 2 (crosshair)
                    Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                    GD.Print($"[PKT] WorkRequestTarget skill={skill} — entering target mode");
                }
                break;

            // ── Inventory / Spells ────────────────────────────────
            case ServerPacketId.ChangeInventorySlot: // 46
                HandleBinChangeInventorySlot(bq);
                break;
            case ServerPacketId.ChangeBankSlot: // 47
                HandleBinChangeBankSlot(bq);
                break;
            case ServerPacketId.ChangeSpellSlot: // 48
                HandleBinChangeSpellSlot(bq);
                break;
            case ServerPacketId.Attributes: // 49
                HandleBinAtributes(bq);
                break;
            case ServerPacketId.SendSkills: // 50
                HandleBinSendSkills(bq);
                break;
            case ServerPacketId.ChangeNPCInventorySlot: // 51
                HandleBinChangeNpcInvSlot(bq);
                break;

            // ── Status / Toggles ──────────────────────────────────
            case ServerPacketId.RestToggle: // 54
                _state.Resting = !_state.Resting;
                break;
            case ServerPacketId.ErrorShow: // 55
                HandleBinErrorShow(bq);
                break;
            case ServerPacketId.Blind: // 56
                _state.UserBlind = true;
                break;
            case ServerPacketId.Silence: // 57
                _state.UserDumb = true;
                break;
            case ServerPacketId.ShowSignal: // 58
                { string text = bq.ReadString(); short grh = bq.ReadInteger(); GD.Print($"[PKT] ShowSignal: {text} grh={grh}"); }
                break;
            case ServerPacketId.DiceRoll: // 59
                HandleBinDiceRoll(bq);
                break;
            case ServerPacketId.UpdateHungerAndThirst: // 60
                HandleBinHungerThirst(bq);
                break;
            case ServerPacketId.Fame: // 61
                HandleBinFame(bq);
                break;
            case ServerPacketId.MiniStats: // 62
                HandleBinMiniStats(bq);
                break;
            case ServerPacketId.LevelUp: // 63
                HandleBinLevelUp(bq);
                break;
            case ServerPacketId.AddCharPreview: // 64
                HandleBinAddPJ(bq);
                break;
            case ServerPacketId.SecurityCode: // 65
                HandleBinSecurityCode(bq);
                break;
            case ServerPacketId.SetInvisible: // 66
                HandleBinSetInvisible(bq);
                break;
            case ServerPacketId.InitAccount: // 67
                HandleBinInitAccount(bq);
                break;
            case ServerPacketId.MeditateToggle: // 69
                HandleBinMeditateToggle();
                break;
            case ServerPacketId.BlindNoMore: // 70
                _state.UserBlind = false;
                break;
            case ServerPacketId.SilenceEnd: // 71
                _state.UserDumb = false;
                break;
            case ServerPacketId.TrainerCreatureList: // 72
                {
                    string creatures = bq.ReadString();
                    _state.TrainerCreatureData = creatures;
                    _state.ShowTrainerPanel = true;
                    GD.Print($"[PKT] TrainerCreatureList len={creatures.Length}");
                }
                break;
            case ServerPacketId.GuildNews: // 73
                {
                    string news = bq.ReadString();
                    string motd = bq.ReadString();
                    string codex = bq.ReadString();
                    _state.GuildNewsText = news;
                    _state.GuildMotdText = motd;
                    _state.GuildCodexText = codex;
                    GD.Print($"[PKT] GuildNews: news={news.Length}c motd={motd.Length}c codex={codex.Length}c");
                }
                break;
            case ServerPacketId.PrivilegeLevel: // 74
                _state.Privileges = bq.ReadByte();
                break;
            case ServerPacketId.CharacterInfo: // 75
                HandleBinCharacterInfo(bq);
                break;
            case ServerPacketId.FinishOK: // 77
                GD.Print("[GAME] FINOK: Graceful logout (binary)");
                break;
            case ServerPacketId.Dead: // 78
                HandleBinDead();
                break;
            case ServerPacketId.RemoveDialogs: // 79
                HandleBinRemoveDialogs();
                break;
            case ServerPacketId.RemoveCharDialog: // 80
                HandleBinRemoveCharDialog(bq);
                break;
            case ServerPacketId.NavigateToggle: // 81
                _state.UserNavigating = !_state.UserNavigating;
                break;
            case ServerPacketId.ParalyzeOK: // 82
                HandleBinParalizeOk(bq);
                break;
            case ServerPacketId.ShowGuildFoundationForm: // 83
                _state.ShowGuildFoundation = true;
                GD.Print("[PKT] ShowGuildFoundationForm (binary)");
                break;
            case ServerPacketId.TradeOK: // 84
                HandleBinTradeOk();
                break;
            case ServerPacketId.BankOK: // 85
                GD.Print("[PKT] BankOK (binary)");
                break;
            case ServerPacketId.ChangeUserTradeSlot: // 86
                HandleBinChangeUserTradeSlot(bq);
                break;
            case ServerPacketId.SendNight: // 87
                _state.IsNight = bq.ReadBoolean();
                break;
            case ServerPacketId.Pong: // 88
                HandleBinPong();
                break;
            case ServerPacketId.UpdateTagAndStatus: // 89
                HandleBinUpdateTagAndStatus(bq);
                break;
            case ServerPacketId.SpawnList: // 90
                { string _ = bq.ReadString(); GD.Print("[PKT] SpawnList (binary)"); }
                break;
            case ServerPacketId.ShowSOSForm: // 91
                { string _ = bq.ReadString(); GD.Print("[PKT] ShowSOSForm (binary)"); }
                break;
            case ServerPacketId.ShowMOTDEditionForm: // 92
                { string _ = bq.ReadString(); GD.Print("[PKT] ShowMOTDEditionForm (binary)"); }
                break;
            case ServerPacketId.ShowGMPanelForm: // 93
                GD.Print("[PKT] ShowGMPanelForm (binary)");
                break;
            case ServerPacketId.UserNameList: // 94
                { string _ = bq.ReadString(); GD.Print("[PKT] UserNameList (binary)"); }
                break;
            case ServerPacketId.ShowGuildAlign: // 95
                GD.Print("[PKT] ShowGuildAlign (binary)");
                break;
            case ServerPacketId.MapMusic: // 96
                HandleBinMapMusic(bq);
                break;
            case ServerPacketId.MapName: // 97
                _state.MapName = bq.ReadString();
                break;
            case ServerPacketId.CharData: // 98
                HandleBinCharData(bq);
                break;
            case ServerPacketId.AuraUpdate: // 99
                HandleBinAuraUpdate(bq);
                break;
            case ServerPacketId.StopWorking: // 100
                _state.UserStopped = true;
                break;
            case ServerPacketId.UpdateStrengthAndDexterity: // 101
                _state.Strength = bq.ReadByte();
                _state.Agility = bq.ReadByte();
                break;
            case ServerPacketId.UpdateBankGold: // 102
                _state.BankGold = bq.ReadLong();
                break;
            case ServerPacketId.AddSlots: // 103
                { byte slots = bq.ReadByte(); GD.Print($"[PKT] AddSlots: {slots}"); }
                break;
            case ServerPacketId.MultiMessage: // 104
                HandleBinMultiMessage(bq);
                break;
            case ServerPacketId.CancelOfferItem: // 105
                HandleBinCancelOfferItem(bq);
                break;
            case ServerPacketId.ShowPartyForm: // 106
                {
                    byte pType = bq.ReadByte();
                    _state.ShowPartyPanel = true;
                    GD.Print($"[PKT] ShowPartyForm type={pType}");
                }
                break;

            // ── Auth / Login (continued) ──────────────────────────
            case ServerPacketId.ShowMessageBox2: // 26
                HandleBinShowMessageBox2(bq);
                break;
            case ServerPacketId.UserIndexAlt: // 27
                HandleBinUserIndexAlt(bq);
                break;
            case ServerPacketId.UserCharIndexAlt: // 28
                HandleBinUserCharIndexAlt(bq);
                break;
            case ServerPacketId.DiceRollAlt: // 68
                HandleBinDiceRollAlt(bq);
                break;
            case ServerPacketId.AccountData: // 76
                HandleBinAccountData(bq);
                break;

            // ── Movement / Projectiles ────────────────────────────
            case ServerPacketId.Arrow: // 108
                HandleBinArrow(bq);
                break;
            case ServerPacketId.NavigateBroadcast: // 109
                HandleBinNavigateBroadcast(bq);
                break;

            // ── Chat variants ─────────────────────────────────────
            case ServerPacketId.ChatTalk: // 110
                HandleBinChatTalk(bq);
                break;
            case ServerPacketId.ChatYell: // 111
                HandleBinChatYell(bq);
                break;
            case ServerPacketId.ChatWhisper: // 112
                HandleBinChatWhisper(bq);
                break;
            case ServerPacketId.ChatClan: // 114
                HandleBinChatClan(bq);
                break;
            case ServerPacketId.ConsoleMsgId: // 115
                HandleBinConsoleMsgId(bq);
                break;
            case ServerPacketId.GmBroadcast: // 116
                HandleBinGmBroadcast(bq);
                break;
            case ServerPacketId.ChatGuild2: // 113
                HandleBinChatGuild2(bq);
                break;

            // ── Forum ────────────────────────────────────────────
            case ServerPacketId.AddForumMsg: // 117
                HandleBinAddForumMsg(bq);
                break;
            case ServerPacketId.ShowForumForm: // 118
                HandleBinShowForumForm(bq);
                break;

            // ── Stat variants ─────────────────────────────────────
            case ServerPacketId.StatHP: // 120
                _state.MinHp = bq.ReadInteger();
                break;
            case ServerPacketId.StatMana: // 121
                _state.MinMana = bq.ReadInteger();
                break;
            case ServerPacketId.StatSta: // 122
                _state.MinSta = bq.ReadInteger();
                break;
            case ServerPacketId.StatGold: // 123
                _state.Gold = bq.ReadLong();
                break;
            case ServerPacketId.StatExp: // 124
                _state.Exp = bq.ReadLong();
                break;
            case ServerPacketId.StatName: // 126
                HandleBinStatName(bq);
                break;
            case ServerPacketId.StatBulk: // 127
                HandleBinStatBulk(bq);
                break;
            case ServerPacketId.HungerThirst: // 128
                HandleBinHungerThirst128(bq);
                break;
            case ServerPacketId.OnlineCount: // 129
                HandleBinOnlineCount(bq);
                break;

            // ── Safe / Combat state ───────────────────────────────
            case ServerPacketId.SafeOn: // 130
                _state.SafeMode = true;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO ACTIVADO<<", Color = "00FF00" });
                break;
            case ServerPacketId.SafeOff: // 131
                _state.SafeMode = false;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DESACTIVADO<<", Color = "FF0000" });
                break;
            case ServerPacketId.SafeResuOn: // 132
                _state.SeguroResu = true;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION ACTIVADO<<", Color = "00FF00" });
                break;
            case ServerPacketId.SafeResuOff: // 133
                _state.SeguroResu = false;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION DESACTIVADO<<", Color = "FF0000" });
                break;
            case ServerPacketId.UserSwing: // 134
                HandleBinUserSwing(bq);
                break;
            case ServerPacketId.UserHit: // 135
                HandleBinUserHit(bq);
                break;
            case ServerPacketId.NpcSwing: // 136
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "La criatura fallo el golpe!!!", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(_state.UserCharIndex, "Fallo!", "CCCCCC");
                break;
            case ServerPacketId.NpcHit: // 137
                HandleBinNpcHit(bq);
                break;
            case ServerPacketId.PvpDmgRecv: // 138
                HandleBinPvpDmgRecv(bq);
                break;
            case ServerPacketId.PvpDmgDeal: // 139
                HandleBinPvpDmgDeal(bq);
                break;
            case ServerPacketId.UserMiss: // 140
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has fallado el golpe!!!", Color = "FF0000", Type = ChatType.Combat });
                break;
            case ServerPacketId.YouDied: // 141
                HandleBinDead();
                break;

            // ── Inventory / Spells (legacy IDs) ──────────────────
            case ServerPacketId.InvSlot: // 145
                HandleBinChangeInventorySlot(bq);
                break;
            case ServerPacketId.SpellSlot: // 147
                HandleBinChangeSpellSlot(bq);
                break;
            case ServerPacketId.SpellInfoResp: // 148
                HandleBinSpellInfoResp(bq);
                break;

            // ── Sound ─────────────────────────────────────────────
            case ServerPacketId.PlaySound: // 150
                HandleBinPlaySound(bq);
                break;

            // ── Work / Crafting ───────────────────────────────────
            case ServerPacketId.WorkMode: // 155
                HandleBinWorkMode(bq);
                break;
            case ServerPacketId.OpenSmith: // 156
                GD.Print("[PKT] OpenSmith");
                break;
            case ServerPacketId.OpenCarp: // 157
                GD.Print("[PKT] OpenCarp");
                break;
            case ServerPacketId.SmithWeapons: // 158
                HandleBinCraftList(bq, _state.SmithWeapons, true);
                break;
            case ServerPacketId.SmithArmors: // 159
                HandleBinCraftList(bq, _state.SmithArmors, true);
                break;
            case ServerPacketId.CarpItems: // 160
                HandleBinCraftList(bq, _state.CarpItems, false);
                break;
            case ServerPacketId.MeditateOK: // 161
                GD.Print("[PKT] MeditateOK");
                break;
            case ServerPacketId.Navigation: // 162
                HandleBinNavigationData(bq);
                break;
            case ServerPacketId.BattleTeamScores: // 163
                HandleBinBattleTeamScores(bq);
                break;
            case ServerPacketId.AmbientColor: // 164
                HandleBinAmbientColor(bq);
                break;

            // ── Bank (legacy) ─────────────────────────────────────
            case ServerPacketId.InitBankLegacy: // 165
                HandleBinInitBankLegacy(bq);
                break;
            case ServerPacketId.BankSlotLegacy: // 166
                HandleBinBankSlotLegacy(bq);
                break;
            case ServerPacketId.BankGoldLegacy: // 167
                _state.BankGold = bq.ReadLong();
                break;
            case ServerPacketId.BankCloseOK: // 168
                _state.Banqueando = false;
                _state.BovedaAbierta = false;
                GD.Print("[PKT] BankCloseOK");
                break;

            // ── Commerce (legacy) ─────────────────────────────────
            case ServerPacketId.NpcInvReset: // 170
                _state.NpcShopCount = 0;
                break;
            case ServerPacketId.NpcInvItem: // 171
                HandleBinNpcInvItem(bq);
                break;
            case ServerPacketId.NpcInvSlotLegacy: // 172
                HandleBinChangeNpcInvSlot(bq);
                break;
            case ServerPacketId.InitCommerceLegacy: // 173
                _state.Comerciando = true;
                break;
            case ServerPacketId.TransactionOK: // 174
                HandleBinTransOk(bq);
                break;
            case ServerPacketId.CommerceCloseOK: // 175
                _state.Comerciando = false;
                GD.Print("[PKT] CommerceCloseOK");
                break;

            // ── Tournament / Response / Auction ───────────────────
            case ServerPacketId.TournamentPoints: // 176
                HandleBinTournamentPoints(bq);
                break;
            case ServerPacketId.ResponseMsg: // 177
                HandleBinResponseMsg(bq);
                break;
            case ServerPacketId.AuctionInit: // 178
                break;
            case ServerPacketId.AuctionBid: // 179
                HandleBinAuctionBid(bq);
                break;

            // ── Trading (legacy) ──────────────────────────────────
            case ServerPacketId.TradeInitLegacy: // 180
                HandleBinTradeInitLegacy(bq);
                break;
            case ServerPacketId.TradeOfferRecv: // 181
                HandleBinTradeOfferRecv(bq);
                break;
            case ServerPacketId.TradeItems: // 182
                HandleBinTradeItems(bq);
                break;
            case ServerPacketId.TradeChatMsgLegacy: // 183
                HandleBinTradeChatLegacy(bq);
                break;
            case ServerPacketId.TradeOKLegacy: // 184
                HandleBinTradeOk();
                break;
            case ServerPacketId.TradeCancelOK: // 185
                _state.Trading = false;
                _state.MyTradeSlotCount = 0;
                _state.PartnerTradeSlotCount = 0;
                _state.MyTradeGold = 0;
                _state.PartnerTradeGold = 0;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Comercio cancelado.", Color = "FF0000" });
                GD.Print("[PKT] TradeCancelOK");
                break;

            // ── Guild (legacy) ────────────────────────────────────
            case ServerPacketId.GuildListLegacy: // 190
                { string _ = bq.ReadString(); GD.Print("[PKT] GuildListLegacy"); }
                break;
            case ServerPacketId.GuildInfoLeader: // 191
                HandleBinGuildInfoStr(bq, "Leader");
                break;
            case ServerPacketId.GuildInfoMember: // 192
                HandleBinGuildInfoStr(bq, "Member");
                break;
            case ServerPacketId.GuildShowForm: // 193
                GD.Print("[PKT] GuildShowForm");
                break;
            case ServerPacketId.GuildDetailsResp: // 194
                HandleBinGuildInfoStr(bq, "Details");
                break;
            case ServerPacketId.GuildBankPermsResp: // 195
                HandleBinGuildBankPermsResp(bq);
                break;
            case ServerPacketId.ClanChatResp: // 196
                HandleBinClanChatResp(bq);
                break;
            case ServerPacketId.GuildBankSlotData: // 197
                HandleBinGuildBankSlotData(bq);
                break;

            // ── Quest ─────────────────────────────────────────────
            case ServerPacketId.QuestListResp: // 200
                HandleBinQuestData(bq, "QuestList");
                break;
            case ServerPacketId.QuestCurrent: // 201
                HandleBinQuestData(bq, "QuestCurrent");
                break;
            case ServerPacketId.QuestSelected: // 202
                HandleBinQuestData(bq, "QuestSelected");
                break;
            case ServerPacketId.QuestNpcList: // 203
                break;

            // ── Mail ──────────────────────────────────────────────
            case ServerPacketId.MailList: // 205
                HandleBinMailData(bq, "MailList");
                break;
            case ServerPacketId.MailPlayerInfo: // 206
                HandleBinMailData(bq, "MailPlayerInfo");
                break;
            case ServerPacketId.MailContent: // 208
                HandleBinMailData(bq, "MailContent");
                break;
            case ServerPacketId.MailItems: // 209
                HandleBinMailData(bq, "MailItems");
                break;

            // ── Misc data ─────────────────────────────────────────
            case ServerPacketId.MenuData: // 221
                HandleBinMenuData(bq);
                break;
            case ServerPacketId.SelectData: // 222
                HandleBinSelectData(bq);
                break;
            case ServerPacketId.MiniTopData: // 223
                HandleBinMiniTopData(bq);
                break;
            case ServerPacketId.ImageData: // 224
                HandleBinStringOnly(bq, "ImageData");
                break;
            case ServerPacketId.BkwData: // 226
                HandleBinStringOnly(bq, "BkwData");
                break;
            case ServerPacketId.FestData: // 227
                HandleBinFestData(bq);
                break;
            case ServerPacketId.EnchatData: // 228
                HandleBinEnchatData(bq);
                break;
            case ServerPacketId.IrchatData: // 229
                HandleBinIrchatData(bq);
                break;
            case ServerPacketId.GinfData: // 230
                HandleBinStringOnly(bq, "GinfData");
                break;
            case ServerPacketId.IcoData: // 231
                HandleBinStringOnly(bq, "IcoData");
                break;
            case ServerPacketId.ZsosData: // 232
                HandleBinStringOnly(bq, "ZsosData");
                break;
            case ServerPacketId.SbrData: // 234
                HandleBinBankReset(bq);
                break;
            case ServerPacketId.AuctionList: // 237
                HandleBinStringOnly(bq, "AuctionList");
                break;

            // ── Cosmetics ─────────────────────────────────────────
            case ServerPacketId.CosmeticSurgery: // 238
                HandleBinCosmeticSurgery(bq);
                break;
            case ServerPacketId.CosmeticImage: // 239
                HandleBinStringOnly(bq, "CosmeticImage");
                break;
            case ServerPacketId.CosmeticPcgn: // 240
                HandleBinStringOnly(bq, "CosmeticPcgn");
                break;
            case ServerPacketId.CosmeticPcss: // 241
                HandleBinStringOnly(bq, "CosmeticPcss");
                break;
            case ServerPacketId.CosmeticPccc: // 242
                HandleBinStringOnly(bq, "CosmeticPccc");
                break;

            // ── Guild bank / Full char info ───────────────────────
            case ServerPacketId.FullCharInfo: // 245
                HandleBinFullCharInfo(bq);
                break;
            case ServerPacketId.GuildBankInitResp: // 247
                HandleBinGuildBankInitResp(bq);
                break;
            case ServerPacketId.GuildBankSlotResp: // 248
                HandleBinGuildBankSlotResp(bq);
                break;
            case ServerPacketId.GuildBankGoldResp: // 249
                _state.GuildBankGold = bq.ReadLong();
                break;

            // ── Ping / Triggers ───────────────────────────────────
            case ServerPacketId.Ping: // 250
                HandleBinPingRequest();
                break;
            case ServerPacketId.TravelsOpen: // 251
                _state.ShowTravelPanel = true;
                GD.Print("[PKT] TravelsOpen");
                break;
            case ServerPacketId.MailOpenTrigger: // 252
                GD.Print("[PKT] MailOpenTrigger");
                _state.ShowMailPanel = true;
                break;
            case ServerPacketId.ArenaData: // 254
                HandleBinArenaData(bq);
                break;
            case ServerPacketId.GenericText: // 255
                HandleGenericTextPacket(bq);
                break;

            // ── Movement / Appearance ─────────────────────────────
            case ServerPacketId.HeadingChange: // 107
                HandleBinHeadingChange(bq);
                break;

            // ── Stats / Level ─────────────────────────────────────
            case ServerPacketId.StatLevel: // 125
                _state.Level = bq.ReadByte();
                break;

            // ── Inventory ─────────────────────────────────────────
            case ServerPacketId.InvInit: // 146
                // Inventory reset signal — no payload.
                break;

            // ── Mount / Levitate ──────────────────────────────────
            case ServerPacketId.UserMount: // 142
                HandleBinUserMount(bq);
                break;
            case ServerPacketId.Levitate: // 143
                HandleBinLevitate(bq);
                break;

            // ── Animation / Equipment stats ───────────────────────
            case ServerPacketId.AnimData: // 225
                HandleBinAnimData(bq);
                break;
            case ServerPacketId.StopDancing: // 220
                _state.UserStopped = bq.ReadBoolean();
                break;

            // ── Reputation / Timer ────────────────────────────────
            case ServerPacketId.RptData: // 233
                _state.Reputation = bq.ReadLong();
                break;
            case ServerPacketId.TimerInfo: // 246
                HandleBinTimerInfo(bq);
                break;

            // ── Class options ─────────────────────────────────────
            case ServerPacketId.ClassOptions: // 144
                HandleBinClassOptions(bq);
                break;

            // ── Particles / Lights ────────────────────────────────
            case ServerPacketId.CharParticleCreate: // 211
                HandleBinCharParticleCreate(bq);
                break;
            case ServerPacketId.ParticleCreate: // 243
                HandleBinParticleCreate(bq);
                break;
            case ServerPacketId.LightCreate: // 244
                HandleBinLightCreate(bq);
                break;

            default:
                // Unknown opcode — fatal for this packet stream since we don't know
                // the packet length. Log and skip this single byte, hoping to resync.
                GD.PrintErr($"[PKT] Unknown binary opcode={opcode}, skipping byte");
                break;
        }
    }
}

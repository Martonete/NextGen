using System;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Network;

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
                break;
            case ServerPacketId.UserOfferConfirm: // 12
                GD.Print("[PKT] UserOfferConfirm");
                break;
            case ServerPacketId.CommerceChat: // 13
                HandleBinCommerceChat(bq);
                break;
            case ServerPacketId.ShowBlacksmithForm: // 14
                GD.Print("[PKT] ShowBlacksmithForm");
                break;
            case ServerPacketId.ShowCarpenterForm: // 15
                GD.Print("[PKT] ShowCarpenterForm");
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
                bq.ReadByte(); // direction — TODO: implement forced movement
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
            case ServerPacketId.PlayMIDI: // 37
                HandleBinPlayMidi(bq);
                break;
            case ServerPacketId.PlayWave: // 38
                HandleBinPlayWave(bq);
                break;

            // ── Guild ─────────────────────────────────────────────
            case ServerPacketId.GuildList: // 39
                { string _ = bq.ReadString(); GD.Print("[PKT] GuildList (binary)"); }
                break;

            // ── Toggles / Area ────────────────────────────────────
            case ServerPacketId.AreaChanged: // 40
                HandleBinAreaChanged(bq);
                break;
            case ServerPacketId.PauseToggle: // 41
                _state.Paused = !_state.Paused;
                if (_state.Paused)
                {
                    // Stop any in-progress movement so the client doesn't queue
                    // extra walk packets while paused (e.g., during map warp).
                    _state.UserMoving = false;
                    _state.PendingMoves = 0;
                }
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
                { byte skill = bq.ReadByte(); GD.Print($"[PKT] WorkRequestTarget skill={skill}"); }
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
            case ServerPacketId.Atributes: // 49
                HandleBinAtributes(bq);
                break;
            case ServerPacketId.SendSkills: // 50
                HandleBinSendSkills(bq);
                break;
            case ServerPacketId.ChangeNPCInventorySlot: // 51
                HandleBinChangeNpcInvSlot(bq);
                break;

            // ── Status / Toggles ──────────────────────────────────
            case ServerPacketId.RestOK: // 54
                _state.Resting = !_state.Resting;
                break;
            case ServerPacketId.ErrorShow: // 55
                HandleBinErrorShow(bq);
                break;
            case ServerPacketId.Blind: // 56
                _state.UserBlind = true;
                break;
            case ServerPacketId.Dumb: // 57
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
            case ServerPacketId.AddPJ: // 64
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
            case ServerPacketId.DumbNoMore: // 71
                _state.UserDumb = false;
                break;
            case ServerPacketId.TrainerCreatureList: // 72
                { string _ = bq.ReadString(); GD.Print("[PKT] TrainerCreatureList (binary)"); }
                break;
            case ServerPacketId.GuildNews: // 73
                { string news = bq.ReadString(); string motd = bq.ReadString(); string codex = bq.ReadString(); GD.Print($"[PKT] GuildNews (binary)"); }
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
            case ServerPacketId.ParalizeOK: // 82
                HandleBinParalizeOk(bq);
                break;
            case ServerPacketId.ShowGuildFundationForm: // 83
                GD.Print("[PKT] ShowGuildFundationForm (binary)");
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
                { byte slot = bq.ReadByte(); GD.Print($"[PKT] CancelOfferItem slot={slot}"); }
                break;
            case ServerPacketId.ShowPartyForm: // 106
                { byte pType = bq.ReadByte(); GD.Print($"[PKT] ShowPartyForm type={pType}"); }
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
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "La criatura fallo el golpe!!!", Color = "FF0000" });
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
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has fallado el golpe!!!", Color = "FF0000" });
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
                HandleBinSmithList(bq, "SmithWeapons");
                break;
            case ServerPacketId.SmithArmors: // 159
                HandleBinSmithList(bq, "SmithArmors");
                break;
            case ServerPacketId.CarpItems: // 160
                HandleBinSmithList(bq, "CarpItems");
                break;
            case ServerPacketId.MedOK: // 161
                GD.Print("[PKT] MedOK");
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
            case ServerPacketId.TransOK: // 174
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
                GD.Print("[PKT] AuctionInit");
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
                GD.Print("[PKT] QuestNpcList trigger");
                break;

            // ── Mail ──────────────────────────────────────────────
            case ServerPacketId.MailList: // 205
                HandleBinMailData(bq, "MailList");
                break;
            case ServerPacketId.MailPlayerInfo: // 206
                HandleBinMailData(bq, "MailPlayerInfo");
                break;
            case ServerPacketId.MailFriends: // 207
                HandleBinMailData(bq, "MailFriends");
                break;
            case ServerPacketId.MailContent: // 208
                HandleBinMailData(bq, "MailContent");
                break;
            case ServerPacketId.MailItems: // 209
                HandleBinMailData(bq, "MailItems");
                break;

            // ── Friends ───────────────────────────────────────────
            case ServerPacketId.FriendList: // 210
                HandleBinFriendList(bq);
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
                HandleBinStringOnly(bq, "FestData");
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
            case ServerPacketId.KfmData: // 235
                HandleBinKfmData(bq);
                break;
            case ServerPacketId.DfmData: // 236
                HandleBinDfmData(bq);
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
                HandleBinStringOnly(bq, "FullCharInfo");
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
                _state.ShowMailPanel = true;
                GD.Print("[PKT] MailOpenTrigger");
                break;
            case ServerPacketId.FriendDialog: // 253
                _state.ShowFriendDialog = true;
                GD.Print("[PKT] FriendDialog");
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

    // ════════════════════════════════════════════════════════════════
    // Individual binary packet handlers
    // ════════════════════════════════════════════════════════════════

    // ── Auth / Login ──────────────────────────────────────────────

    private void HandleBinLogged(ByteQueue bq)
    {
        byte charClass = bq.ReadByte();
        _state.IsLogged = true;
        _state.UserClass = charClass;
        GD.Print($"[GAME] Login successful — LOGGED (binary, class={charClass})");
    }

    private void HandleBinDisconnect(ByteQueue bq)
    {
        GD.Print("[GAME] Disconnect received (binary)");
        _state.IsLogged = false;
    }

    private void HandleBinErrorMsg(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.LoginError = msg;
        _state.MensajeText = msg;
        GD.Print($"[LOGIN] ERR (binary): {msg}");
    }

    private void HandleBinShowMessageBox(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
        GD.Print($"[GM] Message box (binary): {msg}");
    }

    private void HandleBinUserIndex(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        GD.Print($"[GAME] UserIndexInServer: {index}");
    }

    private void HandleBinUserCharIndex(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserCharIndex = index;
        GD.Print($"[GAME] Self char index (binary): {index}");
    }

    // ── Commerce / Bank ───────────────────────────────────────────

    private void HandleBinBankInit(ByteQueue bq)
    {
        int bankGold = bq.ReadLong();
        _state.BankGold = bankGold;
        _state.Banqueando = true;
        GD.Print($"[PKT] BankInit (binary): gold={bankGold}");
    }

    private void HandleBinUserCommerceInit(ByteQueue bq)
    {
        string otherName = bq.ReadString();
        _state.Trading = true;
        GD.Print($"[PKT] UserCommerceInit (binary): {otherName}");
    }

    private void HandleBinCommerceChat(ByteQueue bq)
    {
        string chat = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = "FFFFFF" });
    }

    // ── Stats ─────────────────────────────────────────────────────

    private void HandleBinUpdateSta(ByteQueue bq)
    {
        short minSta = bq.ReadInteger();
        _state.MinSta = minSta;
    }

    private void HandleBinUpdateMana(ByteQueue bq)
    {
        short minMana = bq.ReadInteger();
        _state.MinMana = minMana;
    }

    private void HandleBinUpdateHp(ByteQueue bq)
    {
        short minHp = bq.ReadInteger();
        _state.MinHp = minHp;
    }

    private void HandleBinUpdateExp(ByteQueue bq)
    {
        int exp = bq.ReadLong();
        _state.Exp = exp;
    }

    private void HandleBinUpdateUserStats(ByteQueue bq)
    {
        _state.MaxHp = bq.ReadInteger();
        _state.MinHp = bq.ReadInteger();
        _state.MaxMana = bq.ReadInteger();
        _state.MinMana = bq.ReadInteger();
        _state.MaxSta = bq.ReadInteger();
        _state.MinSta = bq.ReadInteger();
        _state.Gold = bq.ReadLong();
        _state.Level = bq.ReadByte();
        _state.ExpNext = bq.ReadLong();
        _state.Exp = bq.ReadLong();
        GD.Print($"[GAME] Stats (binary): HP {_state.MinHp}/{_state.MaxHp} Mana {_state.MinMana}/{_state.MaxMana} Lvl {_state.Level}");
    }

    // ── Map / Position ────────────────────────────────────────────

    private void HandleBinChangeMap(ByteQueue bq)
    {
        short mapNum = bq.ReadInteger();
        short mapVersion = bq.ReadInteger();

        _state.CurrentMap = mapNum;
        // Binary ChangeMap doesn't carry RGB — use map header defaults
        // (the server will send MapMusic/MapName/ambient separately if needed)

        // Clear all characters except self
        var selfIdx = _state.UserCharIndex;
        var toRemove = new System.Collections.Generic.List<int>();
        foreach (var kvp in _state.Characters)
        {
            if (kvp.Key != selfIdx)
                toRemove.Add(kvp.Key);
        }
        foreach (int key in toRemove)
            _state.Characters.Remove(key);

        _state.GroundObjects.Clear();
        _state.MapParticles.Clear();
        _state.MapLights.Clear();
        _state.LightsDirty = true;

        _state.ScreenOffsetX = 0;
        _state.ScreenOffsetY = 0;
        _state.AddToUserPosX = 0;
        _state.AddToUserPosY = 0;
        _state.UserMoving = false;
        _state.PendingMoves = 0;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh))
        {
            selfCh.MoveOffsetX = 0;
            selfCh.MoveOffsetY = 0;
            selfCh.Moving = false;
            selfCh.ScrollDirectionX = 0;
            selfCh.ScrollDirectionY = 0;
        }

        OnMapLoad?.Invoke();
        GD.Print($"[GAME] Change map (binary): {mapNum} v{mapVersion}");
    }

    private void HandleBinPosUpdate(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();

        _state.UserPosX = x;
        _state.UserPosY = y;

        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ch.PosX = x;
            ch.PosY = y;
            ch.MoveOffsetX = 0;
            ch.MoveOffsetY = 0;
            ch.Moving = false;
            ch.ScrollDirectionX = 0;
            ch.ScrollDirectionY = 0;
        }
        _state.ScreenOffsetX = 0;
        _state.ScreenOffsetY = 0;
        _state.AddToUserPosX = 0;
        _state.AddToUserPosY = 0;
        _state.UserMoving = false;
        _state.PendingMoves = 0;
    }

    private void HandleBinAreaChanged(ByteQueue bq)
    {
        byte playerX = bq.ReadByte();
        byte playerY = bq.ReadByte();

        // VB6: CambioDeArea 9x9 grid zones
        int minLimX = (playerX / 9 - 1) * 9;
        int maxLimX = minLimX + 26;
        int minLimY = (playerY / 9 - 1) * 9;
        int maxLimY = minLimY + 26;

        const int HalfW = 8, HalfH = 6, VisMargin = 3;
        int visMinX = playerX - HalfW - VisMargin;
        int visMaxX = playerX + HalfW + VisMargin;
        int visMinY = playerY - HalfH - VisMargin;
        int visMaxY = playerY + HalfH + VisMargin;

        var toRemove = new System.Collections.Generic.List<int>();
        foreach (var kvp in _state.Characters)
        {
            if (kvp.Key == _state.UserCharIndex) continue;
            var c = kvp.Value;
            if (c.PosX < minLimX || c.PosX > maxLimX || c.PosY < minLimY || c.PosY > maxLimY)
            {
                if (c.PosX >= visMinX && c.PosX <= visMaxX && c.PosY >= visMinY && c.PosY <= visMaxY)
                    continue;
                toRemove.Add(kvp.Key);
            }
        }
        foreach (int key in toRemove)
            _state.Characters.Remove(key);

        var objToRemove = new System.Collections.Generic.List<(int, int)>();
        foreach (var kvp in _state.GroundObjects)
        {
            if (kvp.Key.Item1 < minLimX || kvp.Key.Item1 > maxLimX ||
                kvp.Key.Item2 < minLimY || kvp.Key.Item2 > maxLimY)
                objToRemove.Add(kvp.Key);
        }
        foreach (var key in objToRemove)
            _state.GroundObjects.Remove(key);
    }

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
        }
        else
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = hexColor });
        }
    }

    private void HandleBinConsoleMsg(ByteQueue bq)
    {
        string chat = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = color });
    }

    private void HandleBinGuildChat(ByteQueue bq)
    {
        string chat = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = chat, Color = "00FF00" });
    }

    // ── Character ─────────────────────────────────────────────────

    private void HandleBinCharacterCreate(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        short body = bq.ReadInteger();
        short head = bq.ReadInteger();
        byte heading = bq.ReadByte();
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        short weapon = bq.ReadInteger();
        short shield = bq.ReadInteger();
        short helmet = bq.ReadInteger();
        short fxIndex = bq.ReadInteger();
        short fxLoops = bq.ReadInteger();
        string name = bq.ReadString();
        byte nickColor = bq.ReadByte();
        byte privileges = bq.ReadByte();

        var ch = new Character
        {
            CharIndex = charIndex,
            Body = body,
            Head = head,
            Heading = heading,
            PosX = x,
            PosY = y,
            WeaponAnim = weapon,
            ShieldAnim = shield,
            CascoAnim = helmet,
            Name = name,
            Criminal = nickColor == 2,
            Privileges = privileges,
        };

        ch.Dead = IsDeadHead(head);

        // Apply FX if present
        if (fxIndex > 0)
        {
            ch.ActiveFxSlots[0] = fxIndex;
            ch.FxLoops[0] = fxLoops >= 999 ? -1 : fxLoops;
            ch.FxFrameCounter[0] = 0;
        }

        _state.Characters[charIndex] = ch;
        GD.Print($"[CC] {name} idx={charIndex} body={body} head={head} weapon={weapon} shield={shield} casco={helmet} (binary)");

        if (body <= 0)
            GD.PrintErr($"[CC] WARNING: char {name} (idx={charIndex}) has body=0!");
    }

    private void HandleBinCharacterMove(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        byte newX = bq.ReadByte();
        byte newY = bq.ReadByte();

        if (!_state.Characters.TryGetValue(charIndex, out var ch))
            return;

        if (charIndex == _state.UserCharIndex)
            return;

        ClearMeditationFx(ch);

        int dx = newX - ch.PosX;
        int dy = newY - ch.PosY;

        if (dy < 0) ch.Heading = 1;
        else if (dx > 0) ch.Heading = 2;
        else if (dy > 0) ch.Heading = 3;
        else if (dx < 0) ch.Heading = 4;

        ch.MoveOffsetX = -(dx * 32);
        ch.MoveOffsetY = -(dy * 32);
        ch.ScrollDirectionX = dx != 0 ? Math.Sign(dx) : 0;
        ch.ScrollDirectionY = dy != 0 ? Math.Sign(dy) : 0;
        ch.PosX = newX;
        ch.PosY = newY;
        ch.Moving = true;
    }

    private void HandleBinCharacterChange(ByteQueue bq)
    {
        short idx = bq.ReadInteger();
        short body = bq.ReadInteger();
        short head = bq.ReadInteger();
        byte heading = bq.ReadByte();
        short weapon = bq.ReadInteger();
        short shield = bq.ReadInteger();
        short helmet = bq.ReadInteger();
        short fxIndex = bq.ReadInteger();
        short fxLoops = bq.ReadInteger();

        if (!_state.Characters.TryGetValue(idx, out var ch))
            return;

        bool wasDead = ch.Dead;
        bool nowDead = IsDeadHead(head);

        ch.Body = body;
        ch.Head = head;
        ch.Heading = heading;
        ch.WeaponAnim = weapon;
        ch.ShieldAnim = shield;
        ch.CascoAnim = helmet;
        ch.Dead = nowDead;

        // Apply FX
        if (fxIndex > 0)
        {
            // Find empty slot or overwrite slot 0
            int slot = -1;
            for (int i = 0; i < 3; i++)
            {
                if (ch.ActiveFxSlots[i] == 0) { slot = i; break; }
            }
            if (slot < 0) slot = 0;
            ch.ActiveFxSlots[slot] = fxIndex;
            ch.FxLoops[slot] = fxLoops >= 999 ? -1 : fxLoops;
            ch.FxFrameCounter[slot] = 0;
        }

        if (wasDead != nowDead)
        {
            ch.TransparenciaBody = 0;
            ch.Llegoalatransp = false;
        }

        if (idx == _state.UserCharIndex)
        {
            if (nowDead && !wasDead) { _state.Dead = true; GD.Print($"[CP] User died (head={head}, binary)"); }
            else if (!nowDead && wasDead) { _state.Dead = false; GD.Print($"[CP] User revived (binary)"); }
        }
    }

    // ── Objects on ground ─────────────────────────────────────────

    private void HandleBinObjectCreate(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        short grhIndex = bq.ReadInteger();
        _state.GroundObjects[(x, y)] = grhIndex;
    }

    private void HandleBinObjectDelete(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        _state.GroundObjects.Remove((x, y));
    }

    private void HandleBinBlockPosition(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        bool blocked = bq.ReadBoolean();
        if (_state.MapData != null && x >= 1 && x <= 100 && y >= 1 && y <= 100)
        {
            _state.MapData.Tiles[x, y].Blocked = blocked;
        }
    }

    // ── Sound / Music ─────────────────────────────────────────────

    private void HandleBinPlayMidi(ByteQueue bq)
    {
        byte midiIndex = bq.ReadByte();
        _state.MusicId = midiIndex;
        OnPlayMusic?.Invoke(midiIndex);
    }

    private void HandleBinPlayWave(ByteQueue bq)
    {
        byte waveIndex = bq.ReadByte();
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        if (waveIndex > 0)
            OnPlaySound?.Invoke(waveIndex);
    }

    // ── FX ────────────────────────────────────────────────────────

    private void HandleBinCreateFx(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        short fxIndex = bq.ReadInteger();
        short fxLoops = bq.ReadInteger();

        if (!_state.Characters.TryGetValue(charIndex, out var ch))
            return;

        if (fxIndex == 0)
        {
            for (int i = 0; i < 3; i++)
            {
                ch.ActiveFxSlots[i] = 0;
                ch.FxLoops[i] = 0;
                ch.FxFrameCounter[i] = 0;
            }
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            if (ch.ActiveFxSlots[i] == 0)
            {
                ch.ActiveFxSlots[i] = fxIndex;
                ch.FxLoops[i] = fxLoops >= 999 ? -1 : fxLoops;
                ch.FxFrameCounter[i] = 0;
                return;
            }
        }

        ch.ActiveFxSlots[0] = fxIndex;
        ch.FxLoops[0] = fxLoops >= 999 ? -1 : fxLoops;
        ch.FxFrameCounter[0] = 0;
    }

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
        short def = bq.ReadInteger();
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
                MaxDef = def,
                Value = (int)value,
            };
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
        short def = bq.ReadInteger();
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
            MaxHit = maxHit, MinHit = minHit, MaxDef = def,
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

    private void HandleBinAtributes(ByteQueue bq)
    {
        _state.Strength = bq.ReadByte();
        _state.Agility = bq.ReadByte();
        byte intel = bq.ReadByte(); // Intelligence — stored if needed
        byte con = bq.ReadByte();   // Constitution
        byte cha = bq.ReadByte();   // Charisma
    }

    private void HandleBinSendSkills(ByteQueue bq)
    {
        for (int i = 0; i < 20; i++)
        {
            byte skillVal = bq.ReadByte();
            // TODO: store skill values in GameState
        }
    }

    private void HandleBinChangeNpcInvSlot(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        string name = bq.ReadString();
        short amount = bq.ReadInteger();
        float value = bq.ReadSingle();
        short grhIndex = bq.ReadInteger();
        short objIndex = bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short def = bq.ReadInteger();

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
            MaxHit = maxHit, MinHit = minHit, MaxDef = def, Slot = slot,
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

    private void HandleBinErrorShow(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
        GD.Print($"[GAME] ERO (binary): {msg}");
    }

    private void HandleBinDiceRoll(ByteQueue bq)
    {
        byte str = bq.ReadByte();
        byte agi = bq.ReadByte();
        byte intel = bq.ReadByte();
        byte con = bq.ReadByte();
        byte cha = bq.ReadByte();
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Dados: Fuerza={str} Agilidad={agi} Inteligencia={intel} Constitucion={con} Carisma={cha}",
            Color = "FFFF00"
        });
    }

    private void HandleBinHungerThirst(ByteQueue bq)
    {
        _state.MaxAgua = bq.ReadByte();
        _state.MinAgua = bq.ReadByte();
        _state.MaxHam = bq.ReadByte();
        _state.MinHam = bq.ReadByte();
    }

    private void HandleBinFame(ByteQueue bq)
    {
        for (int i = 0; i < 7; i++)
            bq.ReadLong(); // 7 fame values — TODO: store in GameState
    }

    private void HandleBinMiniStats(ByteQueue bq)
    {
        int gold = bq.ReadLong();
        int exp = bq.ReadLong();
        _state.Gold = gold;
        _state.Exp = exp;
    }

    private void HandleBinLevelUp(ByteQueue bq)
    {
        short skillPoints = bq.ReadInteger();
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Has subido de nivel! Tienes {skillPoints} puntos de habilidad.",
            Color = "00FF00"
        });
    }

    // ── Login flow ────────────────────────────────────────────────

    private void HandleBinAddPJ(ByteQueue bq)
    {
        string name = bq.ReadString();
        byte slot = bq.ReadByte();
        short head = bq.ReadInteger();
        short body = bq.ReadInteger();
        short weapon = bq.ReadInteger();
        short shield = bq.ReadInteger();
        short helmet = bq.ReadInteger();
        byte level = bq.ReadByte();
        string charClass = bq.ReadString();
        bool dead = bq.ReadBoolean();
        string race = bq.ReadString();

        var preview = new CharacterPreview
        {
            Name = name,
            Slot = slot,
            Head = head,
            Body = body,
            Level = level,
            Class = charClass,
            Dead = dead,
            Race = race,
        };
        _state.CharacterList.Add(preview);
        GD.Print($"[LOGIN] ADDPJ (binary): {name} Lvl {level} ({charClass})");
    }

    private void HandleBinSecurityCode(ByteQueue bq)
    {
        string code = bq.ReadString();
        _state.SecurityCode = code;
        _state.CurrentScreen = Screen.CharSelect;
        GD.Print("[LOGIN] CODEH (binary): switching to char select");
    }

    private void HandleBinSetInvisible(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool invisible = bq.ReadBoolean();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.Invisible = invisible;
            ch.TransparenciaBody = 0;
            ch.Llegoalatransp = false;
        }
    }

    private void HandleBinInitAccount(ByteQueue bq)
    {
        byte numChars = bq.ReadByte();
        string notice = bq.ReadString();
        _state.CharacterList.Clear();
        _state.ServerNotice = notice;
        GD.Print($"[LOGIN] INIAC (binary): {numChars} chars, notice={notice}");
    }

    // ── Death & status ────────────────────────────────────────────

    private void HandleBinDead()
    {
        _state.Dead = true;
        _state.ShowDeathPanel = true;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = "¡Has muerto!", Color = "FF0000" });
        GD.Print("[GAME] Player died — Dead (binary)");
    }

    private void HandleBinMeditateToggle()
    {
        _state.Meditating = !_state.Meditating;
        if (!_state.Meditating && _state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ClearMeditationFx(ch);
        }
    }

    private void HandleBinRemoveDialogs()
    {
        foreach (var kvp in _state.Characters)
        {
            kvp.Value.DialogText = "";
            kvp.Value.DialogDurationMs = 0;
        }
    }

    private void HandleBinRemoveCharDialog(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.DialogText = "";
            ch.DialogDurationMs = 0;
        }
    }

    private void HandleBinParalizeOk(ByteQueue bq)
    {
        // ParalizeOK is a simple toggle in binary (no extra fields)
        _state.UserParalyzed = !_state.UserParalyzed;
    }

    private void HandleBinTradeOk()
    {
        _state.Trading = false;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = "Comercio exitoso.", Color = "00FF00" });
    }

    private void HandleBinChangeUserTradeSlot(ByteQueue bq)
    {
        byte offerSlot = bq.ReadByte();
        short objIndex = bq.ReadInteger();
        int amount = bq.ReadLong();
        short grhIndex = bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short def = bq.ReadInteger();
        int value = bq.ReadLong();
        string name = bq.ReadString();
        GD.Print($"[PKT] ChangeUserTradeSlot (binary): slot={offerSlot} {name} x{amount}");
    }

    private void HandleBinPong()
    {
        ulong now = Time.GetTicksMsec();
        ulong rtt = now - _state.PingSentMs;
        string lagLabel;
        if (rtt < 100) lagLabel = "0 Lag";
        else if (rtt < 200) lagLabel = "Bajo";
        else if (rtt < 400) lagLabel = "Medio";
        else if (rtt < 900) lagLabel = "Alto";
        else lagLabel = "Injugable";
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"<<Recibido: el ping es de {rtt} Mili-Segundos ({rtt / 1000.0:F3} Seg) LAG: {lagLabel}",
            Color = "00FF00"
        });
    }

    private void HandleBinUpdateTagAndStatus(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        byte nickColor = bq.ReadByte();
        string tag = bq.ReadString();

        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.Criminal = nickColor == 2;
            if (!string.IsNullOrEmpty(tag))
                ch.Name = tag;
        }
    }

    // ── Map extras ────────────────────────────────────────────────

    private void HandleBinMapMusic(ByteQueue bq)
    {
        byte midiId = bq.ReadByte();
        _state.MusicId = midiId;
        OnPlayMusic?.Invoke(midiId);
    }

    private void HandleBinCharData(ByteQueue bq)
    {
        short idx = bq.ReadInteger();
        byte color = bq.ReadByte();
        short auraA = bq.ReadInteger();
        short auraW = bq.ReadInteger();
        short auraE = bq.ReadInteger();
        short auraR = bq.ReadInteger();
        short auraC = bq.ReadInteger();
        bool levitando = bq.ReadBoolean();
        byte ranking = bq.ReadByte();

        if (_state.Characters.TryGetValue(idx, out var ch))
        {
            ch.AuraIndexA = auraA;
            ch.AuraIndexW = auraW;
            ch.AuraIndexE = auraE;
            ch.AuraIndexR = auraR;
            ch.AuraIndexC = auraC;
            ch.Levitating = levitando;
        }
    }

    private void HandleBinAuraUpdate(ByteQueue bq)
    {
        short idx = bq.ReadInteger();
        short auraA = bq.ReadInteger();
        short auraW = bq.ReadInteger();
        short auraE = bq.ReadInteger();
        short auraR = bq.ReadInteger();
        short auraC = bq.ReadInteger();

        if (_state.Characters.TryGetValue(idx, out var ch))
        {
            ch.AuraIndexA = auraA;
            ch.AuraIndexW = auraW;
            ch.AuraIndexE = auraE;
            ch.AuraIndexR = auraR;
            ch.AuraIndexC = auraC;
        }
    }

    private void HandleBinCharacterInfo(ByteQueue bq)
    {
        string name = bq.ReadString();
        byte race = bq.ReadByte();
        byte charClass = bq.ReadByte();
        byte gender = bq.ReadByte();
        byte level = bq.ReadByte();
        int gold = bq.ReadLong();
        int bankGold = bq.ReadLong();
        int reputation = bq.ReadLong();
        string description = bq.ReadString();
        string guildName = bq.ReadString();
        GD.Print($"[PKT] CharacterInfo (binary): {name} Lvl {level} Guild={guildName}");
    }

    // ── Console message by ID ─────────────────────────────────────

    private void HandleBinConsoleMsgId(ByteQueue bq)
    {
        short msgId = bq.ReadInteger();
        string args = bq.ReadString();

        if (msgId > 0 && msgId < _state.TextMessages.Length)
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
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
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
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = "FF0000" });
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
            _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = hexColor });
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
            _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = hexColor });
        }
    }

    private void HandleBinChatWhisper(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color });
    }

    private void HandleBinChatClan(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color });
    }

    private void HandleBinOnlineCount(ByteQueue bq)
    {
        short count = bq.ReadInteger();
        _state.OnlineCount = count;
    }

    // ── MultiMessage ──────────────────────────────────────────────

    private void HandleBinMultiMessage(ByteQueue bq)
    {
        byte subType = bq.ReadByte();

        switch (subType)
        {
            case 0: // NPCSwing
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "La criatura fallo el golpe!!!", Color = "FF0000" });
                break;
            case 1: // NPCKillUser
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "La criatura te ha matado!!!", Color = "FF0000" });
                break;
            case 2: // BlockedWithShieldUser
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has bloqueado el ataque con el escudo!", Color = "FF0000" });
                break;
            case 3: // BlockedWithShieldOther
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "El escudo del enemigo bloqueo tu ataque!", Color = "FF0000" });
                break;
            case 4: // UserSwing
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has fallado el golpe!!!", Color = "FF0000" });
                break;
            case 5: // SafeModeOn
                _state.SafeMode = true;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO ACTIVADO<<", Color = "00FF00" });
                break;
            case 6: // SafeModeOff
                _state.SafeMode = false;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DESACTIVADO<<", Color = "FF0000" });
                break;
            case 7: // ResuscitationSafeOff
                _state.SeguroResu = false;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION DESACTIVADO<<", Color = "FF0000" });
                break;
            case 8: // ResuscitationSafeOn
                _state.SeguroResu = true;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION ACTIVADO<<", Color = "00FF00" });
                break;
            case 9: // NobilityLost
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has perdido tu nobleza!", Color = "FF0000" });
                break;
            case 10: // CantUseWhileMeditating
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "No puedes usar eso mientras meditas!", Color = "FF0000" });
                break;
            case 12: // NPCHitUser
            {
                byte bodyPart = bq.ReadByte();
                short damage = bq.ReadInteger();
                string bodyName = GetNpcHitBodyPartText(bodyPart);
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{bodyName}{damage}", Color = "FF0000" });
                break;
            }
            case 13: // UserHitNPC
            {
                int damage = bq.ReadLong();
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Le has pegado a la criatura por {damage}!!", Color = "FF0000" });
                break;
            }
            case 14: // UserAttackedSwing
            {
                short attackerIndex = bq.ReadInteger();
                string attackerName = "";
                if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
                    attackerName = attacker.Name;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{attackerName} te ataco y fallo!!", Color = "FF0000" });
                break;
            }
            case 15: // UserHittedByUser
            {
                short attackerIndex = bq.ReadInteger();
                byte bodyPart = bq.ReadByte();
                short damage = bq.ReadInteger();
                string attackerName = "";
                if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
                    attackerName = attacker.Name;
                string bodyName = GetPvpReceivedBodyPartText(bodyPart);
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{attackerName}{bodyName}{damage}", Color = "FF0000" });
                break;
            }
            case 16: // UserHittedUser
            {
                short victimIndex = bq.ReadInteger();
                byte bodyPart = bq.ReadByte();
                short damage = bq.ReadInteger();
                string victimName = "";
                if (_state.Characters.TryGetValue(victimIndex, out var victim))
                    victimName = victim.Name;
                string bodyName = GetPvpDealtBodyPartText(bodyPart);
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Le has pegado a {victimName}{bodyName}{damage}", Color = "FF0000" });
                break;
            }
            case 17: // WorkRequestTarget
                GD.Print("[PKT] MultiMessage: WorkRequestTarget");
                break;
            case 18: // HaveKilledUser
            {
                short killedIndex = bq.ReadInteger();
                int expGained = bq.ReadLong();
                string killedName = "";
                if (_state.Characters.TryGetValue(killedIndex, out var killed))
                    killedName = killed.Name;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Has matado a {killedName}! Ganaste {expGained} exp.", Color = "FF0000" });
                break;
            }
            case 19: // UserKill
            {
                short killerIndex = bq.ReadInteger();
                string killerName = "";
                if (_state.Characters.TryGetValue(killerIndex, out var killer))
                    killerName = killer.Name;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{killerName} te ha matado!!!", Color = "FF0000" });
                break;
            }
            case 20: // EarnExp
                GD.Print("[PKT] MultiMessage: EarnExp");
                break;
            case 21: // GoHome
            {
                byte distance = bq.ReadByte();
                short time = bq.ReadInteger();
                string homeCity = bq.ReadString();
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Regresando a {homeCity} en {time}s (distancia: {distance})...", Color = "00FF00" });
                break;
            }
            case 22: // CancelGoHome
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Regreso a hogar cancelado.", Color = "FF0000" });
                break;
            case 23: // FinishHome
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has regresado a tu hogar.", Color = "00FF00" });
                break;
            default:
                GD.Print($"[PKT] MultiMessage unknown sub-type={subType}");
                break;
        }
    }

    // ── Movement / Appearance ────────────────────────────────────────

    /// <summary>
    /// HeadingChange (ID 107) — broadcast char heading to area.
    /// Wire: i16 charIndex, u8 heading
    /// Matches VB6 |H opcode.
    /// </summary>
    private void HandleBinHeadingChange(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        byte heading = bq.ReadByte();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Heading = heading;
    }

    // ── Mount / Levitate ─────────────────────────────────────────────

    /// <summary>
    /// UserMount (ID 142) — mount/dismount a character.
    /// Wire: i16 charIndex, bool mounted
    /// Matches VB6 USM opcode.
    /// </summary>
    private void HandleBinUserMount(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool mounted = bq.ReadBoolean();

        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Mounted = mounted;

        if (charIndex == _state.UserCharIndex)
            _state.UserMounted = mounted;
    }

    /// <summary>
    /// Levitate (ID 143) — levitation state for a character.
    /// Wire: i16 charIndex, bool levitating
    /// Matches VB6 MVOL opcode.
    /// </summary>
    private void HandleBinLevitate(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool levitating = bq.ReadBoolean();

        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Levitating = levitating;
    }

    // ── Animation / Equipment stats ──────────────────────────────────

    /// <summary>
    /// AnimData (ID 225) — equipment hitbox stats (20 comma-separated fields).
    /// Wire: string data (CSV: armaMin,armaMax,armorMin,armorMax,escuMin,escuMax,
    ///        cascMin,cascMax,herrMin,herrMax, then 10 magic defense fields)
    /// Matches VB6 ANM opcode.
    /// </summary>
    private void HandleBinAnimData(ByteQueue bq)
    {
        string data = bq.ReadString();
        var parts = data.Split(',');
        if (parts.Length < 20) return;

        _state.AttackMin = int.TryParse(parts[0], out var v0) ? v0 : 0;
        _state.AttackMax = int.TryParse(parts[1], out var v1) ? v1 : 0;

        int armorMin  = int.TryParse(parts[2], out var p2)  ? p2  : 0;
        int armorMax  = int.TryParse(parts[3], out var p3)  ? p3  : 0;
        int escuMin   = int.TryParse(parts[4], out var p4)  ? p4  : 0;
        int escuMax   = int.TryParse(parts[5], out var p5)  ? p5  : 0;
        int cascMin   = int.TryParse(parts[6], out var p6)  ? p6  : 0;
        int cascMax   = int.TryParse(parts[7], out var p7)  ? p7  : 0;
        int herrMin   = int.TryParse(parts[8], out var p8)  ? p8  : 0;
        int herrMax   = int.TryParse(parts[9], out var p9)  ? p9  : 0;
        _state.DefenseMin = armorMin + escuMin + cascMin + herrMin;
        _state.DefenseMax = armorMax + escuMax + cascMax + herrMax;

        int magMin  = int.TryParse(parts[10], out var m0) ? m0 : 0;
        int magMax  = int.TryParse(parts[11], out var m1) ? m1 : 0;
        int magMina = int.TryParse(parts[12], out var m2) ? m2 : 0;
        int magMaxa = int.TryParse(parts[13], out var m3) ? m3 : 0;
        int magMinb = int.TryParse(parts[14], out var m4) ? m4 : 0;
        int magMaxb = int.TryParse(parts[15], out var m5) ? m5 : 0;
        int magMinc = int.TryParse(parts[16], out var m6) ? m6 : 0;
        int magMaxc = int.TryParse(parts[17], out var m7) ? m7 : 0;
        int magMind = int.TryParse(parts[18], out var m8) ? m8 : 0;
        int magMaxd = int.TryParse(parts[19], out var m9) ? m9 : 0;
        _state.MagDefMin = magMin + magMina + magMinb + magMinc + magMind;
        _state.MagDefMax = magMax + magMaxa + magMaxb + magMaxc + magMaxd;
    }

    // ── Timer ────────────────────────────────────────────────────────

    /// <summary>
    /// TimerInfo (ID 246) — scroll/timer slot (TIS opcode). Ignored for now.
    /// Wire: u8 id, i32 time1, i32 time2
    /// </summary>
    private void HandleBinTimerInfo(ByteQueue bq)
    {
        byte id = bq.ReadByte();
        int time1 = bq.ReadLong();
        int time2 = bq.ReadLong();
        // Scroll timers not yet implemented on the client.
        GD.Print($"[PKT] TimerInfo id={id} t1={time1} t2={time2}");
    }

    // ── Class options ────────────────────────────────────────────────

    /// <summary>
    /// ClassOptions (ID 144) — level bonus class selection (99 opcode).
    /// Wire: u8 option1, u8 option2
    /// Stub — class bonus UI not yet implemented.
    /// </summary>
    private void HandleBinClassOptions(ByteQueue bq)
    {
        byte opt1 = bq.ReadByte();
        byte opt2 = bq.ReadByte();
        GD.Print($"[PKT] ClassOptions opt1={opt1} opt2={opt2} (bonus selection UI not yet implemented)");
    }

    // ── Particles / Lights ───────────────────────────────────────────

    /// <summary>
    /// ParticleCreate (ID 243) — map particle stream (PCF opcode).
    /// Wire: i16 particleGroup, u8 x, u8 y, u8 layer
    /// </summary>
    private void HandleBinParticleCreate(ByteQueue bq)
    {
        short particleGroup = bq.ReadInteger();
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        byte layer = bq.ReadByte();
        ParticleSystem.CreateMapStream(_state, particleGroup, x, y);
    }

    /// <summary>
    /// LightCreate (ID 244) — map tile light effect (PCL opcode).
    /// Wire: u8 x, u8 y, u8 range, u8 r, u8 g, u8 b
    /// </summary>
    private void HandleBinLightCreate(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        byte range = bq.ReadByte();
        byte r = bq.ReadByte();
        byte g = bq.ReadByte();
        byte b = bq.ReadByte();
        var light = new MapLight
        {
            X = x,
            Y = y,
            Range = range,
            R = r,
            G = g,
            B = b,
            Active = true
        };
        _state.MapLights.Add(light);
        _state.LightsDirty = true;
    }

    // ════════════════════════════════════════════════════════════════
    // Handlers added during full-opcode coverage pass
    // ════════════════════════════════════════════════════════════════

    // ── Auth / Login (continued) ──────────────────────────────────

    private void HandleBinShowMessageBox2(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
        GD.Print($"[GM] MessageBox2 (binary): {msg}");
    }

    private void HandleBinUserIndexAlt(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        GD.Print($"[GAME] UserIndexAlt: {index}");
    }

    private void HandleBinUserCharIndexAlt(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserCharIndex = index;
        GD.Print($"[GAME] UserCharIndexAlt: {index}");
    }

    private void HandleBinDiceRollAlt(ByteQueue bq)
    {
        byte str = bq.ReadByte();
        byte agi = bq.ReadByte();
        byte intel = bq.ReadByte();
        byte con = bq.ReadByte();
        byte cha = bq.ReadByte();
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Dados: Fuerza={str} Agilidad={agi} Inteligencia={intel} Constitucion={con} Carisma={cha}",
            Color = "FFFF00"
        });
    }

    private void HandleBinAccountData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] AccountData (binary): {data}");
        _state.RawAccountData = data;
    }

    // ── Movement / Projectiles ────────────────────────────────────

    /// <summary>
    /// Arrow (ID 108) — projectile arrow from src to tgt (FLECHI opcode).
    /// Wire: i16 srcIndex, i16 tgtIndex, i16 grhIndex
    /// </summary>
    private void HandleBinArrow(ByteQueue bq)
    {
        short srcIndex = bq.ReadInteger();
        short tgtIndex = bq.ReadInteger();
        short grhIndex = bq.ReadInteger();

        // Create visual arrow projectile (same as text FLECHI handler)
        if (_state.Characters.TryGetValue(srcIndex, out var srcCh) &&
            _state.Characters.TryGetValue(tgtIndex, out var tgtCh))
        {
            float srcPixelX = srcCh.PosX * 32f + 16f;
            float srcPixelY = srcCh.PosY * 32f + 16f;
            float tgtPixelX = tgtCh.PosX * 32f + 16f;
            float tgtPixelY = tgtCh.PosY * 32f + 16f;
            _state.ActiveArrows.Add(new ArrowProjectile
            {
                ShooterCharIndex = srcIndex,
                TargetCharIndex = tgtIndex,
                GrhIndex = grhIndex,
                X = srcPixelX, Y = srcPixelY,
                TargetX = tgtPixelX, TargetY = tgtPixelY,
                Speed = 8f,
                Active = true,
            });
        }
        GD.Print($"[PKT] Arrow: src={srcIndex} tgt={tgtIndex} grh={grhIndex}");
    }

    /// <summary>
    /// NavigateBroadcast (ID 109) — broadcast navigation state for a char (NVG opcode).
    /// Wire: i16 charIndex, bool navigating
    /// </summary>
    private void HandleBinNavigateBroadcast(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool navigating = bq.ReadBoolean();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Navigating = navigating;
    }

    // ── Chat (continued) ──────────────────────────────────────────

    private void HandleBinChatGuild2(ByteQueue bq)
    {
        string msg = bq.ReadString();
        byte fontIndex = bq.ReadByte();
        string color = FontTypes.GetHexColor(fontIndex);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color });
    }

    // ── Stat variants ─────────────────────────────────────────────

    private void HandleBinStatName(ByteQueue bq)
    {
        string name = bq.ReadString();
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            ch.Name = name;
        GD.Print($"[PKT] StatName: {name}");
    }

    private void HandleBinStatBulk(ByteQueue bq)
    {
        short bulk = bq.ReadInteger();
        _state.CarryBulk = bulk;
    }

    /// <summary>
    /// HungerThirst (ID 128) — same layout as UpdateHungerAndThirst (ID 60).
    /// Wire: u8 maxAgua, u8 minAgua, u8 maxHam, u8 minHam
    /// </summary>
    private void HandleBinHungerThirst128(ByteQueue bq)
    {
        _state.MaxAgua = bq.ReadByte();
        _state.MinAgua = bq.ReadByte();
        _state.MaxHam = bq.ReadByte();
        _state.MinHam = bq.ReadByte();
    }

    // ── Safe / Combat state ───────────────────────────────────────

    /// <summary>
    /// UserSwing (ID 134) — attacker index who missed player.
    /// Wire: i16 attackerIndex
    /// </summary>
    private void HandleBinUserSwing(ByteQueue bq)
    {
        short attackerIndex = bq.ReadInteger();
        string attackerName = "";
        if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
            attackerName = attacker.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attackerName} te ataco y fallo!!",
            Color = "FF0000"
        });
    }

    /// <summary>
    /// UserHit (ID 135) — attacker index + damage that hit player.
    /// Wire: i16 attackerIndex, i16 damage
    /// </summary>
    private void HandleBinUserHit(ByteQueue bq)
    {
        short attackerIndex = bq.ReadInteger();
        short damage = bq.ReadInteger();
        string attackerName = "";
        if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
            attackerName = attacker.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attackerName} te pego {damage}!!",
            Color = "FF0000"
        });
    }

    /// <summary>
    /// NpcHit (ID 137) — NPC hit player. Wire: u8 bodyPart, i16 damage
    /// </summary>
    private void HandleBinNpcHit(ByteQueue bq)
    {
        byte bodyPart = bq.ReadByte();
        short damage = bq.ReadInteger();
        string bodyName = GetNpcHitBodyPartText(bodyPart);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{bodyName}{damage}", Color = "FF0000" });
    }

    /// <summary>
    /// PvpDmgRecv (ID 138) — PvP damage received. Wire: i16 attackerIndex, i16 damage
    /// </summary>
    private void HandleBinPvpDmgRecv(ByteQueue bq)
    {
        short attackerIndex = bq.ReadInteger();
        short damage = bq.ReadInteger();
        string attackerName = "";
        if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
            attackerName = attacker.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attackerName} te pego {damage} en PvP!!",
            Color = "FF0000"
        });
    }

    /// <summary>
    /// PvpDmgDeal (ID 139) — PvP damage dealt. Wire: i16 victimIndex, i16 damage
    /// </summary>
    private void HandleBinPvpDmgDeal(ByteQueue bq)
    {
        short victimIndex = bq.ReadInteger();
        short damage = bq.ReadInteger();
        string victimName = "";
        if (_state.Characters.TryGetValue(victimIndex, out var victim))
            victimName = victim.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Le pegaste {damage} a {victimName} en PvP!!",
            Color = "FF0000"
        });
    }

    // ── Spells ────────────────────────────────────────────────────

    /// <summary>
    /// SpellInfoResp (ID 148) — spell info string (INFS response).
    /// Wire: string data
    /// </summary>
    private void HandleBinSpellInfoResp(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] SpellInfoResp (binary): {data}");
        _state.SpellInfoText = data;
    }

    // ── Sound ─────────────────────────────────────────────────────

    /// <summary>
    /// PlaySound (ID 150) — play a sound effect by index.
    /// Wire: u8 soundIndex
    /// </summary>
    private void HandleBinPlaySound(ByteQueue bq)
    {
        byte soundIndex = bq.ReadByte();
        if (soundIndex > 0)
            OnPlaySound?.Invoke(soundIndex);
    }

    // ── Work / Crafting ───────────────────────────────────────────

    /// <summary>
    /// WorkMode (ID 155) — work mode skill toggle (T01 opcode).
    /// Wire: u8 skill
    /// </summary>
    private void HandleBinWorkMode(ByteQueue bq)
    {
        byte skill = bq.ReadByte();
        GD.Print($"[PKT] WorkMode skill={skill}");
    }

    /// <summary>
    /// SmithWeapons/SmithArmors/CarpItems (IDs 158/159/160) — buildable item list as raw string.
    /// Wire: string data (comma-separated name,idx pairs)
    /// </summary>
    private void HandleBinSmithList(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] {tag} (binary): {data.Length} bytes");
        _state.CraftListData = data;
    }

    /// <summary>
    /// Navigation (ID 162) — navigation mode data string.
    /// Wire: string data
    /// </summary>
    private void HandleBinNavigationData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] Navigation data (binary): {data}");
    }

    /// <summary>
    /// BattleTeamScores (ID 163) — BatallaMistica event kill scores.
    /// Wire: i32 t1, i32 t2, i32 t3, i32 t4
    /// </summary>
    private void HandleBinBattleTeamScores(ByteQueue bq)
    {
        int t1 = bq.ReadLong();
        int t2 = bq.ReadLong();
        int t3 = bq.ReadLong();
        int t4 = bq.ReadLong();
        GD.Print($"[PKT] BattleTeamScores: {t1}/{t2}/{t3}/{t4}");
        _state.BattleTeamScores = $"{t1},{t2},{t3},{t4}";
    }

    /// <summary>
    /// AmbientColor (ID 164) — map ambient RGB override (PCR opcode).
    /// Wire: u8 r, u8 g, u8 b
    /// </summary>
    private void HandleBinAmbientColor(ByteQueue bq)
    {
        byte r = bq.ReadByte();
        byte g = bq.ReadByte();
        byte b = bq.ReadByte();
        _state.AmbientColorR = r;
        _state.AmbientColorG = g;
        _state.AmbientColorB = b;
        // Also set MapColor fields — WorldRenderer reads these for ambient light
        _state.MapColorR = r;
        _state.MapColorG = g;
        _state.MapColorB = b;
        GD.Print($"[PKT] AmbientColor R={r} G={g} B={b}");
    }

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
        GD.Print("[PKT] BankReset (binary)");
    }

    private void HandleBinInitBankLegacy(ByteQueue bq)
    {
        string data = bq.ReadString();
        _state.Banqueando = true;
        GD.Print($"[PKT] InitBankLegacy (binary): {data}");
    }

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
    private void HandleBinNpcInvItem(ByteQueue bq)
    {
        // Same layout as ChangeNPCInventorySlot (ID 51)
        HandleBinChangeNpcInvSlot(bq);
    }

    /// <summary>
    /// TransOK (ID 174) — NPC commerce buy/sell confirmation.
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

    /// <summary>
    /// TournamentPoints (ID 176). Wire: i32 points
    /// </summary>
    private void HandleBinTournamentPoints(ByteQueue bq)
    {
        int pts = bq.ReadLong();
        GD.Print($"[PKT] TournamentPoints: {pts}");
        _state.TournamentPoints = pts;
    }

    /// <summary>
    /// ResponseMsg (ID 177) — server response text (RESPUES opcode).
    /// Wire: string text, string name
    /// </summary>
    private void HandleBinResponseMsg(ByteQueue bq)
    {
        string text = bq.ReadString();
        string name = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "00FFFF" });
        GD.Print($"[PKT] ResponseMsg: {text} (name={name})");
    }

    /// <summary>
    /// AuctionBid (ID 179) — auction bid info string (GVN opcode).
    /// Wire: string data
    /// </summary>
    private void HandleBinAuctionBid(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] AuctionBid (binary): {data}");
        _state.AuctionBidData = data;
    }

    // ── Trading (legacy) ──────────────────────────────────────────

    /// <summary>
    /// TradeInitLegacy (ID 180) — legacy trade init. Wire: string partnerName
    /// </summary>
    private void HandleBinTradeInitLegacy(ByteQueue bq)
    {
        string partnerName = bq.ReadString();
        _state.Trading = true;
        GD.Print($"[PKT] TradeInitLegacy: partner={partnerName}");
    }

    /// <summary>
    /// TradeOfferRecv (ID 181) — trade gold offer received. Wire: i32 gold
    /// </summary>
    private void HandleBinTradeOfferRecv(ByteQueue bq)
    {
        int gold = bq.ReadLong();
        GD.Print($"[PKT] TradeOfferRecv: gold={gold}");
        _state.TradePartnerGold = gold;
    }

    /// <summary>
    /// TradeItems (ID 182) — trade item info. Wire: i16 objIndex, i16 amount, string name
    /// </summary>
    private void HandleBinTradeItems(ByteQueue bq)
    {
        short objIndex = bq.ReadInteger();
        short amount = bq.ReadInteger();
        string name = bq.ReadString();
        GD.Print($"[PKT] TradeItems: {name} x{amount} (obj={objIndex})");
    }

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
    private void HandleBinGuildInfoStr(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] GuildInfo{tag} (binary): {data.Length} chars");
        _state.GuildInfoData = data;
    }

    /// <summary>
    /// GuildBankPermsResp (ID 195) — guild bank permissions.
    /// Wire: bool canObj, bool canGold
    /// </summary>
    private void HandleBinGuildBankPermsResp(ByteQueue bq)
    {
        bool canObj = bq.ReadBoolean();
        bool canGold = bq.ReadBoolean();
        GD.Print($"[PKT] GuildBankPermsResp: canObj={canObj} canGold={canGold}");
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
        _state.ChatMessages.Enqueue(new ChatMessage { Text = msg, Color = color });
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
        short grh = bq.ReadInteger();
        byte objType = bq.ReadByte();
        short maxHit = bq.ReadInteger();
        short minHit = bq.ReadInteger();
        short maxDef = bq.ReadInteger();
        int bankGold = bq.ReadLong();
        int userGold = bq.ReadLong();
        GD.Print($"[PKT] GuildBankSlotData slot={slot} {name} x{amount} bankGold={bankGold}");
    }

    // ── Quest ─────────────────────────────────────────────────────

    /// <summary>
    /// QuestListResp / QuestCurrent / QuestSelected — quest data as raw string.
    /// Wire: string data
    /// </summary>
    private void HandleBinQuestData(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] {tag} (binary): {data.Length} chars");
        _state.QuestData = data;
    }

    // ── Mail ──────────────────────────────────────────────────────

    /// <summary>
    /// MailList / MailPlayerInfo / MailFriends / MailContent / MailItems — mail data.
    /// Wire: string data
    /// </summary>
    private void HandleBinMailData(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] {tag} (binary): {data.Length} chars");
        _state.MailData = data;
    }

    // ── Friends ───────────────────────────────────────────────────

    /// <summary>
    /// FriendList (ID 210) — friends list data string (LDM opcode).
    /// Wire: string data
    /// </summary>
    private void HandleBinFriendList(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] FriendList (binary): {data.Length} chars");
        _state.FriendListData = data;

        // Parse CSV into FriendsList (same as text LDM handler)
        _state.FriendsList.Clear();
        if (!string.IsNullOrEmpty(data))
        {
            var parts = data.Split(',');
            // First field is count — skip it, take names from index 1 onward
            for (int i = 1; i < parts.Length; i++)
            {
                var entry = parts[i].Trim();
                if (entry.Length == 0) continue;
                _state.FriendsList.Add(entry);
            }
        }
        while (_state.FriendsList.Count < 20)
            _state.FriendsList.Add("(NADIE)(OFF)");
        _state.FriendsListDirty = true;
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
        GD.Print($"[PKT] MenuData: {name} priv={priv}");
        _state.MenuTargetName = name;
        _state.MenuTargetPriv = priv;
    }

    /// <summary>
    /// SelectData (ID 222) — selection list data (SELE opcode).
    /// Wire: string data
    /// </summary>
    private void HandleBinSelectData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] SelectData (binary): {data}");
        _state.SelectData = data;
    }

    /// <summary>
    /// MiniTopData (ID 223) — mini ranking data (MTOP opcode).
    /// Wire: string data
    /// </summary>
    private void HandleBinMiniTopData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] MiniTopData (binary): {data.Length} chars");
        _state.MiniTopData = data;
    }

    /// <summary>
    /// Generic single-string packets (ImageData, BkwData, FestData, GinfData,
    /// IcoData, ZsosData, SbrData, AuctionList, CosmeticImage/Pcgn/Pcss/Pccc,
    /// FullCharInfo) — read one string and log.
    /// </summary>
    private void HandleBinStringOnly(ByteQueue bq, string tag)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] {tag} (binary): {data.Length} chars");
    }

    /// <summary>
    /// EnchatData (ID 228) — enter chat room. Wire: string name
    /// </summary>
    private void HandleBinEnchatData(ByteQueue bq)
    {
        string name = bq.ReadString();
        GD.Print($"[PKT] EnchatData: {name}");
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

    /// <summary>
    /// KfmData (ID 235) — friend came online. Wire: string name
    /// </summary>
    private void HandleBinKfmData(ByteQueue bq)
    {
        string name = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{name} se ha conectado.", Color = "00FF00" });
        GD.Print($"[PKT] KfmData: {name} online");
    }

    /// <summary>
    /// DfmData (ID 236) — friend went offline. Wire: string name
    /// </summary>
    private void HandleBinDfmData(ByteQueue bq)
    {
        string name = bq.ReadString();
        _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{name} se ha desconectado.", Color = "FF8800" });
        GD.Print($"[PKT] DfmData: {name} offline");
    }

    // ── Cosmetics ─────────────────────────────────────────────────

    /// <summary>
    /// CosmeticSurgery (ID 238) — cosmetic surgery options. Wire: u8 raza, u8 genero
    /// </summary>
    private void HandleBinCosmeticSurgery(ByteQueue bq)
    {
        byte raza = bq.ReadByte();
        byte genero = bq.ReadByte();
        GD.Print($"[PKT] CosmeticSurgery raza={raza} genero={genero}");
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
        _state.BovedaAbierta = true;
        GD.Print($"[PKT] GuildBankInitResp canObj={canObj} canGold={canGold}");
    }

    /// <summary>
    /// GuildBankSlotResp (ID 248) — guild bank slot (BANCOBK opcode).
    /// Wire: u8 slot, u8 objType
    /// </summary>
    private void HandleBinGuildBankSlotResp(ByteQueue bq)
    {
        byte slot = bq.ReadByte();
        byte objType = bq.ReadByte();
        GD.Print($"[PKT] GuildBankSlotResp slot={slot} type={objType}");
    }

    // ── Ping ──────────────────────────────────────────────────────

    /// <summary>
    /// Ping (ID 250) — server→client ping request. Respond immediately with Pong.
    /// </summary>
    private void HandleBinPingRequest()
    {
        // Send pong back to server (using existing binary ping/pong infrastructure)
        GD.Print("[PKT] Ping received — sending Pong");
        // The TcpClient's SendPing() method sends the pong response.
        _state.PingSentMs = Time.GetTicksMsec();
    }

    // ── Arena ─────────────────────────────────────────────────────

    /// <summary>
    /// ArenaData (ID 254) — arena duel list (MAR opcode).
    /// Wire: string names (8 comma-separated duel slot names)
    /// </summary>
    private void HandleBinArenaData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] ArenaData (binary): {data}");
        _state.ArenaDuelData = data;
    }

    // ── GenericText fallback ──────────────────────────────────────

    /// <summary>
    /// GenericText (ID 255) — legacy text packet bridge.
    /// Wire: u16 LE length, then ASCII text bytes.
    /// Dispatches through the legacy text handler.
    /// </summary>
    private void HandleGenericTextPacket(ByteQueue bq)
    {
        short len = bq.ReadInteger();
        if (len <= 0) return;

        var bytes = new byte[len];
        for (int i = 0; i < len; i++)
            bytes[i] = bq.ReadByte();

        string text = System.Text.Encoding.ASCII.GetString(bytes);
        HandlePacket(text);
    }
}

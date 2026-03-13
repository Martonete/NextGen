using Godot;
using System;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Game;

/// <summary>
/// Synchronizes panel visibility with GameState flags each frame.
/// Extracted from Main._Process() panel state tracking block.
/// </summary>
public class PanelStateSync
{
    private readonly GameState _state;

    // Panel references
    private CommercePanel? _commercePanel;
    private TradePanel? _tradePanel;
    private BankPanel? _bankPanel;
    private VaultPanel? _vaultPanel;
    private GuildBankPanel? _guildBankPanel;
    private CraftPanel? _craftPanel;
    private GuildPanel? _guildPanel;
    private GuildFoundationPanel? _guildFoundationPanel;
    private ForumPanel? _forumPanel;
    private MailPanel? _mailPanel;
    private PartyPanel? _partyPanel;
    private TravelPanel? _travelPanel;
    private QuestPanel? _questPanel;
    private TrainerPanel? _trainerPanel;
    private NpcDialogPanel? _npcDialogPanel;
    private ChangePasswordPanel? _changePasswordPanel;
    private CharInfoPopup? _charInfoPopup;
    private DeathPanel? _deathPanel;
    private OptionsPanel? _optionsPanel;
    private TooltipPanel? _tooltipPanel;
    private ColorRect? _blindOverlay;

    // New panels
    private GmPanel? _gmPanel;
    private SosPanel? _sosPanel;
    private PeaceProposalPanel? _peaceProposalPanel;
    private GuildAlignmentPanel? _guildAlignmentPanel;
    private MotdEditorPanel? _motdEditorPanel;
    private GuildMemberPanel? _guildMemberPanel;
    private Rendering.DayNightCycle? _dayNightCycle;
    private LoadingScreen? _loadingScreen;
    private TutorialPanel? _tutorialPanel;

    // State tracking for edge detection
    private bool _lastComerciando;
    private bool _lastTrading;
    private bool _lastBanqueando;
    private float _blindAlpha;

    /// <summary>Callback to update drop dialog visibility.</summary>
    public Action? UpdateDropDialogVisibility;

    public PanelStateSync(GameState state)
    {
        _state = state;
    }

    /// <summary>Bind all panel references.</summary>
    public void BindPanels(
        CommercePanel? commercePanel, TradePanel? tradePanel,
        BankPanel? bankPanel, VaultPanel? vaultPanel,
        GuildBankPanel? guildBankPanel, CraftPanel? craftPanel,
        GuildPanel? guildPanel, GuildFoundationPanel? guildFoundationPanel,
        ForumPanel? forumPanel,
        MailPanel? mailPanel, PartyPanel? partyPanel,
        TravelPanel? travelPanel, QuestPanel? questPanel,
        TrainerPanel? trainerPanel, NpcDialogPanel? npcDialogPanel,
        ChangePasswordPanel? changePasswordPanel, CharInfoPopup? charInfoPopup,
        DeathPanel? deathPanel, OptionsPanel? optionsPanel,
        TooltipPanel? tooltipPanel, ColorRect? blindOverlay)
    {
        _commercePanel = commercePanel;
        _tradePanel = tradePanel;
        _bankPanel = bankPanel;
        _vaultPanel = vaultPanel;
        _guildBankPanel = guildBankPanel;
        _craftPanel = craftPanel;
        _guildPanel = guildPanel;
        _guildFoundationPanel = guildFoundationPanel;
        _forumPanel = forumPanel;
        _mailPanel = mailPanel;
        _partyPanel = partyPanel;
        _travelPanel = travelPanel;
        _questPanel = questPanel;
        _trainerPanel = trainerPanel;
        _npcDialogPanel = npcDialogPanel;
        _changePasswordPanel = changePasswordPanel;
        _charInfoPopup = charInfoPopup;
        _deathPanel = deathPanel;
        _optionsPanel = optionsPanel;
        _tooltipPanel = tooltipPanel;
        _blindOverlay = blindOverlay;
    }

    /// <summary>Bind new feature panels added in parity update.</summary>
    public void BindNewPanels(
        GmPanel? gmPanel, SosPanel? sosPanel,
        PeaceProposalPanel? peaceProposalPanel, GuildAlignmentPanel? guildAlignmentPanel,
        MotdEditorPanel? motdEditorPanel, GuildMemberPanel? guildMemberPanel,
        DayNightCycle? dayNightCycle, LoadingScreen? loadingScreen,
        TutorialPanel? tutorialPanel)
    {
        _gmPanel = gmPanel;
        _sosPanel = sosPanel;
        _peaceProposalPanel = peaceProposalPanel;
        _guildAlignmentPanel = guildAlignmentPanel;
        _motdEditorPanel = motdEditorPanel;
        _guildMemberPanel = guildMemberPanel;
        _dayNightCycle = dayNightCycle;
        _loadingScreen = loadingScreen;
        _tutorialPanel = tutorialPanel;
    }

    /// <summary>Reset edge-detection tracking state (on disconnect).</summary>
    public void ResetTracking()
    {
        _lastComerciando = false;
        _lastTrading = false;
        _lastBanqueando = false;
    }

    /// <summary>
    /// Called each frame during Screen.Game to sync panel visibility with state flags.
    /// </summary>
    public void Update(float delta)
    {
        // Update floating tooltip position each frame
        _tooltipPanel?.UpdatePosition();

        // Commerce panel state tracking
        if (_state.Comerciando != _lastComerciando)
        {
            _lastComerciando = _state.Comerciando;
            if (_state.Comerciando)
                _commercePanel?.OpenShop();
            else
                _commercePanel?.CloseShop();
        }

        // Trade panel state tracking (player-to-player)
        if (_state.Trading != _lastTrading)
        {
            _lastTrading = _state.Trading;
            if (_state.Trading)
                _tradePanel?.OpenTrade();
            else
                _tradePanel?.CloseTrade();
        }
        // Consume TradeJustOpened flag (set by PacketHandler on trade init)
        if (_state.TradeJustOpened)
        {
            _state.TradeJustOpened = false;
            _tradePanel?.OpenTrade();
        }

        // Bank panel state tracking
        if (_state.Banqueando != _lastBanqueando)
        {
            _lastBanqueando = _state.Banqueando;
            if (_state.Banqueando)
            {
                // Only open BankPanel if vault isn't already open
                if (!_state.BovedaAbierta)
                    _bankPanel?.OpenBank();
            }
            else
            {
                _bankPanel?.CloseBank();
                _vaultPanel?.CloseVault();
                _state.BovedaAbierta = false;
            }
        }

        // Travel panel state tracking
        if (_state.ShowTravelPanel)
        {
            _state.ShowTravelPanel = false;
            _travelPanel?.OpenTravel();
        }

        // Guild panel — open standalone clan panel
        if (_state.ShowGuildPanel)
        {
            _state.ShowGuildPanel = false;
            string viewType = string.IsNullOrEmpty(_state.GuildInfoType) ? "List" : _state.GuildInfoType;
            _guildPanel?.ShowView(viewType);
        }
        if (_state.ShowGuildFoundation)
        {
            _state.ShowGuildFoundation = false;
            _optionsPanel?.Close();
            _guildFoundationPanel?.Show();
        }

        // Forum panel — open from ShowForumForm packet
        if (_state.ShowForumPanel)
        {
            _state.ShowForumPanel = false;
            _forumPanel?.ShowForum();
        }

        // Mail panel — open from MailList/MailOpenTrigger packet
        if (_state.ShowMailPanel)
        {
            _state.ShowMailPanel = false;
            _mailPanel?.ShowPanel();
        }

        // Party panel — open from ShowPartyForm packet
        if (_state.ShowPartyPanel)
        {
            _state.ShowPartyPanel = false;
            _partyPanel?.OpenPanel();
        }

        // Guild bank panel
        if (_state.ShowGuildBank)
        {
            _state.ShowGuildBank = false;
            _guildBankPanel?.OpenGuildBank();
        }

        // Craft panels (blacksmith / carpenter)
        if (_state.ShowBlacksmithForm)
        {
            _state.ShowBlacksmithForm = false;
            _craftPanel?.ShowBlacksmith();
        }
        if (_state.ShowCarpenterForm)
        {
            _state.ShowCarpenterForm = false;
            _craftPanel?.ShowCarpenter();
        }

        // Quest panel — handle quest data from server
        if (!string.IsNullOrEmpty(_state.QuestDataTag))
        {
            string tag = _state.QuestDataTag;
            string payload = _state.QuestDataPayload;
            _state.QuestDataTag = "";
            _state.QuestDataPayload = "";
            _questPanel?.HandleQuestData(tag, payload);
        }
        if (_state.ShowQuestPanel)
        {
            _state.ShowQuestPanel = false;
            _questPanel?.OpenPanel();
        }

        // Trainer panel — open from TrainerCreatureList packet
        if (_state.ShowTrainerPanel)
        {
            _state.ShowTrainerPanel = false;
            string creatures = _state.TrainerCreatureData;
            _state.TrainerCreatureData = "";
            _trainerPanel?.OpenTrainer(creatures);
        }

        // NPC dialog panel — show when NPC speaks via ChatOverHead
        if (_state.ShowNpcDialog)
        {
            _state.ShowNpcDialog = false;
            _npcDialogPanel?.ShowDialog(_state.NpcDialogName, _state.NpcDialogText);
        }

        // Change password panel — triggered by /PASSWD chat command
        if (_state.ShowChangePassword)
        {
            _state.ShowChangePassword = false;
            _changePasswordPanel?.Open();
        }

        // Character info popup — show from FullCharInfo packet (/MIRAR)
        if (_state.ShowCharInfo)
        {
            _state.ShowCharInfo = false;
            if (_state.CharInfoCurrent != null)
                _charInfoPopup?.ShowInfo(_state.CharInfoCurrent);
        }

        // Blind screen overlay — smooth fade in/out
        if (_blindOverlay != null)
        {
            float targetAlpha = _state.UserBlind ? 0.95f : 0.0f;
            float fadeSpeed = delta * 3.3f; // ~0.3s transition
            _blindAlpha = Mathf.MoveToward(_blindAlpha, targetAlpha, fadeSpeed);
            _blindOverlay.Color = new Color(0, 0, 0, _blindAlpha);
        }

        // Death panel — show when player dies, hide on revive
        if (_state.ShowDeathPanel)
        {
            _state.ShowDeathPanel = false;
            if (_state.Config?.ShowDeathDialog ?? true)
                _deathPanel?.Show();
        }
        if (!_state.Dead && _deathPanel != null && _deathPanel.Visible)
        {
            _deathPanel.Hide();
        }

        // Drop quantity dialog (VB6: frmCantidad)
        UpdateDropDialogVisibility?.Invoke();

        // ── New parity panels ──────────────────────────────────

        // SOS/Help panel — triggered when server sends GM SOS notification
        if (_state.ShowSosPanel)
        {
            _state.ShowSosPanel = false;
            _sosPanel?.AddSosEntry(_state.SosPlayerName, _state.SosMessage);
            _sosPanel?.Open();
        }

        // Peace proposal panel
        if (_state.ShowPeaceProposal)
        {
            _state.ShowPeaceProposal = false;
            _peaceProposalPanel?.ShowProposal(_state.PeaceProposalGuild, _state.PeaceProposalType);
        }

        // Guild alignment picker
        if (_state.ShowGuildAlignment)
        {
            _state.ShowGuildAlignment = false;
            _guildAlignmentPanel?.Open();
        }

        // MOTD editor
        if (_state.ShowMotdEditor)
        {
            _state.ShowMotdEditor = false;
            _motdEditorPanel?.Open();
        }

        // Guild member detail
        if (_state.ShowGuildMember)
        {
            _state.ShowGuildMember = false;
            if (_state.GuildMemberIsApplicant)
                _guildMemberPanel?.ShowApplicant(_state.GuildMemberName, _state.GuildMemberPetition);
            else
                _guildMemberPanel?.ShowMember(_state.GuildMemberName);
        }

        // Day/Night cycle — update hour when server sends it
        if (_state.GameHourDirty)
        {
            _state.GameHourDirty = false;
            _dayNightCycle?.SetHour(_state.GameHour);
        }

        // Loading screen
        if (_state.ShowLoadingScreen)
        {
            _state.ShowLoadingScreen = false;
            _loadingScreen?.Show(_state.LoadingMapName);
        }

        // Tutorial
        if (_state.ShowTutorial)
        {
            _state.ShowTutorial = false;
            _tutorialPanel?.Open();
        }
    }

    /// <summary>Close all panels (called on disconnect).</summary>
    public void CloseAll()
    {
        _commercePanel?.CloseShop();
        _tradePanel?.CloseTrade();
        _bankPanel?.CloseBank();
        _vaultPanel?.CloseVault();
        _guildBankPanel?.CloseGuildBank();
        _guildPanel?.Hide();
        _guildFoundationPanel?.Hide();
        _forumPanel?.Hide();
        _mailPanel?.Hide();
        _partyPanel?.Hide();
        _travelPanel?.CloseTravel();
        _questPanel?.Hide();
        _trainerPanel?.Hide();
        _npcDialogPanel?.Hide();
        _changePasswordPanel?.Hide();
        _deathPanel?.Hide();
        _craftPanel?.ClosePanel();
        _gmPanel?.Close();
        _sosPanel?.Hide();
        _peaceProposalPanel?.Hide();
        _guildAlignmentPanel?.Hide();
        _motdEditorPanel?.Close();
        _guildMemberPanel?.Hide();
        _loadingScreen?.ForceHide();
        ResetTracking();
    }
}

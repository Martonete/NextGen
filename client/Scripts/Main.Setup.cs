using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;

namespace ArgentumNextgen;

/// <summary>
/// Partial class containing game UI panel creation and subsystem wiring.
/// Split from Main.cs _Ready() to keep the orchestrator under 800 lines.
/// </summary>
public partial class Main
{
    /// <summary>
    /// Create all game UI panels (inventory, spells, sidebar, commerce, bank, guild, etc.),
    /// HUD labels, stat bars, and wire up extracted subsystems.
    /// Called from _Ready() after game data and core systems are initialized.
    /// </summary>
    private void SetupGamePanels()
    {
        // === Inventory & Spells UI (VB6-exact pixel positions, twips/15) ===

        // InvEqu panel background -- VB6: InvEqu at (581,125) 198x282 pixels
        _invEquImage = new TextureRect();
        _invEquImage.Position = new Vector2(581, 125);
        _invEquImage.Size = new Vector2(198, 282);
        _invEquImage.StretchMode = TextureRect.StretchModeEnum.Scale;
        _invEquImage.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI!.AddChild(_invEquImage);

        // Tab buttons -- VB6 13.3: Label4 "Inventario" (592,128,93,29), Label7 "Hechizos" (688,128,75,30)
        _invTabButton = CreateInvisibleButton(592, 128, 93, 29);
        _gameUI.AddChild(_invTabButton);
        _invTabButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnInventoryTabPressed(); };

        _spellTabButton = CreateInvisibleButton(688, 128, 75, 30);
        _gameUI.AddChild(_spellTabButton);
        _spellTabButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnSpellTabPressed(); };

        // Inventory panel -- VB6: picInv at (600,160) 160x128 pixels
        _inventoryPanel = new InventoryPanel();
        _inventoryPanel.Position = new Vector2(600, 160);
        _inventoryPanel.Size = new Vector2(160, 128);
        _inventoryPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _inventoryPanel.FocusMode = Control.FocusModeEnum.None;
        _gameUI.AddChild(_inventoryPanel);

        // Old item name label removed — tooltip panel now shows item names directly

        // DyD toggle -- VB6: DyD at (541,338,21,21)
        _dydOffTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_off.jpg"));
        _dydOnTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_on.jpg"));
        _dydToggle = new TextureButton();
        _dydToggle.Position = new Vector2(541, 338);
        _dydToggle.Size = new Vector2(21, 21);
        _dydToggle.StretchMode = TextureButton.StretchModeEnum.Scale;
        _dydToggle.TextureNormal = _dydOffTex;
        _dydToggle.Pressed += () => {
            _soundManager?.PlayNamedSound("click.wav");
            _inventoryPanel!.DyDEnabled = !_inventoryPanel.DyDEnabled;
            _dydToggle.TextureNormal = _inventoryPanel.DyDEnabled ? _dydOnTex : _dydOffTex;
        };
        _gameUI.AddChild(_dydToggle);

        // Spell panel -- VB6: hlst at (8880,2400,2565,2790) = (592,160,171,186)
        _spellPanel = new SpellPanel();
        _spellPanel.Position = new Vector2(592, 160);
        _spellPanel.Size = new Vector2(171, 186);
        _spellPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _spellPanel.FocusMode = Control.FocusModeEnum.None;
        _spellPanel.Visible = false;
        _gameUI.AddChild(_spellPanel);

        // LANZAR button -- VB6 13.3: CmdLanzar at (584, 352, 77, 25)
        _lanzarButton = CreateInvisibleButton(584, 352, 77, 25);
        _lanzarButton.Visible = false;
        _gameUI.AddChild(_lanzarButton);
        _lanzarButton.Pressed += OnLanzarPressed;

        // INFO button -- VB6 13.3: cmdInfo at (712, 352, 57, 27)
        _infoButton = CreateInvisibleButton(712, 352, 57, 27);
        _infoButton.Visible = false;
        _gameUI.AddChild(_infoButton);
        _infoButton.Pressed += () => _spellPanel.InfoSelected();

        // Spell move arrows -- VB6: cmdMoverHechi[0] up at (766,222,15,25), [1] down at (766,247,15,25)
        _spellUpButton = CreateInvisibleButton(766, 222, 15, 25);
        _spellUpButton.Visible = false;
        _gameUI.AddChild(_spellUpButton);
        _spellUpButton.Pressed += () => _spellPanel.MoveSpell(1);

        _spellDownButton = CreateInvisibleButton(766, 247, 15, 25);
        _spellDownButton.Visible = false;
        _gameUI.AddChild(_spellDownButton);
        _spellDownButton.Pressed += () => _spellPanel.MoveSpell(2);

        // === Bottom bar labels -- VB6 13.3 exact positions ===
        _armorLabel = CreateStatLabel(78, 580, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_armorLabel);
        _helmLabel = CreateStatLabel(196, 580, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_helmLabel);
        _shieldLabel = CreateStatLabel(342, 580, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_shieldLabel);
        _weaponLabel = CreateStatLabel(464, 580, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_weaponLabel);
        _fuerzaLabel = CreateStatLabel(648, 415, 14, 14, new Color(0, 1, 0), 9);
        _gameUI.AddChild(_fuerzaLabel);
        _agilidadLabel = CreateStatLabel(608, 415, 14, 14, new Color(1f, 1f, 0f), 9);
        _gameUI.AddChild(_agilidadLabel);
        _repLabel = CreateStatLabel(616, 52, 32, 12, Colors.White, 10, "Cambria", 400);
        _gameUI.AddChild(_repLabel);
        _fpsLabel = CreateStatLabel(440, 6, 37, 12, Colors.White, 7);
        _gameUI.AddChild(_fpsLabel);

        // Macro status indicator
        _macroStatusLabel = new Label();
        _macroStatusLabel.Position = new Vector2(484, 4);
        _macroStatusLabel.Size = new Vector2(60, 12);
        _macroStatusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        _macroStatusLabel.AddThemeFontSizeOverride("font_size", 7);
        ApplyFont(_macroStatusLabel, "Tahoma", 700);
        _macroStatusLabel.Visible = false;
        _macroStatusLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI.AddChild(_macroStatusLabel);

        // Game HUD updater
        _gameUIUpdater = new GameUIUpdater(_state);
        _gameUIUpdater.BindLabels(_expLabel!, _goldLabel!, _levelLabel!, _nameLabel!,
            _onlineLabel!, _coordsLabel!, _armorLabel, _helmLabel, _shieldLabel, _weaponLabel,
            _fuerzaLabel, _agilidadLabel, _repLabel, _fpsLabel, _macroStatusLabel,
            _btnCastiGM, _statBarOverlay);
        _gameUIUpdater.QueueWorldRedraw = () => _worldRenderer?.QueueRedraw();

        // VB6 13.3 sidebar buttons
        SetupSidebarButtons();

        // Create all game panels (commerce, bank, guild, etc.)
        CreateGamePanels();

        // Tooltip, context menu, minimap, quest, trainer, blind overlay
        SetupOverlayPanels();

        // Dialog manager, panel sync, input router
        SetupSubsystems();
    }

    private void SetupSidebarButtons()
    {
        var mapaButton = CreateInvisibleButton(682, 445, 93, 20);
        _gameUI!.AddChild(mapaButton);
        mapaButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); _minimapPanel?.Toggle(); };

        var grupoButton = CreateInvisibleButton(681, 466, 94, 21);
        _gameUI.AddChild(grupoButton);
        grupoButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); _partyPanel?.TogglePanel(); };

        var opcionesButton = CreateInvisibleButton(681, 485, 95, 22);
        _gameUI.AddChild(opcionesButton);
        opcionesButton.Pressed += () =>
        {
            _soundManager?.PlayNamedSound("click.wav");
            if (_optionsPanel != null)
            {
                if (_state.OptionsPanelOpen) _optionsPanel.Close();
                else _optionsPanel.Open();
            }
        };

        var estadisticasButton = CreateInvisibleButton(681, 507, 95, 24);
        _gameUI.AddChild(estadisticasButton);
        estadisticasButton.Pressed += () =>
        {
            _soundManager?.PlayNamedSound("click.wav");
            if (_statsPanel != null)
            {
                if (_state.StatsPanelOpen) _statsPanel.Close();
                else _statsPanel.Open();
            }
        };

        var clanesButton = CreateInvisibleButton(683, 532, 92, 26);
        _gameUI.AddChild(clanesButton);
        clanesButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnClanesButtonPressed(); };

        // Minimize button -- VB6: lblMinimizar at (11280,60) = (752,4) 17x17
        var minimizeButton = CreateInvisibleButton(752, 4, 17, 17);
        _gameUI.AddChild(minimizeButton);
        minimizeButton.Pressed += () => _dialogManager?.OnMinimizePressed();

        // Close/Menu button -- VB6: lblCerrar at (11550,60) = (770,4) 17x17
        var closeMenuButton = CreateInvisibleButton(770, 4, 17, 17);
        _gameUI.AddChild(closeMenuButton);
        closeMenuButton.Pressed += () =>
        {
            if (_state.EscapeMenuOpen)
                _dialogManager?.HideEscapeMenu();
            else
                _dialogManager?.ShowEscapeMenu(GetViewportRect().Size);
        };
    }

    private void CreateGamePanels()
    {
        _commercePanel = new CommercePanel();
        _commercePanel.Position = new Vector2(57, 109);
        _commercePanel.Visible = false;
        _gameUI!.AddChild(_commercePanel);

        _tradePanel = new TradePanel();
        _tradePanel.Position = new Vector2(70, 142);
        _tradePanel.Visible = false;
        _gameUI.AddChild(_tradePanel);

        _bankPanel = new BankPanel();
        _bankPanel.Position = new Vector2(197, 254);
        _bankPanel.Visible = false;
        _bankPanel.OnOpenVault += OnBankOpenVault;
        _gameUI.AddChild(_bankPanel);

        _vaultPanel = new VaultPanel();
        _vaultPanel.Position = new Vector2(55, 88);
        _vaultPanel.Visible = false;
        _gameUI.AddChild(_vaultPanel);

        _guildBankPanel = new GuildBankPanel();
        _guildBankPanel.Position = new Vector2(55, 88);
        _guildBankPanel.Visible = false;
        _gameUI.AddChild(_guildBankPanel);

        _craftPanel = new CraftPanel();
        _craftPanel.Visible = false;
        _gameUI.AddChild(_craftPanel);

        _guildPanel = new GuildPanel();
        _guildPanel.Position = new Vector2(60, 100);
        _guildPanel.Visible = false;
        _gameUI.AddChild(_guildPanel);

        _guildFoundationPanel = new GuildFoundationPanel();
        _guildFoundationPanel.Position = new Vector2(80, 80);
        _guildFoundationPanel.Visible = false;
        _gameUI.AddChild(_guildFoundationPanel);

        _forumPanel = new ForumPanel();
        _forumPanel.Position = new Vector2(20, 80);
        _forumPanel.Visible = false;
        _gameUI.AddChild(_forumPanel);

        _friendListPanel = new FriendListPanel();
        _friendListPanel.Position = new Vector2(240, 60);
        _friendListPanel.Visible = false;
        _gameUI.AddChild(_friendListPanel);

        _mailPanel = new MailPanel();
        _mailPanel.Position = new Vector2(30, 50);
        _mailPanel.Visible = false;
        _gameUI.AddChild(_mailPanel);

        _partyPanel = new PartyPanel();
        _partyPanel.Position = new Vector2(480, 150);
        _partyPanel.Visible = false;
        _gameUI.AddChild(_partyPanel);

        _travelPanel = new TravelPanel();
        _travelPanel.Position = new Vector2(55, 177);
        _travelPanel.Visible = false;
        _gameUI.AddChild(_travelPanel);

        _deathPanel = new DeathPanel();
        _deathPanel.Position = new Vector2(148, 302);
        _deathPanel.Visible = false;
        _gameUI.AddChild(_deathPanel);

        _macroPanel = new MacroPanel();
        _macroPanel.Position = new Vector2(8 + (544 - 280) / 2, 144 + (416 - 380) / 2);
        _macroPanel.Visible = false;
        _gameUI.AddChild(_macroPanel);
        _macroPanel.Init(_state, _dataPath);

        _statsPanel = new StatsPanel();
        _statsPanel.Position = new Vector2(8 + (544 - 380) / 2, 20);
        _statsPanel.Visible = false;
        _gameUI.AddChild(_statsPanel);
        _statsPanel.Init(_state);

        _npcDialogPanel = new NpcDialogPanel();
        _npcDialogPanel.Position = new Vector2(8 + (544 - 300) / 2, 144 + 416 - 120 - 10);
        _npcDialogPanel.Visible = false;
        _gameUI.AddChild(_npcDialogPanel);
        _npcDialogPanel.Init(_state);

        _charInfoPopup = new CharInfoPopup();
        _charInfoPopup.Visible = false;
        _gameUI.AddChild(_charInfoPopup);

        _changePasswordPanel = new ChangePasswordPanel();
        _changePasswordPanel.Visible = false;
        _gameUI.AddChild(_changePasswordPanel);

        _optionsPanel = new OptionsPanel();
        _optionsPanel.Position = new Vector2(8 + (544 - 420) / 2, 20);
        _optionsPanel.Visible = false;
        _gameUI.AddChild(_optionsPanel);
        _optionsPanel.Init(_state, _state.Config, _dataPath);
        _optionsPanel.OnConfigApplied += ApplyConfigToSystems;

        _keyBindPanel = new KeyBindPanel();
        _keyBindPanel.Position = new Vector2(8 + (544 - 420) / 2, 144 + (416 - 500) / 2);
        _keyBindPanel.Visible = false;
        _gameUI.AddChild(_keyBindPanel);
        _keyBindPanel.Init(_state, _state.Keys, _dataPath);

        _optionsPanel.OnOpenKeyBinds += () =>
        {
            _optionsPanel.Close();
            if (_keyBindPanel != null && !_state.KeyBindPanelOpen)
            {
                _state.KeyBindPanelOpen = true;
                _keyBindPanel.Open();
            }
        };

        // GM Panel
        _gmPanel = new GmPanel();
        _gmPanel.Position = new Vector2(8 + (544 - 500) / 2, 30);
        _gmPanel.Visible = false;
        _gameUI.AddChild(_gmPanel);
        _gmPanel.Init(_state, null); // TCP wired later on connect

        // Spawn List Panel
        _spawnListPanel = new SpawnListPanel();
        _spawnListPanel.Position = new Vector2(8 + (544 - 320) / 2, 80);
        _spawnListPanel.Visible = false;
        _gameUI.AddChild(_spawnListPanel);
        _spawnListPanel.Init(_state, null);
        _gmPanel.OnOpenSpawnListRequested += () => _spawnListPanel?.Open();

        // SOS/Help Panel
        _sosPanel = new SosPanel();
        _sosPanel.Position = new Vector2(8 + (544 - 360) / 2, 60);
        _sosPanel.Visible = false;
        _gameUI.AddChild(_sosPanel);
        _sosPanel.Init(_state, null);

        // MOTD Editor Panel
        _motdEditorPanel = new MotdEditorPanel();
        _motdEditorPanel.Position = new Vector2(8 + (544 - 380) / 2, 100);
        _motdEditorPanel.Visible = false;
        _gameUI.AddChild(_motdEditorPanel);
        _motdEditorPanel.Init(_state, null);

        // Guild Alignment Panel
        _guildAlignmentPanel = new GuildAlignmentPanel();
        _guildAlignmentPanel.Position = new Vector2(8 + (544 - 300) / 2, 144 + (416 - 220) / 2);
        _guildAlignmentPanel.Visible = false;
        _gameUI.AddChild(_guildAlignmentPanel);
        _guildAlignmentPanel.Init(_state, null);

        // Peace Proposal Panel
        _peaceProposalPanel = new PeaceProposalPanel();
        _peaceProposalPanel.Position = new Vector2(8 + (544 - 340) / 2, 144 + (416 - 200) / 2);
        _peaceProposalPanel.Visible = false;
        _gameUI.AddChild(_peaceProposalPanel);
        _peaceProposalPanel.Init(_state, null);

        // Guild Member Detail Panel
        _guildMemberPanel = new GuildMemberPanel();
        _guildMemberPanel.Position = new Vector2(8 + (544 - 320) / 2, 100);
        _guildMemberPanel.Visible = false;
        _gameUI.AddChild(_guildMemberPanel);
        _guildMemberPanel.Init(_state, null);

        // Day/Night Cycle
        _dayNightCycle = new Rendering.DayNightCycle();
        _dayNightCycle.Init(_state);
        _dayNightCycle.Enabled = _state.Config.ShowDayNight;
        _gameUI.AddChild(_dayNightCycle);

        // Loading Screen
        _loadingScreen = new LoadingScreen();
        _loadingScreen.Init(_state);
        _gameUI.AddChild(_loadingScreen);

        // Tutorial Panel
        _tutorialPanel = new TutorialPanel();
        _tutorialPanel.Init(_state, _dataPath);
        _tutorialPanel.LoadCompletion();
        _gameUI.AddChild(_tutorialPanel);
    }

    private void SetupOverlayPanels()
    {
        // Tooltip panel
        _tooltipPanel = new TooltipPanel();
        _gameUI!.AddChild(_tooltipPanel);

        // Wire tooltip to all panels with item/spell slots
        _inventoryPanel!.RichTooltip = _tooltipPanel;
        _spellPanel!.RichTooltip = _tooltipPanel;
        _commercePanel!.RichTooltip = _tooltipPanel;
        _vaultPanel!.RichTooltip = _tooltipPanel;
        _tradePanel!.RichTooltip = _tooltipPanel;
        _guildBankPanel!.RichTooltip = _tooltipPanel;

        // Inventory/spell tab UI manager
        _inventoryUI = new UI.InventoryUI(_state);
        _inventoryUI.BindPanels(_inventoryPanel, _spellPanel, null, _dydToggle!,
            _dydOffTex, _dydOnTex, _lanzarButton!, _infoButton!, _spellUpButton!, _spellDownButton!,
            _invEquImage, null, null, _tooltipPanel);
        _inventoryUI.SendPacket = (pkt) => _tcp?.SendPacket(pkt);
        _inventoryUI.OnShowDropDialog = (slot, name) => _dialogManager?.ShowDropDialog(slot, name);
        _inventoryUI.TryTradeOffer = (slot, pos) =>
        {
            if (_tradePanel == null || !_tradePanel.Visible) return false;
            var rect = new Rect2(_tradePanel.GlobalPosition, _tradePanel.Size);
            if (!rect.HasPoint(pos)) return false;
            byte s = (byte)(slot + 1);
            _tcp?.SendPacket(ClientPackets.WriteTradeOfferItem(s, (short)_state.Inventory[slot].Amount));
            return true;
        };
        _inventoryUI.TryVaultDeposit = (slot, pos) =>
        {
            if (_vaultPanel == null || !_vaultPanel.Visible) return false;
            var rect = new Rect2(_vaultPanel.GlobalPosition, _vaultPanel.Size);
            if (!rect.HasPoint(pos)) return false;
            byte s = (byte)(slot + 1);
            _tcp?.SendPacket(ClientPackets.WriteBankDeposit(s, (short)_state.Inventory[slot].Amount));
            return true;
        };
        _inventoryUI.TryGuildBankDeposit = (slot, pos) =>
        {
            if (_guildBankPanel == null || !_guildBankPanel.Visible) return false;
            var rect = new Rect2(_guildBankPanel.GlobalPosition, _guildBankPanel.Size);
            if (!rect.HasPoint(pos)) return false;
            byte s = (byte)(slot + 1);
            _tcp?.SendPacket(ClientPackets.WriteGuildBankDepositItem(s, (short)_state.Inventory[slot].Amount));
            return true;
        };

        // Context menu
        _contextMenu = new ContextMenu();
        _gameUI.AddChild(_contextMenu);

        // Minimap panel
        // Minimap — next to the console (console ends at X=447, minimap at X=450)
        _minimapPanel = new MinimapPanel();
        _minimapPanel.Init(_state, _gameData, System.IO.Path.Combine(_dataPath, "Graficos"));
        _minimapPanel.Position = new Vector2(456, 24);
        _minimapPanel.Size = new Vector2(100, 100);
        _minimapPanel.Visible = _state.Config.ShowMinimap;
        _gameUI.AddChild(_minimapPanel);

        // Quest panel
        _questPanel = new QuestPanel();
        _questPanel.Position = new Vector2(8 + (544 - 560) / 2, 50);
        _questPanel.Visible = false;
        _gameUI.AddChild(_questPanel);

        // Trainer/Pet panel
        _trainerPanel = new TrainerPanel();
        _trainerPanel.Position = new Vector2(150, 80);
        _trainerPanel.Visible = false;
        _gameUI.AddChild(_trainerPanel);

        // Blind screen overlay
        _blindOverlay = new ColorRect();
        _blindOverlay.Color = new Color(0, 0, 0, 0);
        _blindOverlay.Position = Vector2.Zero;
        _blindOverlay.Size = new Vector2(800, 600);
        _blindOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _blindOverlay.Visible = true;
        _blindOverlay.ZIndex = 100;
        _gameUI.AddChild(_blindOverlay);
    }

    private void SetupSubsystems()
    {
        // Dialog manager (window mode, escape menu, mensaje, drop dialog)
        _dialogManager = new UI.DialogManager(_state);
        _dialogManager.CreateWindowModeDialog(GetNode<CanvasLayer>("UILayer"));
        _dialogManager.CreateEscapeMenu(this);
        _dialogManager.CreateMensajeDialog(GetNode<CanvasLayer>("UILayer"));
        _dialogManager.CreateDropDialog(_gameUI!);
        _dialogManager.OnLogout = () =>
        {
            _tcp?.SendPacket(ClientPackets.WriteTalk("/salir"));
            HandleDisconnect("");
        };
        _dialogManager.OnQuit = () => GetTree().Quit();
        _dialogManager.OnWindowModeChosen = (windowed) => OnWindowModeChosen(windowed);
        _dialogManager.OnDropItem = (slot, qty) =>
        {
            _tcp?.SendPacket(ClientPackets.WriteDropItem((byte)(slot + 1), (short)qty));
        };

        // Panel state synchronizer
        _panelSync = new PanelStateSync(_state);
        _panelSync.BindPanels(_commercePanel, _tradePanel, _bankPanel, _vaultPanel,
            _guildBankPanel, _craftPanel, _guildPanel, _guildFoundationPanel,
            _forumPanel, _friendListPanel, _mailPanel, _partyPanel,
            _travelPanel, _questPanel, _trainerPanel, _npcDialogPanel,
            _changePasswordPanel, _charInfoPopup, _deathPanel, _optionsPanel,
            _tooltipPanel, _blindOverlay);
        _panelSync.PlaySound = (id) => _soundManager?.PlaySound(id);
        _panelSync.UpdateDropDialogVisibility = () => _dialogManager?.UpdateDropDialogVisibility();
        _panelSync.BindNewPanels(_gmPanel, _sosPanel, _peaceProposalPanel, _guildAlignmentPanel,
            _motdEditorPanel, _guildMemberPanel, _dayNightCycle, _loadingScreen, _tutorialPanel);

        // Input router
        _inputRouter = new InputRouter(_state);
        _inputRouter.BindPanels(_keyBindPanel, _statsPanel, _optionsPanel, _macroPanel,
            _questPanel, _trainerPanel, _contextMenu, _chatSystem);
        _inputRouter.SendPacket = (pkt) => _tcp?.SendPacket(pkt);
        _inputRouter.CloseDropDialog = () => _dialogManager?.CloseDropDialog();
        _inputRouter.ShowEscapeMenu = () => ShowEscapeMenu();
        _inputRouter.HideEscapeMenu = () => HideEscapeMenu();
        _inputRouter.HandleDisconnect = (msg) => HandleDisconnect(msg);
        _inputRouter.DataPath = _dataPath;
    }
}

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
        // === HUD Frame (replaces Principal.jpg) ===
        var hudFrame = new GameHudFrame();
        hudFrame.ZIndex = 0;
        _gameUI!.AddChild(hudFrame);
        _gameUI.MoveChild(hudFrame, 0);

        // Frame overlay — draws big_bar.png borders on top of all HUD content
        if (hudFrame.FrameOverlay != null)
            _gameUI.AddChild(hudFrame.FrameOverlay);

        // === Inventory & Spells UI (VB6-exact pixel positions) ===

        // Sidebar usable area: x=580→784 (204px). Content width=190px centered.
        int sideX = ResolutionManager.S(577);  // centered start (shifted 10px left)
        int contentW = ResolutionManager.S(190);
        int tabW = contentW / 2; // 95 each

        // Tab buttons — centered in sidebar, with icons (+20% from original, expanded up and to sides)
        int tabH = ResolutionManager.S(34);
        int tabY = ResolutionManager.S(122);
        int tabX = sideX - ResolutionManager.S(6);
        int tabBtnW = (contentW + ResolutionManager.S(12)) / 2;

        _invTabButton = RpgTheme.CreateRpgButtonWithIcon("Inventario", "Inventory.png", false, 10, 16);
        _invTabButton.Position = new Vector2(tabX, tabY);
        _invTabButton.Size = new Vector2(tabBtnW, tabH);
        _gameUI.AddChild(_invTabButton);
        _invTabButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnInventoryTabPressed(); };

        _spellTabButton = RpgTheme.CreateRpgButtonWithIcon("Hechizos", "skills.png", false, 10, 16);
        _spellTabButton.Position = new Vector2(tabX + tabBtnW, tabY);
        _spellTabButton.Size = new Vector2(tabBtnW, tabH);
        _gameUI.AddChild(_spellTabButton);
        _spellTabButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnSpellTabPressed(); };

        // Inventory panel — centered (grid is 5col x 5row = 170px wide, center in 190)
        int invX = sideX + (contentW - ResolutionManager.S(171)) / 2; // ~596
        _inventoryPanel = new InventoryPanel();
        _inventoryPanel.Position = new Vector2(invX, ResolutionManager.S(158));
        _inventoryPanel.Size = new Vector2(ResolutionManager.S(171), ResolutionManager.S(174));
        _inventoryPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _inventoryPanel.FocusMode = Control.FocusModeEnum.None;
        _gameUI.AddChild(_inventoryPanel);

        // DyD toggle
        _dydOffTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_off.jpg"));
        _dydOnTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_on.jpg"));
        _dydToggle = new TextureButton();
        _dydToggle.Position = new Vector2(sideX - ResolutionManager.S(25), ResolutionManager.S(338));
        _dydToggle.Size = new Vector2(ResolutionManager.S(21), ResolutionManager.S(21));
        _dydToggle.StretchMode = TextureButton.StretchModeEnum.Scale;
        _dydToggle.TextureNormal = _dydOffTex;
        _dydToggle.MouseDefaultCursorShape = CursorShape.PointingHand;
        _dydToggle.Pressed += () => {
            _soundManager?.PlayNamedSound("click.wav");
            _inventoryPanel!.DyDEnabled = !_inventoryPanel.DyDEnabled;
            _dydToggle.TextureNormal = _inventoryPanel.DyDEnabled ? _dydOnTex : _dydOffTex;
        };
        _gameUI.AddChild(_dydToggle);

        // Spell panel — same width as inventory, centered
        _spellPanel = new SpellPanel();
        _spellPanel.Position = new Vector2(sideX, ResolutionManager.S(158));
        _spellPanel.Size = new Vector2(contentW, ResolutionManager.S(186));
        _spellPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _spellPanel.FocusMode = Control.FocusModeEnum.None;
        _spellPanel.Visible = false;
        _gameUI.AddChild(_spellPanel);

        // LANZAR + INFO — span full content width, centered
        int halfBtn = contentW / 2 - ResolutionManager.S(2); // 93 each with 4px gap
        _lanzarButton = RpgTheme.CreateRpgButton("Lanzar", false, 12);
        _lanzarButton.Position = new Vector2(sideX, ResolutionManager.S(348));
        _lanzarButton.Size = new Vector2(halfBtn, ResolutionManager.S(28));
        _lanzarButton.Visible = false;
        _gameUI.AddChild(_lanzarButton);
        _lanzarButton.Pressed += OnLanzarPressed;

        _infoButton = RpgTheme.CreateRpgButton("Info", false, 12);
        _infoButton.Position = new Vector2(sideX + halfBtn + ResolutionManager.S(4), ResolutionManager.S(348));
        _infoButton.Size = new Vector2(halfBtn, ResolutionManager.S(28));
        _infoButton.Visible = false;
        _gameUI.AddChild(_infoButton);
        _infoButton.Pressed += () => _spellPanel.InfoSelected();

        // Spell move arrows — right edge of content
        _spellUpButton = RpgTheme.CreateMiniButton("Mini_arrow_top2.png", "Mini_arrow_top2_t.png", new Vector2(ResolutionManager.S(15), ResolutionManager.S(25)));
        _spellUpButton.Position = new Vector2(sideX + contentW + ResolutionManager.S(2), ResolutionManager.S(200));
        _spellUpButton.Visible = false;
        _gameUI.AddChild(_spellUpButton);
        _spellUpButton.Pressed += () => _spellPanel.MoveSpell(1);

        _spellDownButton = RpgTheme.CreateMiniButton("Mini_arrow_bot2.png", "Mini_arrow_bot2_t.png", new Vector2(ResolutionManager.S(15), ResolutionManager.S(25)));
        _spellDownButton.Position = new Vector2(sideX + contentW + ResolutionManager.S(2), ResolutionManager.S(230));
        _spellDownButton.Visible = false;
        _gameUI.AddChild(_spellDownButton);
        _spellDownButton.Pressed += () => _spellPanel.MoveSpell(2);

        // === Bottom bar labels ===
        _armorLabel = CreateStatLabel(ResolutionManager.S(55), ResolutionManager.S(574), ResolutionManager.S(100), ResolutionManager.S(17), Colors.White, 7);
        _gameUI.AddChild(_armorLabel);
        _helmLabel = CreateStatLabel(ResolutionManager.S(170), ResolutionManager.S(574), ResolutionManager.S(90), ResolutionManager.S(17), Colors.White, 7);
        _gameUI.AddChild(_helmLabel);
        _shieldLabel = CreateStatLabel(ResolutionManager.S(310), ResolutionManager.S(574), ResolutionManager.S(95), ResolutionManager.S(17), Colors.White, 7);
        _gameUI.AddChild(_shieldLabel);
        _weaponLabel = CreateStatLabel(ResolutionManager.S(435), ResolutionManager.S(574), ResolutionManager.S(90), ResolutionManager.S(17), Colors.White, 7);
        _gameUI.AddChild(_weaponLabel);
        _agilidadLabel = CreateStatLabel(ResolutionManager.S(580), ResolutionManager.S(405), ResolutionManager.S(55), ResolutionManager.S(14), new Color(1f, 1f, 0f), 9);
        _gameUI.AddChild(_agilidadLabel);
        var statSepLabel = CreateStatLabel(ResolutionManager.S(635), ResolutionManager.S(405), ResolutionManager.S(10), ResolutionManager.S(14), new Color(0.5f, 0.5f, 0.5f), 9);
        statSepLabel.Text = "|";
        statSepLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _gameUI.AddChild(statSepLabel);
        _fuerzaLabel = CreateStatLabel(ResolutionManager.S(645), ResolutionManager.S(405), ResolutionManager.S(50), ResolutionManager.S(14), new Color(0, 1, 0), 9);
        _gameUI.AddChild(_fuerzaLabel);
        // Reputation label removed (system disabled)
        _fpsLabel = CreateStatLabel(ResolutionManager.S(682), ResolutionManager.S(565), ResolutionManager.S(93), ResolutionManager.S(12), Colors.White, 7);
        _gameUI.AddChild(_fpsLabel);

        // Macro status indicator
        _macroStatusLabel = new Label();
        _macroStatusLabel.Position = new Vector2(ResolutionManager.S(484), ResolutionManager.S(4));
        _macroStatusLabel.Size = new Vector2(ResolutionManager.S(60), ResolutionManager.S(12));
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
            _fuerzaLabel, _agilidadLabel, null, _fpsLabel, _macroStatusLabel,
            null, _statBarOverlay);
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
        var mapaButton = RpgTheme.CreateRpgButton("Mapa", false, 10);
        mapaButton.Position = new Vector2(ResolutionManager.S(682), ResolutionManager.S(445));
        mapaButton.Size = new Vector2(ResolutionManager.S(93), ResolutionManager.S(20));
        _gameUI!.AddChild(mapaButton);
        mapaButton.Pressed += () =>
        {
            _soundManager?.PlayNamedSound("click.wav");
            _minimapPanel?.Toggle();
            UpdateConsoleWidth();
        };

        var grupoButton = RpgTheme.CreateRpgButton("Grupo", false, 10);
        grupoButton.Position = new Vector2(ResolutionManager.S(682), ResolutionManager.S(466));
        grupoButton.Size = new Vector2(ResolutionManager.S(93), ResolutionManager.S(20));
        _gameUI.AddChild(grupoButton);
        grupoButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); _partyPanel?.TogglePanel(); };

        var opcionesButton = RpgTheme.CreateRpgButton("Opciones", false, 10);
        opcionesButton.Position = new Vector2(ResolutionManager.S(682), ResolutionManager.S(487));
        opcionesButton.Size = new Vector2(ResolutionManager.S(93), ResolutionManager.S(20));
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

        var estadisticasButton = RpgTheme.CreateRpgButton("Stats", false, 10);
        estadisticasButton.Position = new Vector2(ResolutionManager.S(682), ResolutionManager.S(508));
        estadisticasButton.Size = new Vector2(ResolutionManager.S(93), ResolutionManager.S(20));
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

        var clanesButton = RpgTheme.CreateRpgButton("Clanes", false, 10);
        clanesButton.Position = new Vector2(ResolutionManager.S(682), ResolutionManager.S(529));
        clanesButton.Size = new Vector2(ResolutionManager.S(93), ResolutionManager.S(20));
        _gameUI.AddChild(clanesButton);
        clanesButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnClanesButtonPressed(); };

        // Minimize button (arrow down = minimize) — above frame overlay
        var minimizeButton = RpgTheme.CreateMiniButton("Mini_arrow_bot.png", "Mini_arrow_bot.png", new Vector2(ResolutionManager.S(17), ResolutionManager.S(17)));
        minimizeButton.Position = new Vector2(ResolutionManager.S(752), ResolutionManager.S(4));
        minimizeButton.ZIndex = 51;
        _gameUI.AddChild(minimizeButton);
        minimizeButton.Pressed += () => _dialogManager?.OnMinimizePressed();

        // Close/Menu button — above frame overlay
        var closeMenuButton = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(ResolutionManager.S(17), ResolutionManager.S(17)));
        closeMenuButton.Position = new Vector2(ResolutionManager.S(770), ResolutionManager.S(4));
        closeMenuButton.ZIndex = 51;
        _gameUI.AddChild(closeMenuButton);
        closeMenuButton.Pressed += () =>
        {
            if (_state.EscapeMenuOpen)
                _dialogManager?.HideEscapeMenu();
            else
                _dialogManager?.ShowEscapeMenu(GetViewportRect().Size);
        };
    }

    /// <summary>
    /// Resize console + chat input based on minimap visibility.
    /// Minimap visible: console/input shrink to leave room for minimap.
    /// Minimap hidden: console/input expand to fill full width.
    /// </summary>
    private void UpdateConsoleWidth()
    {
        if (_consoleLabel == null) return;
        bool minimapVisible = _minimapPanel != null && _minimapPanel.Visible;
        float right = minimapVisible ? ResolutionManager.Sf(423) : ResolutionManager.Sf(547);
        _consoleLabel.OffsetRight = right;
        if (_chatInputNode != null)
            _chatInputNode.OffsetRight = right;
        if (_minimapBorder != null)
            _minimapBorder.Visible = minimapVisible;
    }

    /// <summary>Add a panel to _gameUI with standard defaults (hidden, positioned, above minimap).</summary>
    private T AddPanel<T>(Vector2? position = null) where T : Control, new()
    {
        var panel = new T();
        if (position.HasValue) panel.Position = position.Value;
        panel.Visible = false;
        panel.ZIndex = RpgBaseForm.ZPanel;
        _gameUI!.AddChild(panel);
        return panel;
    }

    private void CreateGamePanels()
    {
        _commercePanel = AddPanel<CommercePanel>(new Vector2(ResolutionManager.S(57), ResolutionManager.S(109)));
        _tradePanel = AddPanel<TradePanel>(new Vector2(ResolutionManager.S(70), ResolutionManager.S(142)));

        _bankPanel = AddPanel<BankPanel>(new Vector2(ResolutionManager.S(197), ResolutionManager.S(254)));
        _bankPanel.OnOpenVault += OnBankOpenVault;

        _vaultPanel = AddPanel<VaultPanel>(new Vector2(ResolutionManager.S(55), ResolutionManager.S(88)));
        _guildBankPanel = AddPanel<GuildBankPanel>(new Vector2(ResolutionManager.S(55), ResolutionManager.S(88)));
        _craftPanel = AddPanel<CraftPanel>();
        _guildPanel = AddPanel<GuildPanel>(new Vector2(ResolutionManager.S(60), ResolutionManager.S(100)));
        _guildFoundationPanel = AddPanel<GuildFoundationPanel>(new Vector2(ResolutionManager.S(80), ResolutionManager.S(80)));
        _forumPanel = AddPanel<ForumPanel>(new Vector2(ResolutionManager.S(20), ResolutionManager.S(80)));
        _partyPanel = AddPanel<PartyPanel>(new Vector2(ResolutionManager.S(480), ResolutionManager.S(150)));
        _travelPanel = AddPanel<TravelPanel>(new Vector2(ResolutionManager.S(55), ResolutionManager.S(177)));
        _deathPanel = AddPanel<DeathPanel>(new Vector2(ResolutionManager.S(148), ResolutionManager.S(302)));
        _charInfoPopup = AddPanel<CharInfoPopup>();
        _changePasswordPanel = AddPanel<ChangePasswordPanel>();

        _macroPanel = AddPanel<MacroPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(280)) / 2, ResolutionManager.S(144) + (ResolutionManager.S(416) - ResolutionManager.S(380)) / 2));
        _macroPanel.Init(_state, _dataPath);

        _statsPanel = AddPanel<StatsPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(380)) / 2, ResolutionManager.S(20)));
        _statsPanel.Init(_state);

        _npcDialogPanel = AddPanel<NpcDialogPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(300)) / 2, ResolutionManager.S(144) + ResolutionManager.S(416) - ResolutionManager.S(120) - ResolutionManager.S(10)));
        _npcDialogPanel.Init(_state);

        _optionsPanel = AddPanel<OptionsPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(420)) / 2, ResolutionManager.S(20)));
        _optionsPanel.Init(_state, _state.Config, _dataPath);
        _optionsPanel.OnConfigApplied += ApplyConfigToSystems;

        _keyBindPanel = AddPanel<KeyBindPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(420)) / 2, ResolutionManager.S(144) + (ResolutionManager.S(416) - ResolutionManager.S(500)) / 2));
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

        _gmPanel = AddPanel<GmPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(500)) / 2, ResolutionManager.S(30)));
        _gmPanel.Init(_state, null);

        _spawnListPanel = AddPanel<SpawnListPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(320)) / 2, ResolutionManager.S(80)));
        _spawnListPanel.Init(_state, null);
        _gmPanel.OnOpenSpawnListRequested += () => _spawnListPanel?.Open();

        _sosPanel = AddPanel<SosPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(360)) / 2, ResolutionManager.S(60)));
        _sosPanel.Init(_state, null);

        _motdEditorPanel = AddPanel<MotdEditorPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(380)) / 2, ResolutionManager.S(100)));
        _motdEditorPanel.Init(_state, null);

        _guildAlignmentPanel = AddPanel<GuildAlignmentPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(300)) / 2, ResolutionManager.S(144) + (ResolutionManager.S(416) - ResolutionManager.S(220)) / 2));
        _guildAlignmentPanel.Init(_state, null);

        _peaceProposalPanel = AddPanel<PeaceProposalPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(340)) / 2, ResolutionManager.S(144) + (ResolutionManager.S(416) - ResolutionManager.S(200)) / 2));
        _peaceProposalPanel.Init(_state, null);

        _guildMemberPanel = AddPanel<GuildMemberPanel>(new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(320)) / 2, ResolutionManager.S(100)));
        _guildMemberPanel.Init(_state, null);

        // Day/Night Cycle
        _dayNightCycle = new Rendering.DayNightCycle();
        _dayNightCycle.Init(_state);
        _dayNightCycle.Enabled = _state.Config.ShowDayNight;
        _gameUI!.AddChild(_dayNightCycle);

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
        _tooltipPanel.ZIndex = RpgBaseForm.ZTooltip;
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
            null, null, null, _tooltipPanel);
        _inventoryUI.BindTabButtons(_invTabButton!, _spellTabButton!);
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
        _contextMenu.ZIndex = RpgBaseForm.ZContextMenu;
        _gameUI.AddChild(_contextMenu);

        // Minimap panel with styled border
        var minimapBorder = new Panel();
        minimapBorder.Position = new Vector2(ResolutionManager.S(429), ResolutionManager.S(19));
        minimapBorder.Size = new Vector2(ResolutionManager.S(118), ResolutionManager.S(118));
        minimapBorder.MouseFilter = Control.MouseFilterEnum.Ignore;
        var mmStyle = new StyleBoxFlat();
        mmStyle.BgColor = new Color(0f, 0f, 0f, 0.55f);
        mmStyle.BorderColor = new Color(0.4f, 0.33f, 0.2f, 0.6f);
        mmStyle.SetBorderWidthAll(1);
        mmStyle.SetCornerRadiusAll(2);
        minimapBorder.AddThemeStyleboxOverride("panel", mmStyle);
        _gameUI.AddChild(minimapBorder);

        _minimapPanel = new MinimapPanel();
        _minimapPanel.Init(_state, _gameData, System.IO.Path.Combine(_dataPath, "Graficos"));
        _minimapPanel.Position = new Vector2(ResolutionManager.S(438), ResolutionManager.S(28));
        _minimapPanel.Size = new Vector2(ResolutionManager.S(100), ResolutionManager.S(100));
        _minimapPanel.Visible = _state.Config.ShowMinimap;
        _gameUI.AddChild(_minimapPanel);
        minimapBorder.Visible = _state.Config.ShowMinimap;

        // Store border ref for toggling with minimap
        _minimapBorder = minimapBorder;
        UpdateConsoleWidth();

        // Quest panel
        _questPanel = new QuestPanel();
        _questPanel.Position = new Vector2(ResolutionManager.S(8) + (ResolutionManager.S(544) - ResolutionManager.S(560)) / 2, ResolutionManager.S(50));
        _questPanel.Visible = false;
        _questPanel.ZIndex = RpgBaseForm.ZPanel;
        _gameUI.AddChild(_questPanel);

        // Trainer/Pet panel
        _trainerPanel = new TrainerPanel();
        _trainerPanel.Position = new Vector2(ResolutionManager.S(150), ResolutionManager.S(80));
        _trainerPanel.Visible = false;
        _trainerPanel.ZIndex = RpgBaseForm.ZPanel;
        _gameUI.AddChild(_trainerPanel);

        // Blind screen overlay
        _blindOverlay = new ColorRect();
        _blindOverlay.Color = new Color(0, 0, 0, 0);
        _blindOverlay.Position = Vector2.Zero;
        _blindOverlay.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        _blindOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _blindOverlay.Visible = true;
        _blindOverlay.ZIndex = RpgBaseForm.ZBlind;
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
        _dialogManager.OnOptions = () => _optionsPanel?.Open();
        _dialogManager.OnRestoreFullscreen = () => EnterFullscreen();
        _dialogManager.OnWindowModeChosen = (windowed) => OnWindowModeChosen(windowed);
        _dialogManager.OnDropItem = (slot, qty) =>
        {
            _tcp?.SendPacket(ClientPackets.WriteDropItem((byte)(slot + 1), (short)qty));
        };

        // Panel state synchronizer
        _panelSync = new PanelStateSync(_state);
        _panelSync.BindPanels(_commercePanel, _tradePanel, _bankPanel, _vaultPanel,
            _guildBankPanel, _craftPanel, _guildPanel, _guildFoundationPanel,
            _forumPanel, _partyPanel,
            _travelPanel, _questPanel, _trainerPanel, _npcDialogPanel,
            _changePasswordPanel, _charInfoPopup, _deathPanel, _optionsPanel,
            _tooltipPanel, _blindOverlay);
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
        _inputRouter.OnEnterFullscreen = () => EnterFullscreen();
        _inputRouter.OnExitFullscreen = () => ExitFullscreen();
    }
}

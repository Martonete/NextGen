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
        int S(int v) => ResolutionManager.S(v);

        // === HUD Frame (replaces Principal.jpg) ===
        _hudFrame = new GameHudFrame();
        _hudFrame.ZIndex = 0;
        _gameUI!.AddChild(_hudFrame);
        var hudFrame = _hudFrame;
        _gameUI.MoveChild(hudFrame, 0);

        // Frame overlay — draws big_bar.png borders on top of all HUD content
        if (hudFrame.FrameOverlay != null)
            _gameUI.AddChild(hudFrame.FrameOverlay);

        // === Inventory & Spells UI (VB6-exact pixel positions, scaled) ===

        // Sidebar usable area: design offset at base res, center extra space at higher res
        int contentW = S(190);
        int sidebarRealW = ResolutionManager.WindowWidth - ResolutionManager.SidebarX;
        int designSidebarW = S(240);
        int designOffset = S(10);
        int extraSpace = Math.Max(0, sidebarRealW - designSidebarW);
        int sideX = ResolutionManager.SidebarX + designOffset + extraSpace / 2;

        // Tab buttons — centered in sidebar, with icons (+20% from original, expanded up and to sides)
        int tabH = S(34);
        int tabY = S(122);
        int tabX = sideX - S(6);
        int tabBtnW = (contentW + S(12)) / 2;

        _invTabButton = RpgTheme.CreateRpgButtonWithIcon("Inventario", "Inventory.png", false, S(10), S(16));
        _invTabButton.Position = new Vector2(tabX, tabY);
        _invTabButton.Size = new Vector2(tabBtnW, tabH);
        _gameUI.AddChild(_invTabButton);
        _invTabButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnInventoryTabPressed(); };

        _spellTabButton = RpgTheme.CreateRpgButtonWithIcon("Hechizos", "skills.png", false, S(10), S(16));
        _spellTabButton.Position = new Vector2(tabX + tabBtnW, tabY);
        _spellTabButton.Size = new Vector2(tabBtnW, tabH);
        _gameUI.AddChild(_spellTabButton);
        _spellTabButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnSpellTabPressed(); };

        // Inventory panel — centered (grid is 5col x 5row = 170px wide, center in 190)
        int invX = sideX + (contentW - S(171)) / 2;
        _inventoryPanel = new InventoryPanel();
        _inventoryPanel.Position = new Vector2(invX, S(158));
        _inventoryPanel.Size = new Vector2(171, 174);
        _inventoryPanel.Scale = new Vector2(ResolutionManager.UIScale, ResolutionManager.UIScale);
        _inventoryPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _inventoryPanel.FocusMode = Control.FocusModeEnum.None;
        _gameUI.AddChild(_inventoryPanel);

        // DyD toggle
        _dydOffTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_off.jpg"));
        _dydOnTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_on.jpg"));
        _dydToggle = new TextureButton();
        _dydToggle.Position = new Vector2(sideX - S(25), S(338));
        _dydToggle.Size = new Vector2(S(21), S(21));
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
        _spellPanel.Position = new Vector2(sideX, S(158));
        _spellPanel.Size = new Vector2(190, 186);
        _spellPanel.Scale = new Vector2(ResolutionManager.UIScale, ResolutionManager.UIScale);
        _spellPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _spellPanel.FocusMode = Control.FocusModeEnum.None;
        _spellPanel.Visible = false;
        _gameUI.AddChild(_spellPanel);

        // LANZAR + INFO — span full content width, centered
        int halfBtn = contentW / 2 - S(2); // 93 each with 4px gap (scaled)
        _lanzarButton = RpgTheme.CreateRpgButton("Lanzar", false, S(12));
        _lanzarButton.Position = new Vector2(sideX, S(348));
        _lanzarButton.Size = new Vector2(halfBtn, S(28));
        _lanzarButton.Visible = false;
        _gameUI.AddChild(_lanzarButton);
        _lanzarButton.Pressed += OnLanzarPressed;

        _infoButton = RpgTheme.CreateRpgButton("Info", false, S(12));
        _infoButton.Position = new Vector2(sideX + halfBtn + S(4), S(348));
        _infoButton.Size = new Vector2(halfBtn, S(28));
        _infoButton.Visible = false;
        _gameUI.AddChild(_infoButton);
        _infoButton.Pressed += () => _spellPanel.InfoSelected();

        // Spell move arrows — right edge of content
        _spellUpButton = RpgTheme.CreateMiniButton("Mini_arrow_top2.png", "Mini_arrow_top2_t.png", new Vector2(S(15), S(25)));
        _spellUpButton.Position = new Vector2(sideX + contentW + S(2), S(200));
        _spellUpButton.Visible = false;
        _gameUI.AddChild(_spellUpButton);
        _spellUpButton.Pressed += () => _spellPanel.MoveSpell(1);

        _spellDownButton = RpgTheme.CreateMiniButton("Mini_arrow_bot2.png", "Mini_arrow_bot2_t.png", new Vector2(S(15), S(25)));
        _spellDownButton.Position = new Vector2(sideX + contentW + S(2), S(230));
        _spellDownButton.Visible = false;
        _gameUI.AddChild(_spellDownButton);
        _spellDownButton.Pressed += () => _spellPanel.MoveSpell(2);

        // === Bottom bar labels (dynamic Y from ResolutionManager) ===
        int bbY = ResolutionManager.BottomBarY + S(9);  // 565+9=574 at 800x600
        _armorLabel = CreateStatLabel(S(55), bbY, S(100), S(17), Colors.White, S(7));
        _gameUI.AddChild(_armorLabel);
        _helmLabel = CreateStatLabel(S(170), bbY, S(90), S(17), Colors.White, S(7));
        _gameUI.AddChild(_helmLabel);
        _shieldLabel = CreateStatLabel(S(310), bbY, S(95), S(17), Colors.White, S(7));
        _gameUI.AddChild(_shieldLabel);
        _weaponLabel = CreateStatLabel(S(435), bbY, S(90), S(17), Colors.White, S(7));
        _gameUI.AddChild(_weaponLabel);
        // Agilidad | Fuerza — centered as a single row in the sidebar
        int statRowW = S(190);
        int statRowX = sideX;
        int statRowY = ResolutionManager.BottomBarY - S(160);
        _agilidadLabel = CreateStatLabel(statRowX, statRowY, statRowW / 2 - S(5), S(14), new Color(1f, 1f, 0f), S(9));
        _agilidadLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _gameUI.AddChild(_agilidadLabel);
        var statSepLabel = CreateStatLabel(statRowX + statRowW / 2 - S(5), statRowY, S(10), S(14), new Color(0.5f, 0.5f, 0.5f), S(9));
        statSepLabel.Text = "|";
        statSepLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _gameUI.AddChild(statSepLabel);
        _statSepLabel = statSepLabel;
        _fuerzaLabel = CreateStatLabel(statRowX + statRowW / 2 + S(5), statRowY, statRowW / 2 - S(5), S(14), new Color(0, 1, 0), S(9));
        _fuerzaLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _gameUI.AddChild(_fuerzaLabel);
        // Reputation label removed (system disabled)
        int fpsW = S(210);
        _fpsLabel = CreateStatLabel(sideX, ResolutionManager.BottomBarY, fpsW, S(12), Colors.White, S(7));
        _fpsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _gameUI.AddChild(_fpsLabel);

        // Macro status indicator
        _macroStatusLabel = new Label();
        _macroStatusLabel.Position = new Vector2(S(484), S(4));
        _macroStatusLabel.Size = new Vector2(S(60), S(12));
        _macroStatusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        _macroStatusLabel.AddThemeFontSizeOverride("font_size", S(7));
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
        int S(int v) => ResolutionManager.S(v);
        int btnX = ResolutionManager.SidebarX + S(122); // 560+122=682 at 800x600

        // Sidebar buttons positioned relative to BottomBarY
        // At 800x600: BottomBarY=565, buttons start at 445 = BottomBarY - 120
        int btn0Y = ResolutionManager.BottomBarY - S(120);
        int btnStep = S(21);

        var mapaButton = RpgTheme.CreateRpgButton("Mapa", false, S(10));
        mapaButton.Position = new Vector2(btnX, btn0Y);
        mapaButton.Size = new Vector2(S(93), S(20));
        _gameUI!.AddChild(mapaButton);
        mapaButton.Pressed += () =>
        {
            _soundManager?.PlayNamedSound("click.wav");
            _minimapPanel?.Toggle();
            UpdateConsoleWidth();
        };
        _mapaButton = mapaButton as TextureButton;

        var grupoButton = RpgTheme.CreateRpgButton("Grupo", false, S(10));
        grupoButton.Position = new Vector2(btnX, btn0Y + btnStep);
        grupoButton.Size = new Vector2(S(93), S(20));
        _gameUI.AddChild(grupoButton);
        grupoButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); _partyPanel?.TogglePanel(); };
        _grupoButton = grupoButton as TextureButton;

        var opcionesButton = RpgTheme.CreateRpgButton("Opciones", false, S(10));
        opcionesButton.Position = new Vector2(btnX, btn0Y + btnStep * 2);
        opcionesButton.Size = new Vector2(S(93), S(20));
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
        _opcionesButton = opcionesButton as TextureButton;

        var estadisticasButton = RpgTheme.CreateRpgButton("Stats", false, S(10));
        estadisticasButton.Position = new Vector2(btnX, btn0Y + btnStep * 3);
        estadisticasButton.Size = new Vector2(S(93), S(20));
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
        _estadisticasButton = estadisticasButton as TextureButton;

        var clanesButton = RpgTheme.CreateRpgButton("Clanes", false, S(10));
        clanesButton.Position = new Vector2(btnX, btn0Y + btnStep * 4);
        clanesButton.Size = new Vector2(S(93), S(20));
        _gameUI.AddChild(clanesButton);
        clanesButton.Pressed += () => { _soundManager?.PlayNamedSound("click.wav"); OnClanesButtonPressed(); };
        _clanesButton = clanesButton as TextureButton;

        // Minimize button (arrow down = minimize) — above frame overlay
        var minimizeButton = RpgTheme.CreateMiniButton("Mini_arrow_bot.png", "Mini_arrow_bot.png", new Vector2(S(17), S(17)));
        minimizeButton.Position = new Vector2(ResolutionManager.WindowWidth - S(48), S(4));
        minimizeButton.ZIndex = 51;
        _gameUI.AddChild(minimizeButton);
        minimizeButton.Pressed += () => _dialogManager?.OnMinimizePressed();
        _minimizeButton = minimizeButton;

        // Close/Menu button — above frame overlay
        var closeMenuButton = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(S(17), S(17)));
        closeMenuButton.Position = new Vector2(ResolutionManager.WindowWidth - S(30), S(4));
        closeMenuButton.ZIndex = 51;
        _gameUI.AddChild(closeMenuButton);
        closeMenuButton.Pressed += () =>
        {
            if (_state.EscapeMenuOpen)
                _dialogManager?.HideEscapeMenu();
            else
                _dialogManager?.ShowEscapeMenu(GetViewportRect().Size);
        };
        _closeMenuButton = closeMenuButton;
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
        // Console right edge: full width when minimap hidden, shrink when visible
        float fullRight = ResolutionManager.ConsoleRight;
        float right = minimapVisible ? fullRight - ResolutionManager.S(124) : fullRight;
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
        _commercePanel = AddPanel<CommercePanel>(new Vector2(57, 109));
        _tradePanel = AddPanel<TradePanel>(new Vector2(70, 142));

        _bankPanel = AddPanel<BankPanel>(new Vector2(197, 254));
        _bankPanel.OnOpenVault += OnBankOpenVault;

        _vaultPanel = AddPanel<VaultPanel>(new Vector2(55, 88));
        _guildBankPanel = AddPanel<GuildBankPanel>(new Vector2(55, 88));
        _craftPanel = AddPanel<CraftPanel>();
        _guildPanel = AddPanel<GuildPanel>(new Vector2(60, 100));
        _guildFoundationPanel = AddPanel<GuildFoundationPanel>(new Vector2(80, 80));
        _forumPanel = AddPanel<ForumPanel>(new Vector2(20, 80));
        _partyPanel = AddPanel<PartyPanel>(new Vector2(480, 150));
        _travelPanel = AddPanel<TravelPanel>(new Vector2(55, 177));
        _deathPanel = AddPanel<DeathPanel>(new Vector2(148, 302));
        _charInfoPopup = AddPanel<CharInfoPopup>();
        _changePasswordPanel = AddPanel<ChangePasswordPanel>();

        int vpW = ResolutionManager.ViewportW;
        int vpH = ResolutionManager.ViewportH;
        int vpLeft = ResolutionManager.LeftMargin;
        int vpTop = ResolutionManager.TopMargin;

        _macroPanel = AddPanel<MacroPanel>(new Vector2(vpLeft + (vpW - 280) / 2, vpTop + (vpH - 380) / 2));
        _macroPanel.Init(_state, _dataPath);

        _statsPanel = AddPanel<StatsPanel>(new Vector2(vpLeft + (vpW - 380) / 2, 20));
        _statsPanel.Init(_state);

        _npcDialogPanel = AddPanel<NpcDialogPanel>(new Vector2(vpLeft + (vpW - 300) / 2, vpTop + vpH - 120 - 10));
        _npcDialogPanel.Init(_state);

        _optionsPanel = AddPanel<OptionsPanel>(new Vector2(vpLeft + (vpW - 420) / 2, 20));
        _optionsPanel.Init(_state, _state.Config, _dataPath);
        _optionsPanel.OnConfigApplied += ApplyConfigToSystems;

        _keyBindPanel = AddPanel<KeyBindPanel>(new Vector2(vpLeft + (vpW - 420) / 2, vpTop + (vpH - 500) / 2));
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

        _gmPanel = AddPanel<GmPanel>(new Vector2(vpLeft + (vpW - 500) / 2, 30));
        _gmPanel.Init(_state, null);

        _spawnListPanel = AddPanel<SpawnListPanel>(new Vector2(vpLeft + (vpW - 320) / 2, 80));
        _spawnListPanel.Init(_state, null);
        _gmPanel.OnOpenSpawnListRequested += () => _spawnListPanel?.Open();

        _sosPanel = AddPanel<SosPanel>(new Vector2(vpLeft + (vpW - 360) / 2, 60));
        _sosPanel.Init(_state, null);

        _motdEditorPanel = AddPanel<MotdEditorPanel>(new Vector2(vpLeft + (vpW - 380) / 2, 100));
        _motdEditorPanel.Init(_state, null);

        _guildAlignmentPanel = AddPanel<GuildAlignmentPanel>(new Vector2(vpLeft + (vpW - 300) / 2, vpTop + (vpH - 220) / 2));
        _guildAlignmentPanel.Init(_state, null);

        _peaceProposalPanel = AddPanel<PeaceProposalPanel>(new Vector2(vpLeft + (vpW - 340) / 2, vpTop + (vpH - 200) / 2));
        _peaceProposalPanel.Init(_state, null);

        _guildMemberPanel = AddPanel<GuildMemberPanel>(new Vector2(vpLeft + (vpW - 320) / 2, 100));
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
        int S(int v) => ResolutionManager.S(v);

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

        // Minimap panel with styled border — inside console area, top-right corner
        int mmBorderX = ResolutionManager.ConsoleRight - S(120);
        var minimapBorder = new Panel();
        minimapBorder.Position = new Vector2(mmBorderX, S(19));
        minimapBorder.Size = new Vector2(S(118), S(118));
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
        _minimapPanel.Position = new Vector2(mmBorderX + S(9), S(28));
        _minimapPanel.Size = new Vector2(S(100), S(100));
        _minimapPanel.Visible = _state.Config.ShowMinimap;
        _gameUI.AddChild(_minimapPanel);
        minimapBorder.Visible = _state.Config.ShowMinimap;

        // Store border ref for toggling with minimap
        _minimapBorder = minimapBorder;
        UpdateConsoleWidth();

        // Quest panel
        _questPanel = new QuestPanel();
        _questPanel.Position = new Vector2(ResolutionManager.LeftMargin + (ResolutionManager.ViewportW - 560) / 2, S(50));
        _questPanel.Visible = false;
        _questPanel.ZIndex = RpgBaseForm.ZPanel;
        _gameUI.AddChild(_questPanel);

        // Trainer/Pet panel
        _trainerPanel = new TrainerPanel();
        _trainerPanel.Position = new Vector2(S(150), S(80));
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

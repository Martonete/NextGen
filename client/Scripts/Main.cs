using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;

namespace ArgentumNextgen;

public partial class Main : Control
{
	private const string ServerHost = "127.0.0.1";
	private const int ServerPort = 5028;

	// Saved references for runtime repositioning on resolution change
	private SubViewportContainer? _viewportContainer;
	private SubViewport? _gameViewport;
	private Control? _sidebarBg;
	private GameHudFrame? _hudFrame;

	// Saved references for runtime repositioning of sidebar elements
	private NinePatchRect? _nameFrame;
	private NinePatchRect? _levelFrame;
	private Panel? _levelBadge;
	private NinePatchRect? _xpBarBg;
	private TextureRect? _goldIcon;
	// Sidebar buttons saved for repositioning
	private TextureButton? _mapaButton;
	private TextureButton? _grupoButton;
	private TextureButton? _opcionesButton;
	private TextureButton? _estadisticasButton;
	private TextureButton? _clanesButton;
	private TextureButton? _minimizeButton;
	private TextureButton? _closeMenuButton;
	private Label? _statSepLabel;

	/// <summary>
	/// Reposition ALL UI elements to match current ResolutionManager values.
	/// Called on resolution change without reloading the scene.
	/// </summary>
	private void RepositionUI()
	{
		int S(int v) => ResolutionManager.S(v);
		int sbW = S(210);
		// Extra space = real sidebar minus design sidebar (scaled).
		// At 800x600: extra=0. At higher res: extra > 0 → distribute evenly.
		int sidebarRealW = ResolutionManager.WindowWidth - ResolutionManager.SidebarX;
		int designSidebarW = S(240); // DesignSidebarWidth scaled
		int extraSidebar = Math.Max(0, sidebarRealW - designSidebarW);
		int sbX = ResolutionManager.SidebarX + extraSidebar / 2;

		// Root + GameUI
		Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
		if (_gameUI != null) _gameUI.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);

		// SubViewportContainer
		if (_viewportContainer != null)
		{
			_viewportContainer.OffsetLeft = ResolutionManager.LeftMargin;
			_viewportContainer.OffsetTop = ResolutionManager.TopMargin;
			_viewportContainer.OffsetRight = ResolutionManager.LeftMargin + ResolutionManager.ViewportW;
			_viewportContainer.OffsetBottom = ResolutionManager.TopMargin + ResolutionManager.ViewportH;
		}

		// SubViewport
		if (_gameViewport != null)
			_gameViewport.Size = new Vector2I(ResolutionManager.ViewportW, ResolutionManager.ViewportH);

		// Sidebar background
		if (_sidebarBg != null)
			_sidebarBg.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);

		// HUD frame (big_bar_bg + big_bar_frame)
		if (_hudFrame != null)
			_hudFrame.ResizeToWindow();

		// Stat bar overlay
		if (_statBarOverlay != null)
			_statBarOverlay.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);

		// --- Sidebar internal elements ---
		// Name frame
		if (_nameFrame != null) { _nameFrame.Position = new Vector2(sbX, S(17)); _nameFrame.Size = new Vector2(sbW, S(42)); }
		// NameLabel
		if (_nameLabel != null) { _nameLabel.Position = new Vector2(sbX + S(14), S(19)); _nameLabel.Size = new Vector2(sbW - S(28), S(38)); _nameLabel.AddThemeFontSizeOverride("font_size", S(11)); }
		// Level frame
		if (_levelFrame != null) { _levelFrame.Position = new Vector2(sbX + S(2), S(55)); _levelFrame.Size = new Vector2(sbW - S(4), S(20)); }
		// Level badge
		if (_levelBadge != null) { _levelBadge.Position = new Vector2(sbX + S(6), S(57)); _levelBadge.Size = new Vector2(S(30), S(16)); }
		// LevelLabel
		if (_levelLabel != null) { _levelLabel.Position = new Vector2(sbX + S(6), S(57)); _levelLabel.Size = new Vector2(S(30), S(16)); _levelLabel.AddThemeFontSizeOverride("font_size", S(9)); }
		// XP bar
		if (_xpBarBg != null) { _xpBarBg.Position = new Vector2(sbX + S(2), S(78)); _xpBarBg.Size = new Vector2(sbW - S(4), S(14)); }
		// ExpLabel
		if (_expLabel != null) { _expLabel.Position = new Vector2(sbX + S(2), S(78)); _expLabel.Size = new Vector2(sbW - S(4), S(14)); _expLabel.AddThemeFontSizeOverride("font_size", S(8)); }
		// Gold icon + label
		if (_goldIcon != null) { _goldIcon.Position = new Vector2(sbX + S(140), ResolutionManager.BottomBarY - S(137)); _goldIcon.Size = new Vector2(S(14), S(14)); }
		if (_goldLabel != null) { _goldLabel.Position = new Vector2(sbX + S(155), ResolutionManager.BottomBarY - S(135)); _goldLabel.Size = new Vector2(S(60), S(14)); _goldLabel.AddThemeFontSizeOverride("font_size", S(8)); }
		// --- Sidebar buttons (relative to BottomBarY) ---
		int btnX = sbX + S(122);
		int btn0Y = ResolutionManager.BottomBarY - S(120);
		int btnStep = S(21);
		int btnFontSize = S(10);
		int btnW = S(93);

		// Coords label (map name + coords) — centered below stat bars (left half of sidebar)
		int coordsW = S(120);
		if (_coordsLabel != null) { _coordsLabel.Position = new Vector2(sbX, S(549)); _coordsLabel.Size = new Vector2(coordsW, S(28)); _coordsLabel.HorizontalAlignment = HorizontalAlignment.Center; _coordsLabel.AddThemeFontSizeOverride("font_size", S(8)); }
		// Online + FPS — centered below Clanes button
		int clanesBottomY = btn0Y + btnStep * 4 + S(22);
		if (_onlineLabel != null) { _onlineLabel.Position = new Vector2(btnX, clanesBottomY); _onlineLabel.Size = new Vector2(btnW, S(12)); _onlineLabel.HorizontalAlignment = HorizontalAlignment.Center; _onlineLabel.AddThemeFontSizeOverride("font_size", S(7)); }
		if (_fpsLabel != null) { _fpsLabel.Position = new Vector2(btnX, clanesBottomY + S(12)); _fpsLabel.Size = new Vector2(btnW, S(12)); _fpsLabel.HorizontalAlignment = HorizontalAlignment.Center; _fpsLabel.AddThemeFontSizeOverride("font_size", S(7)); }

		if (_mapaButton != null) { _mapaButton.Position = new Vector2(btnX, btn0Y); _mapaButton.Size = new Vector2(S(93), S(20)); _mapaButton.AddThemeFontSizeOverride("font_size", btnFontSize); }
		if (_grupoButton != null) { _grupoButton.Position = new Vector2(btnX, btn0Y + btnStep); _grupoButton.Size = new Vector2(S(93), S(20)); _grupoButton.AddThemeFontSizeOverride("font_size", btnFontSize); }
		if (_opcionesButton != null) { _opcionesButton.Position = new Vector2(btnX, btn0Y + btnStep * 2); _opcionesButton.Size = new Vector2(S(93), S(20)); _opcionesButton.AddThemeFontSizeOverride("font_size", btnFontSize); }
		if (_estadisticasButton != null) { _estadisticasButton.Position = new Vector2(btnX, btn0Y + btnStep * 3); _estadisticasButton.Size = new Vector2(S(93), S(20)); _estadisticasButton.AddThemeFontSizeOverride("font_size", btnFontSize); }
		if (_clanesButton != null) { _clanesButton.Position = new Vector2(btnX, btn0Y + btnStep * 4); _clanesButton.Size = new Vector2(S(93), S(20)); _clanesButton.AddThemeFontSizeOverride("font_size", btnFontSize); }

		// Minimize/Close buttons
		if (_minimizeButton != null) { _minimizeButton.Position = new Vector2(ResolutionManager.WindowWidth - S(48), S(4)); _minimizeButton.Size = new Vector2(S(17), S(17)); }
		if (_closeMenuButton != null) { _closeMenuButton.Position = new Vector2(ResolutionManager.WindowWidth - S(30), S(4)); _closeMenuButton.Size = new Vector2(S(17), S(17)); }

		// --- Inventory/Spell panel area ---
		int contentW = S(190);
		int designOffset = S(10);
		int sideX = ResolutionManager.SidebarX + designOffset + extraSidebar / 2;
		int tabX = sideX - S(6);
		int tabBtnW = (contentW + S(12)) / 2;
		if (_invTabButton != null) { _invTabButton.Position = new Vector2(tabX, S(122)); _invTabButton.Size = new Vector2(tabBtnW, S(34)); _invTabButton.AddThemeFontSizeOverride("font_size", S(10)); }
		if (_spellTabButton != null) { _spellTabButton.Position = new Vector2(tabX + tabBtnW, S(122)); _spellTabButton.Size = new Vector2(tabBtnW, S(34)); _spellTabButton.AddThemeFontSizeOverride("font_size", S(10)); }
		int invX = sideX + (contentW - S(171)) / 2;
		float uiScale = ResolutionManager.UIScale;
		if (_inventoryPanel != null) { _inventoryPanel.Position = new Vector2(invX, S(158)); _inventoryPanel.Size = new Vector2(171, 174); _inventoryPanel.Scale = new Vector2(uiScale, uiScale); }
		if (_spellPanel != null) { _spellPanel.Position = new Vector2(sideX, S(158)); _spellPanel.Size = new Vector2(190, 186); _spellPanel.Scale = new Vector2(uiScale, uiScale); }
		if (_dydToggle != null) { _dydToggle.Position = new Vector2(sideX - S(25), S(338)); _dydToggle.Size = new Vector2(S(21), S(21)); }
		int halfBtn = contentW / 2 - S(2);
		if (_lanzarButton != null) { _lanzarButton.Position = new Vector2(sideX, S(348)); _lanzarButton.Size = new Vector2(halfBtn, S(28)); _lanzarButton.AddThemeFontSizeOverride("font_size", S(10)); }
		if (_infoButton != null) { _infoButton.Position = new Vector2(sideX + halfBtn + S(4), S(348)); _infoButton.Size = new Vector2(halfBtn, S(28)); _infoButton.AddThemeFontSizeOverride("font_size", S(10)); }
		if (_spellUpButton != null) _spellUpButton.Position = new Vector2(sideX + contentW + S(2), S(200));
		if (_spellDownButton != null) _spellDownButton.Position = new Vector2(sideX + contentW + S(2), S(230));

		// --- Stat labels ---
		int bbY = ResolutionManager.BottomBarY + S(9);
		if (_armorLabel != null) { _armorLabel.Position = new Vector2(S(55), bbY); _armorLabel.Size = new Vector2(S(100), S(17)); _armorLabel.AddThemeFontSizeOverride("font_size", S(7)); }
		if (_helmLabel != null) { _helmLabel.Position = new Vector2(S(170), bbY); _helmLabel.Size = new Vector2(S(90), S(17)); _helmLabel.AddThemeFontSizeOverride("font_size", S(7)); }
		if (_shieldLabel != null) { _shieldLabel.Position = new Vector2(S(310), bbY); _shieldLabel.Size = new Vector2(S(95), S(17)); _shieldLabel.AddThemeFontSizeOverride("font_size", S(7)); }
		if (_weaponLabel != null) { _weaponLabel.Position = new Vector2(S(435), bbY); _weaponLabel.Size = new Vector2(S(90), S(17)); _weaponLabel.AddThemeFontSizeOverride("font_size", S(7)); }
		// Agilidad | Fuerza — centered row
		int statRowW = S(190);
		int statRowY = ResolutionManager.BottomBarY - S(160);
		if (_agilidadLabel != null) { _agilidadLabel.Position = new Vector2(sideX, statRowY); _agilidadLabel.Size = new Vector2(statRowW / 2 - S(5), S(14)); _agilidadLabel.HorizontalAlignment = HorizontalAlignment.Right; _agilidadLabel.AddThemeFontSizeOverride("font_size", S(9)); }
		if (_statSepLabel != null) { _statSepLabel.Position = new Vector2(sideX + statRowW / 2 - S(5), statRowY); _statSepLabel.Size = new Vector2(S(10), S(14)); }
		if (_fuerzaLabel != null) { _fuerzaLabel.Position = new Vector2(sideX + statRowW / 2 + S(5), statRowY); _fuerzaLabel.Size = new Vector2(statRowW / 2 - S(5), S(14)); _fuerzaLabel.HorizontalAlignment = HorizontalAlignment.Left; _fuerzaLabel.AddThemeFontSizeOverride("font_size", S(9)); }
		// --- Minimap (inside console area, top-right corner) ---
		// Position: right-aligned to ConsoleRight, with S(5) gap from console text
		int mmBorderW = S(118);
		int mmBorderX = ResolutionManager.ConsoleRight - mmBorderW;
		if (_minimapBorder != null) { _minimapBorder.Position = new Vector2(mmBorderX, S(19)); _minimapBorder.Size = new Vector2(mmBorderW, S(118)); }
		if (_minimapPanel != null) { _minimapPanel.Position = new Vector2(mmBorderX + S(9), S(28)); _minimapPanel.Size = new Vector2(S(100), S(100)); }

		// Macro status label
		if (_macroStatusLabel != null) { _macroStatusLabel.Position = new Vector2(S(484), S(4)); _macroStatusLabel.Size = new Vector2(S(60), S(12)); _macroStatusLabel.AddThemeFontSizeOverride("font_size", S(7)); }

		// Blind overlay
		if (_blindOverlay != null)
			_blindOverlay.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);

		// Console + ChatInput offsets (scaled from design values)
		if (_consoleLabel != null)
		{
			_consoleLabel.OffsetLeft = S(27);
			_consoleLabel.OffsetTop = S(28);
			_consoleLabel.OffsetBottom = S(115);
		}
		if (_chatInputNode != null)
		{
			_chatInputNode.OffsetLeft = S(22);
			_chatInputNode.OffsetTop = S(118);
			_chatInputNode.OffsetBottom = S(139);
		}
		// Right edge depends on minimap visibility
		UpdateConsoleWidth();

		// Re-center all game panels in the viewport area
		CenterPanelsInViewport();

		// Fog overlay needs redraw
		_worldRenderer?.QueueRedraw();
	}

	/// <summary>
	/// Re-center all game panels within the current viewport area.
	/// </summary>
	private void CenterPanelsInViewport()
	{
		int vpL = ResolutionManager.LeftMargin;
		int vpT = ResolutionManager.TopMargin;
		int vpW = ResolutionManager.ViewportW;
		int vpH = ResolutionManager.ViewportH;
		float uiScale = ResolutionManager.UIScale;

		void Center(Control? panel)
		{
			if (panel == null) return;
			// Scale panel proportionally, but cap so it fits inside the viewport
			float maxScaleW = (float)vpW / panel.Size.X;
			float maxScaleH = (float)vpH / panel.Size.Y;
			float panelScale = Math.Min(uiScale, Math.Min(maxScaleW * 0.95f, maxScaleH * 0.95f));
			panel.Scale = new Vector2(panelScale, panelScale);
			// Center using scaled size
			float scaledW = panel.Size.X * panelScale;
			float scaledH = panel.Size.Y * panelScale;
			float px = vpL + (vpW - scaledW) / 2f;
			float py = vpT + (vpH - scaledH) / 2f;
			panel.Position = new Vector2(Math.Max(vpL, px), Math.Max(vpT, py));
		}

		Center(_commercePanel);
		Center(_tradePanel);
		Center(_bankPanel);
		Center(_vaultPanel);
		Center(_guildBankPanel);
		Center(_craftPanel);
		Center(_guildPanel);
		Center(_guildFoundationPanel);
		Center(_forumPanel);
		Center(_partyPanel);
		Center(_travelPanel);
		Center(_deathPanel);
		Center(_macroPanel);
		Center(_statsPanel);
		Center(_optionsPanel);
		Center(_keyBindPanel);
		Center(_questPanel);
		Center(_trainerPanel);
		Center(_changePasswordPanel);
		Center(_gmPanel);
		Center(_spawnListPanel);
		Center(_sosPanel);
		Center(_motdEditorPanel);
		Center(_guildAlignmentPanel);
		Center(_peaceProposalPanel);
		Center(_guildMemberPanel);
		Center(_tutorialPanel);
	}

	private readonly GameData _gameData = new();
	private readonly GameState _state = new();
	private AoTcpClient? _tcp;
	private PacketHandler? _packetHandler;
	private InputHandler? _inputHandler;
	private WorldRenderer? _worldRenderer;
	private GrhAnimator _animator = new();
	private ParticleSystem _particleSystem = new();
	private LightSystem _lightSystem = new();
	private SoundManager? _soundManager;

	// Borderless window drag state
	private bool _windowDragging;
	private Vector2I _windowDragStart;

	private bool _connecting;


	// Login form (programmatic — replaces scene-based LoginPanel)
	private UI.LoginForm? _loginForm;

	// Character select form (programmatic — replaces scene-based CharSelectPanel)
	private UI.CharSelectForm? _charSelectForm;

	// Chat system (extracted to ChatSystem.cs)
	private ChatSystem? _chatSystem;

	// Inventory/spell tab UI (extracted to InventoryUI.cs)
	private UI.InventoryUI? _inventoryUI;

	// Game HUD updater (extracted to GameUIUpdater.cs)
	private GameUIUpdater? _gameUIUpdater;

	// Panel state synchronizer (extracted to PanelStateSync.cs)
	private PanelStateSync? _panelSync;

	// Input router (extracted to InputRouter.cs)
	private InputRouter? _inputRouter;

	// Game UI nodes
	private Control? _gameUI;
	private Label? _goldLabel;
	private Label? _levelLabel;
	private Label? _nameLabel;
	private Label? _onlineLabel;
	private Label? _coordsLabel;
	private Label? _expLabel;

	// Bottom bar combat/attribute labels (VB6: lblArmor, lblHelm, lblShielder, lblWeapon)
	private Label? _armorLabel;    // Body armor def
	private Label? _helmLabel;     // Helmet def
	private Label? _shieldLabel;   // Shield def
	private Label? _weaponLabel;   // Weapon hit
	private Label? _fuerzaLabel;
	private Label? _agilidadLabel;
	private Label? _fpsLabel;
	private Label? _macroStatusLabel; // Shows "MACRO" when work/spell macro is active

	private string _dataPath = ""; // cached for macro file I/O
	private IResourceProvider? _resources;

	// Custom stat bar overlay (draws colored fill rects at VB6 positions)
	private StatBarOverlay? _statBarOverlay;


	// Inventory & Spells UI (VB6-accurate positions)
	private InventoryPanel? _inventoryPanel;
	private SpellPanel? _spellPanel;
	private TextureButton? _invTabButton;
	private TextureButton? _spellTabButton;
	private TextureButton? _dydToggle;
	private Texture2D? _dydOffTex;
	private Texture2D? _dydOnTex;
	private TextureButton? _lanzarButton;
	private TextureButton? _infoButton;
	private TextureButton? _spellUpButton;
	private TextureButton? _spellDownButton;
	// Commerce panel (frmComerciar)
	private CommercePanel? _commercePanel;

	// Trade panel (frmComerciarUsu — player-to-player)
	private TradePanel? _tradePanel;

	// Bank panels (frmBanco + frmNuevoBancoObj)
	private BankPanel? _bankPanel;
	private VaultPanel? _vaultPanel;
	private GuildBankPanel? _guildBankPanel;
	private CraftPanel? _craftPanel;

	// Guild panels
	private GuildPanel? _guildPanel;
	private GuildFoundationPanel? _guildFoundationPanel;

	// Forum panel (frmForo)
	private ForumPanel? _forumPanel;

	// Party panel (frmGrupo)
	private PartyPanel? _partyPanel;

	// Travel panel (frmViajar)
	private TravelPanel? _travelPanel;

	// Death panel (frmMuertito)
	private DeathPanel? _deathPanel;

	// Macro panel (frmMakro)
	private MacroPanel? _macroPanel;

	// Stats panel (frmEstadisticas)
	private StatsPanel? _statsPanel;

	// Options panel (frmOpcionesNew)
	private OptionsPanel? _optionsPanel;

	// Key binding panel (frmTeclas)
	private KeyBindPanel? _keyBindPanel;

	// Quest panel (frmQuest)
	private QuestPanel? _questPanel;

	// Trainer/Pet panel (frmEntrenador)
	private TrainerPanel? _trainerPanel;

	// NPC dialog panel (shows NPC speech in fixed bottom-center panel)
	private NpcDialogPanel? _npcDialogPanel;

	// Change password panel
	private ChangePasswordPanel? _changePasswordPanel;

	// Character info popup (shows /MIRAR results centered on screen)
	private CharInfoPopup? _charInfoPopup;

	// Minimap panel (shows player/NPC dots on 100x100 tile grid)
	private MinimapPanel? _minimapPanel;
	private Panel? _minimapBorder;

	// Console (chat output) + chat input — stored for resizing when minimap toggles
	private RichTextLabel? _consoleLabel;
	private LineEdit? _chatInputNode;

	// Blind screen overlay (VB6: frmMain goes black when blinded)
	private ColorRect? _blindOverlay;

	// Tooltip panel (floating item/spell info on hover)
	private TooltipPanel? _tooltipPanel;

	// Context menu (right-click on characters in game world)
	private ContextMenu? _contextMenu;

	// GM Panel (frmPanelGM)
	private GmPanel? _gmPanel;

	// Spawn List (frmSpawnList)
	private SpawnListPanel? _spawnListPanel;

	// SOS/Help panel (GM help requests)
	private SosPanel? _sosPanel;

	// MOTD Editor (guild message of the day)
	private MotdEditorPanel? _motdEditorPanel;

	// Guild alignment picker (during foundation)
	private GuildAlignmentPanel? _guildAlignmentPanel;

	// Peace proposal panel (guild diplomacy)
	private PeaceProposalPanel? _peaceProposalPanel;

	// Guild member detail panel
	private GuildMemberPanel? _guildMemberPanel;

	// Day/Night cycle overlay
	private Rendering.DayNightCycle? _dayNightCycle;

	// Loading screen
	private LoadingScreen? _loadingScreen;

	// Tutorial panel
	private TutorialPanel? _tutorialPanel;

	// Dialog manager (extracted to DialogManager.cs)
	private UI.DialogManager? _dialogManager;

	// Account creation screen (extracted to AccountCreateScreen.cs)
	private UI.AccountCreateScreen? _accountCreateScreen;

	// Character creation screen (extracted to CharCreateScreen.cs)
	private UI.CharCreateScreen? _charCreateScreen;

	// Track screen transitions
	private Screen _lastScreen = Screen.Login;

	// Startup preload system
	private LoadingScreen? _startupLoadingScreen;
	private IEnumerator<int>? _texturePreloadIter;
	private bool _startupPreloadDone;

	/// <summary>
	/// Creates a fresh PacketHandler and wires all callbacks.
	/// Called exactly once — at login time, after _tcp is assigned.
	/// </summary>
	private void CreatePacketHandler()
	{
		_packetHandler = new PacketHandler(_state);
		_packetHandler.OnMapLoad = () => { _soundManager?.StopAllSfx(); LoadCurrentMap(); };
		if (_soundManager != null)
		{
			_packetHandler.OnPlaySound = (id) => _soundManager.PlaySound(id);
			_packetHandler.OnPlaySoundAt = (id, x, y) => _soundManager.PlaySoundAt(id, x, y, _state.UserPosX, _state.UserPosY);
			_packetHandler.OnPlayMusic = (id) => _soundManager.PlayMusic(id);
			_packetHandler.OnStopSfx = () => _soundManager?.StopAllSfx();
		}
		_packetHandler.OnFloatingText = (charIndex, text, colorHex) =>
		{
			var floatingLayer = _worldRenderer?.FloatingText;
			if (floatingLayer == null) return;
			var color = Color.FromHtml(colorHex);
			floatingLayer.AddText(charIndex, text, color);
		};
	}

	public override void _Ready()
	{
		GD.Print("=== Argentum Nextgen — Godot 4 Client ===");

		string dataPath;
		if (OS.HasFeature("editor"))
		{
			dataPath = ResolveEditorDataPath();
		}
		else
		{
			string exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
			dataPath = System.IO.Path.Combine(exeDir, "Data");

			// Load patch PCK files (overrides base resources).
			// Drop a "patch.pck" next to the exe for delta updates.
			LoadPatchPacks(exeDir);
		}

		GD.Print($"[MAIN] Data path: {dataPath}");
		_dataPath = dataPath;
		_resources = ResourceProviderFactory.Create(dataPath);
		_gameData.LoadAll(_resources);
		_state.TextMessages = _gameData.TextMessages;

		// Load particle definitions
		_particleSystem.LoadDefinitions(_resources, "INIT/Particles.ini", _state);

		// Load user configuration (Options.ao)
		_state.Config = GameConfig.Load(dataPath);
		_state.ShowNames = _state.Config.ShowNames;

		// Apply V-Sync
		DisplayServer.WindowSetVsyncMode(
			_state.Config.VsyncEnabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		if (_state.Config.FpsLimit > 0)
			Engine.MaxFps = _state.Config.FpsLimit;

		// Apply dynamic resolution (window size + tile calculation)
		ResolutionManager.ApplyResolution(_state.Config.ResolutionWidth, _state.Config.ResolutionHeight);

		// Load key bindings (Teclas.ao)
		_state.Keys = KeyBindings.Load(dataPath);

		if (!_gameData.IsLoaded)
		{
			GD.PrintErr("[MAIN] Failed to load game data — aborting");
			return;
		}

		// Setup renderer inside the SubViewport
		_worldRenderer = new WorldRenderer();
		_worldRenderer.Init(_state, _gameData, _animator);
		var gameWorldNode = GetNode<Node2D>("GameUI/GameViewportContainer/GameViewport/GameWorld");
		gameWorldNode.AddChild(_worldRenderer);

		// Save references for runtime repositioning
		_gameViewport = GetNode<SubViewport>("GameUI/GameViewportContainer/GameViewport");
		// Pixel snap is set via project settings (rendering/2d/snap/snap_2d_transforms_to_pixel)
		_viewportContainer = GetNode<SubViewportContainer>("GameUI/GameViewportContainer");

		// Wire resolution change: reposition all UI in-place (no scene reload)
		// In fullscreen, update ContentScaleSize so the new layout fills the screen.
		ResolutionManager.OnResolutionChanged += () =>
		{
			if (_state.Config.Fullscreen)
				EnterFullscreen();
			else
				ExitFullscreen();
			// Defer repositioning to next frame so window resize has taken effect
			Callable.From(RepositionUI).CallDeferred();
		};

		// Apply initial SubViewport size
		_gameViewport.Size = new Vector2I(ResolutionManager.ViewportW, ResolutionManager.ViewportH);

		// Setup sound manager
		_soundManager = new SoundManager();
		AddChild(_soundManager);
		_soundManager.Init(dataPath, _resources);
		_soundManager.MusicEnabled = _state.Config.MusicEnabled;
		_soundManager.SoundEnabled = _state.Config.SfxEnabled;
		_soundManager.SetMusicVolume(_state.Config.MusicVolume);
		_soundManager.SetSfxVolume(_state.Config.SfxVolume);

		// Initialize weather renderer with sound manager (rain sound)
		_worldRenderer?.InitWeather(_soundManager, _resources);

		// Set Linear texture filtering on UI layers so fonts/text scale smoothly.
		// Game viewport keeps Nearest (pixel art) via project default_texture_filter=0.
		// UILayer is a CanvasLayer (not CanvasItem), so set filter on its children.
		foreach (var child in GetNode("UILayer").GetChildren())
			if (child is CanvasItem ci) ci.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		GetNode<Control>("GameUI").TextureFilter = CanvasItem.TextureFilterEnum.Linear;

		// Grab Login UI nodes
		// Login form (programmatic — replaces scene LoginPanel)
		_loginForm = new UI.LoginForm(_state, _dataPath);
		_loginForm.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		GetNode("UILayer").AddChild(_loginForm);
		_loginForm.OnLoginRequest = (account, password) =>
		{
			_tcp = new AoTcpClient();
			CreatePacketHandler();
			_inputHandler = new InputHandler(_tcp, _state, _state.Keys, GetViewport());
			if (_inputRouter != null) _inputRouter.InputHandler = _inputHandler;
			_inputHandler.OnPlaySoundAt = (id, x, y) =>
				_soundManager?.PlaySoundAt(id, x, y, _state.UserPosX, _state.UserPosY);
			_inputHandler.OnToggleMusic = () =>
			{
				_state.Config.MusicEnabled = !_state.Config.MusicEnabled;
				if (_soundManager != null)
				{
					_soundManager.MusicEnabled = _state.Config.MusicEnabled;
					if (_soundManager.MusicEnabled && _state.MusicId > 0)
						_soundManager.PlayMusic(_state.MusicId);
				}
				_state.Config.Save(_dataPath);
			};
			_loginForm!.Connecting = true;
			_connecting = true;
			_ = ConnectAndLogin(account, password);
		};
		_loginForm.OnCreateAccountPressed = OnCrearCuentaPressed;

		// Character select form (programmatic — replaces scene CharSelectPanel)
		_charSelectForm = new UI.CharSelectForm();
		_charSelectForm.Init(_state, _gameData);
		_charSelectForm.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		GetNode("UILayer").AddChild(_charSelectForm);
		_charSelectForm.OnEnterPressed = OnEnterPressed;
		_charSelectForm.OnDisconnect = () => HandleDisconnect("");

		// Grab Game UI nodes
		_gameUI = GetNode<Control>("GameUI");

		// === Apply dynamic resolution to viewport/UI ===
		Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
		_gameUI.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);

		var viewportContainer = GetNode<SubViewportContainer>("GameUI/GameViewportContainer");
		// MouseFilter.Pass so buttons on top of the viewport area can receive clicks.
		// Game viewport clicks are handled via _Input → HandleMouseClick, not GUI system.
		viewportContainer.MouseFilter = Control.MouseFilterEnum.Pass;
		viewportContainer.OffsetLeft = ResolutionManager.LeftMargin;
		viewportContainer.OffsetTop = ResolutionManager.TopMargin;
		viewportContainer.OffsetRight = ResolutionManager.LeftMargin + ResolutionManager.ViewportW;
		viewportContainer.OffsetBottom = ResolutionManager.TopMargin + ResolutionManager.ViewportH;

		// Chat system
		var console = GetNode<RichTextLabel>("GameUI/Console");
		_consoleLabel = console;
		// Set scaled offsets (design: left=27, top=28, bottom=115, right=ConsoleRight)
		console.OffsetLeft = S(27);
		console.OffsetTop = S(28);
		console.OffsetBottom = S(115);
		console.OffsetRight = ResolutionManager.ConsoleRight;
		var consoleStyle = new StyleBoxEmpty();
		consoleStyle.ContentMarginLeft = 2;
		consoleStyle.ContentMarginRight = 2;
		consoleStyle.ContentMarginTop = 1;
		consoleStyle.ContentMarginBottom = 6;
		console.AddThemeStyleboxOverride("normal", consoleStyle);

		var chatInput = GetNode<LineEdit>("GameUI/ChatInput");
		_chatInputNode = chatInput;
		// Set scaled offsets (design: left=22, top=118, bottom=139, right=ConsoleRight)
		chatInput.OffsetLeft = S(22);
		chatInput.OffsetTop = S(118);
		chatInput.OffsetBottom = S(139);
		chatInput.OffsetRight = ResolutionManager.ConsoleRight;
		chatInput.Visible = false;
		chatInput.MaxLength = 160;
		// RPG-styled input for chat
		var chatNormal = new StyleBoxFlat();
		chatNormal.BgColor = new Color(0.10f, 0.08f, 0.06f, 0.85f);
		chatNormal.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.9f);
		chatNormal.SetBorderWidthAll(2);
		chatNormal.SetCornerRadiusAll(2);
		chatNormal.ContentMarginLeft = 6; chatNormal.ContentMarginRight = 6;
		chatNormal.ContentMarginTop = 2;  chatNormal.ContentMarginBottom = 2;
		chatInput.AddThemeStyleboxOverride("normal", chatNormal);
		var chatFocus = (StyleBoxFlat)chatNormal.Duplicate();
		chatFocus.BorderColor = new Color(0.65f, 0.55f, 0.35f, 1f);
		chatInput.AddThemeStyleboxOverride("focus", chatFocus);
		chatInput.AddThemeStyleboxOverride("read_only", (StyleBoxFlat)chatNormal.Duplicate());
		chatInput.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		chatInput.AddThemeColorOverride("caret_color", new Color(0.9f, 0.85f, 0.7f));

		_chatSystem = new ChatSystem(_state);
		_chatSystem.BindNodes(console, chatInput);
		_chatSystem.ExpandConsole(); // Start expanded (input hidden)
		_chatSystem.SendPacket = (pkt) => _tcp?.SendPacket(pkt);
		_chatSystem.OnWorkMacroToggle = () => _inventoryUI?.HandleWorkMacroToggle();
		_chatSystem.OnSpellMacroToggle = () => _inventoryUI?.HandleSpellMacroToggle();
		_chatSystem.OnPasswdCommand = () => _state.ShowChangePassword = true;
		_chatSystem.OnGmPanelToggle = () => _gmPanel?.Toggle();
		_chatSystem.OnSosPanelToggle = () => _sosPanel?.Open();
		_goldLabel = GetNode<Label>("GameUI/GoldLabel");
		_levelLabel = GetNode<Label>("GameUI/LevelLabel");
		_nameLabel = GetNode<Label>("GameUI/NameLabel");
		_onlineLabel = GetNode<Label>("GameUI/OnlineLabel");
		_coordsLabel = GetNode<Label>("GameUI/CoordsLabel");
		_expLabel = GetNode<Label>("GameUI/ExpLabel");

		// === Redesigned top sidebar: Name frame + Level strip + XP bar ===
		// Background layer inserted after HudFrame so frames draw behind labels
		_sidebarBg = new Control();
		_sidebarBg.MouseFilter = Control.MouseFilterEnum.Ignore;
		_sidebarBg.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
		var sidebarBg = _sidebarBg;
		_gameUI.AddChild(sidebarBg);
		_gameUI.MoveChild(sidebarBg, 1); // index 1 = after HudFrame(0), before scene labels

		// Sidebar content area: design position at base res, center extra at higher res
		int S(int v) => ResolutionManager.S(v);
		int sbW = S(210);
		int sidebarRealW2 = ResolutionManager.WindowWidth - ResolutionManager.SidebarX;
		int designSbW2 = S(240);
		int extraSb2 = Math.Max(0, sidebarRealW2 - designSbW2);
		int sbX = ResolutionManager.SidebarX + extraSb2 / 2;

		// --- Name frame: name_frame_mid_ready.png NinePatch ---
		var nameFrame = RpgTheme.CreateNinePatch("name_frame_mid_ready.png", new Vector4(30, 10, 30, 10));
		nameFrame.Position = new Vector2(sbX, S(17));
		nameFrame.Size = new Vector2(sbW, S(42));
		sidebarBg.AddChild(nameFrame);
		_nameFrame = nameFrame;

		// NameLabel: centered with padding inside frame
		_nameLabel.Position = new Vector2(sbX + S(14), S(19));
		_nameLabel.Size = new Vector2(sbW - S(28), S(38));
		_nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_nameLabel.VerticalAlignment = VerticalAlignment.Center;
		_nameLabel.ClipText = true;
		ApplyFont(_nameLabel, "Tahoma", 700);
		_nameLabel.AddThemeFontSizeOverride("font_size", S(11));
		_nameLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
		_nameLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
		_nameLabel.AddThemeConstantOverride("shadow_offset_x", 1);
		_nameLabel.AddThemeConstantOverride("shadow_offset_y", 1);
		_nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

		// --- Level + Rep strip: info_window.png NinePatch ---
		var levelFrame = RpgTheme.CreateNinePatch("info_window.png", new Vector4(12, 12, 12, 12));
		levelFrame.Position = new Vector2(sbX + S(2), S(55));
		levelFrame.Size = new Vector2(sbW - S(4), S(20));
		sidebarBg.AddChild(levelFrame);
		_levelFrame = levelFrame;

		// Level badge: small rounded dark box with gold border
		var levelBadge = new Panel();
		levelBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
		var badgeStyle = new StyleBoxFlat();
		badgeStyle.BgColor = new Color(0.06f, 0.05f, 0.04f, 0.95f);
		badgeStyle.BorderColor = new Color(0.72f, 0.56f, 0.22f, 0.9f);
		badgeStyle.SetBorderWidthAll(1);
		badgeStyle.SetCornerRadiusAll(S(9));
		levelBadge.AddThemeStyleboxOverride("panel", badgeStyle);
		levelBadge.Position = new Vector2(sbX + S(6), S(57));
		levelBadge.Size = new Vector2(S(30), S(16));
		sidebarBg.AddChild(levelBadge);
		_levelBadge = levelBadge;

		// LevelLabel: inside badge
		_levelLabel.Position = new Vector2(sbX + S(6), S(57));
		_levelLabel.Size = new Vector2(S(30), S(16));
		_levelLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_levelLabel.VerticalAlignment = VerticalAlignment.Center;
		ApplyFont(_levelLabel, "Tahoma", 700);
		_levelLabel.AddThemeFontSizeOverride("font_size", S(9));
		_levelLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.4f));

		// --- XP bar: xp_bar.png background + fill from StatBarOverlay ---
		var xpBarBg = RpgTheme.CreateNinePatch("xp_bar.png", new Vector4(4, 4, 4, 4));
		xpBarBg.Position = new Vector2(sbX + S(2), S(78));
		xpBarBg.Size = new Vector2(sbW - S(4), S(14));
		sidebarBg.AddChild(xpBarBg);
		_xpBarBg = xpBarBg;

		// ExpLabel: overlay centered on XP bar
		_expLabel.Position = new Vector2(sbX + S(2), S(78));
		_expLabel.Size = new Vector2(sbW - S(4), S(14));
		_expLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_expLabel.VerticalAlignment = VerticalAlignment.Center;
		ApplyFont(_expLabel, "Tahoma", 700);
		_expLabel.AddThemeFontSizeOverride("font_size", S(8));
		_expLabel.AddThemeColorOverride("font_color", Colors.White);
		_expLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
		_expLabel.AddThemeConstantOverride("shadow_offset_x", 1);
		_expLabel.AddThemeConstantOverride("shadow_offset_y", 1);

		// Gold icon + label: positioned relative to BottomBarY
		var goldIcon = new TextureRect();
		goldIcon.Texture = RpgTheme.GetTex("Icons/greed.png");
		goldIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		goldIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		goldIcon.Position = new Vector2(sbX + S(140), ResolutionManager.BottomBarY - S(137));
		goldIcon.Size = new Vector2(S(14), S(14));
		goldIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
		_gameUI.AddChild(goldIcon);
		_goldIcon = goldIcon;

		_goldLabel.Position = new Vector2(sbX + S(155), ResolutionManager.BottomBarY - S(135));
		_goldLabel.Size = new Vector2(S(60), S(14));
		_goldLabel.HorizontalAlignment = HorizontalAlignment.Left;
		ApplyFont(_goldLabel, "Tahoma", 700);
		_goldLabel.AddThemeFontSizeOverride("font_size", S(8));
		_goldLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.502f));

		// Map name + coords — centered below stat bars
		_coordsLabel.Position = new Vector2(sbX, S(549));
		_coordsLabel.Size = new Vector2(sbW, S(28));
		_coordsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		ApplyFont(_coordsLabel, "Tahoma", 700);
		_coordsLabel.AddThemeFontSizeOverride("font_size", S(8));

		// Online label — right side
		_onlineLabel.Position = new Vector2(sbX + sbW / 2, ResolutionManager.BottomBarY - S(5));
		_onlineLabel.Size = new Vector2(sbW / 2 - S(4), S(12));
		_onlineLabel.HorizontalAlignment = HorizontalAlignment.Right;
		ApplyFont(_onlineLabel, "Tahoma", 700);
		_onlineLabel.AddThemeFontSizeOverride("font_size", S(7));

		// Custom stat bar overlay — draws colored fill rects at VB6 positions
		_statBarOverlay = new StatBarOverlay();
		_statBarOverlay.DataPath = dataPath;
		_statBarOverlay.Resources = _resources;
		_statBarOverlay.Position = Vector2.Zero;
		_statBarOverlay.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
		_statBarOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		_gameUI.AddChild(_statBarOverlay);

		// Apply initial form transparency from config
		float initFormAlpha = _state.Config.FormTransparency ? _state.Config.FormTransparencyAlpha / 100f : 1.0f;
		RpgBaseForm.ApplyGlobalAlpha(initFormAlpha);

		// Create all game panels, sidebar buttons, HUD labels, and wire subsystems
		SetupGamePanels();

		// Character creation screen
		_charCreateScreen = new UI.CharCreateScreen(_state, _gameData);
		_charCreateScreen.CreateDeleteConfirmDialog(GetNode<CanvasLayer>("UILayer"));

		// Wire CharSelectForm callbacks
		_charSelectForm!.OnCreatePressed = () =>
		{
			_state.CurrentScreen = Screen.CharCreate;
			HandleScreenChange(Screen.CharCreate);
			_lastScreen = Screen.CharCreate;
		};
		_charSelectForm.OnDeletePressed = () =>
		{
			var charList = _charSelectForm.CharList!;
			var noticeLabel = _charSelectForm.NoticeLabel!;
			if (!charList.IsAnythingSelected())
			{
				noticeLabel.Text = "Seleccione un personaje";
				return;
			}
			_charCreateScreen.ShowDeleteConfirm(charList, noticeLabel);
		};
		_charCreateScreen.OnDeleteCharConfirmed = (_) =>
		{
			var charList = _charSelectForm!.CharList!;
			var noticeLabel = _charSelectForm!.NoticeLabel!;
			int[] selected = charList.GetSelectedItems();
			if (selected.Length == 0 || selected[0] >= _state.CharacterList.Count || _tcp == null) return;
			string charName = _state.CharacterList[selected[0]].Name;
			_tcp.SendPacket(ClientPackets.WriteDeleteCharacter(charName));
			GD.Print($"[MAIN] Sent: DeleteCharacter {charName}");
			noticeLabel.Text = "Eliminando personaje...";
		};

		// Account creation screen
		_accountCreateScreen = new UI.AccountCreateScreen(_state);
		_accountCreateScreen.CreatePanel(GetNode("UILayer"));
		_accountCreateScreen.OnCreateAccount = (name, pass, pin) => _ = ConnectAndCreateAccount(name, pass, pin);
		_accountCreateScreen.OnBack = OnAccountCreateBack;

		// Character creation panel
		_charCreateScreen!.CreatePanel(GetNode("UILayer"));
		_charCreateScreen.OnBack = () =>
		{
			_state.CurrentScreen = Screen.CharSelect;
			HandleScreenChange(Screen.CharSelect);
			_lastScreen = Screen.CharSelect;
		};
		_charCreateScreen.OnCreateCharConfirm = () =>
		{
			int head = _state.CreateCharHead;
			string account = _state.AccountName;
			_tcp!.SendPacket(ClientPackets.WriteCreateCharacter(
				_state.CreateCharName, (byte)_state.CreateCharRace, (byte)_state.CreateCharGender,
				(byte)_state.CreateCharClass, (short)head, (byte)_state.CreateCharFaction, account));
			GD.Print("[MAIN] Sent: CreateCharacter (binary)");
		};

		// Load InvEqu textures (inventory/spell backgrounds) — no longer depends on Principal.jpg
		LoadInvEquTextures(dataPath);

		// Wire signals
		chatInput.TextSubmitted += (text) => _chatSystem?.OnChatSubmitted(text);

		// All forms start hidden
		_charCreateScreen!.Panel!.Visible = false;
		_accountCreateScreen!.Panel!.Visible = false;
		_gameUI.Visible = false;

		// Load remembered account (XOR-encrypted file)
		_loginForm?.LoadRememberedAccount();

		// Apply display preferences
		if (_state.Config.LoadedFromFile)
		{
			GD.Print("[MAIN] Config loaded from file — applying saved display preference");
			if (_state.Config.Fullscreen)
				EnterFullscreen();
			else
				ExitFullscreen();
		}
		else
		{
			// First launch — show window mode dialog (will be behind startup loading screen)
			_dialogManager?.ShowWindowModeDialog();
			CallDeferred(MethodName.CenterWindowModeDialog);
		}

		// Create startup loading screen on UILayer (above everything, blocks all input)
		_startupLoadingScreen = new LoadingScreen();
		_startupLoadingScreen.Init(_state);
		_startupLoadingScreen.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		GetNode("UILayer").AddChild(_startupLoadingScreen);
		_startupLoadingScreen.Show("Argentum Nextgen");
		_startupLoadingScreen.SetLabel("Cargando gráficos...");

		// Start texture preload
		_startupPreloadDone = false;
		_texturePreloadIter = _gameData.Textures!.PreloadAll(_gameData.Grhs);
		GD.Print($"[MAIN] Starting texture preload: {_gameData.Textures.PreloadTotal} textures");
	}

	private void CenterWindowModeDialog()
	{
		_dialogManager?.CenterWindowModeDialog(GetViewportRect().Size);
	}

	private void FocusAccountInput()
	{
		_loginForm?.FocusAccountInput();
	}

	// Static helpers delegated to UIHelpers
	private static void ApplyFont(Label label, string fontName = "Tahoma", int weight = 700)
		=> UI.UIHelpers.ApplyFont(label, fontName, weight);
	private static Label CreateStatLabel(float x, float y, float w, float h, Color color, int fontSize,
										  string fontName = "Tahoma", int weight = 700)
		=> UI.UIHelpers.CreateStatLabel(x, y, w, h, color, fontSize, fontName, weight);
	private static Button CreateInvisibleButton(float x, float y, float w, float h, bool usePointerCursor = true)
		=> UI.UIHelpers.CreateInvisibleButton(x, y, w, h, usePointerCursor);

	private void OnInventoryTabPressed() => _inventoryUI?.OnInventoryTabPressed();
	private void OnSpellTabPressed() => _inventoryUI?.OnSpellTabPressed();
	private void OnLanzarPressed() => _inventoryUI?.OnLanzarPressed();

	private static ImageTexture? LoadJpgTexture(string path)
		=> UI.UIHelpers.LoadJpgTexture(path);

	private static string FriendlyConnectionError(Exception ex)
		=> UI.UIHelpers.FriendlyConnectionError(ex);

	public override void _Notification(int what)
	{
		if (what == NotificationWMWindowFocusIn && _dialogManager != null && _dialogManager.ShouldRestoreFullscreen())
		{
			_dialogManager.BeginRestoreFullscreen();
			GetTree().CreateTimer(0.3).Timeout += () => _dialogManager.RestoreFullscreen(_state.Config);
		}
	}

	private void ShowEscapeMenu() => _dialogManager?.ShowEscapeMenu(GetViewportRect().Size);
	private void HideEscapeMenu() => _dialogManager?.HideEscapeMenu();

	// Drop dialog methods moved to DialogManager.cs
	private void CloseDropDialog() => _dialogManager?.CloseDropDialog();

	private void OnInventoryDropOutside(int slot, Vector2 globalPos) =>
		_inventoryUI?.OnInventoryDropOutside(slot, globalPos);


	public override void _Process(double delta)
	{
		// Startup texture preload (blocks everything until done)
		if (!_startupPreloadDone && _texturePreloadIter != null)
		{
			var texMgr = _gameData.Textures;
			if (texMgr != null)
			{
				bool done = texMgr.TickPreload(_texturePreloadIter, 12.0);
				float progress = texMgr.PreloadTotal > 0
					? (float)texMgr.PreloadDone / texMgr.PreloadTotal
					: 1f;
				_startupLoadingScreen?.SetProgress(progress);
				_startupLoadingScreen?.SetLabel(
					$"Cargando gráficos... ({texMgr.PreloadDone}/{texMgr.PreloadTotal})");

				if (done)
				{
					_startupPreloadDone = true;
					_texturePreloadIter = null;
					_startupLoadingScreen?.Complete();
					GD.Print("[MAIN] Texture preload complete");

					// Now show login (or window mode dialog if first launch)
					if (_state.Config.LoadedFromFile)
					{
						if (_loginForm != null) _loginForm.ShowForm();
						CallDeferred(MethodName.FocusAccountInput);
					}
				}
			}
			return; // Block all other processing
		}

		if (_tcp == null || _packetHandler == null) return;

		// VB6: Socket1_Disconnect — detect lost connection and return to login
		if (!_connecting && !_tcp.IsConnected)
		{
			if (_state.CurrentScreen == Screen.Login || _state.CurrentScreen == Screen.AccountCreate)
			{
				// Drain packets buffered before disconnect (e.g. success message sent just before server closed)
				if (_tcp != null)
				{
					foreach (var chunk in _tcp.PollPackets())
						_packetHandler?.HandleBinaryData(chunk);
				}

				// Server dropped connection during login/account creation — reset UI
				_tcp?.Dispose();
				_tcp = null;
				_packetHandler = null;
				_inputHandler = null;

				// After draining packets, show any error/success message from the server
				if (!string.IsNullOrEmpty(_state.LoginError))
				{
					string msg = _state.LoginError;
					_state.LoginError = "";

					// Check for account creation success
					if (_state.CurrentScreen == Screen.AccountCreate &&
						msg.Contains("exito", StringComparison.OrdinalIgnoreCase))
					{
						_state.CurrentScreen = Screen.Login;
						HandleScreenChange(Screen.Login);
						_lastScreen = Screen.Login;
						_dialogManager?.ShowMensaje("Cuenta creada exitosamente. Ingrese sus datos.", GetViewportRect().Size);
					}
					else
					{
						// Show the actual error message from the server
						_dialogManager?.ShowMensaje(msg, GetViewportRect().Size);
					}
				}
				else
				{
					_dialogManager?.ShowMensaje("El servidor cerró la conexión.", GetViewportRect().Size);
				}

				if (_loginForm?.ConnectButton != null) _loginForm!.ConnectButton.Disabled = false;
				return;
			}
			HandleDisconnect("Conexión perdida con el servidor.");
			return;
		}

		// Poll and process inbound binary data
		var dataChunks = _tcp.PollPackets();
		foreach (byte[] chunk in dataChunks)
		{
			_packetHandler.HandleBinaryData(chunk);
			if (_packetHandler.StreamCorrupted)
			{
				HandleDisconnect("Stream corrupted (unknown opcode) — disconnected.");
				return;
			}
		}

		// Update spatial audio listener position each frame
		_soundManager?.UpdateListenerPosition(_state.UserPosX, _state.UserPosY);

		// React to screen transitions driven by PacketHandler
		if (_state.CurrentScreen != _lastScreen)
		{
			HandleScreenChange(_state.CurrentScreen);
			_lastScreen = _state.CurrentScreen;
		}

		// Show login/server errors via Mensaje dialog (not inline labels)
		if (!string.IsNullOrEmpty(_state.LoginError))
		{
			string errorMsg = _state.LoginError;
			_state.LoginError = "";

			// Re-enable buttons so user can retry after dismissing the error
			if (_state.CurrentScreen == Screen.Login)
			{
				if (_loginForm?.ConnectButton != null) _loginForm!.ConnectButton.Disabled = false;
			}
			else if (_state.CurrentScreen == Screen.CharCreate)
			{
				if (_charCreateScreen?.CreateButton != null) _charCreateScreen.CreateButton.Disabled = false;
			}
			else if (_state.CurrentScreen == Screen.AccountCreate)
			{
				if (errorMsg.Contains("exito", StringComparison.OrdinalIgnoreCase))
				{
					// Success message — auto-switch to login after timer
					if (_accountCreateScreen != null) _accountCreateScreen.SuccessTimer = 2.0;
				}
				_accountCreateScreen?.EnableCreateButton();
			}

			// Show ALL server messages via the Mensaje dialog form
			_dialogManager?.ShowMensaje(errorMsg, GetViewportRect().Size);
		}

		// Show Mensaje dialog (VB6: Mensaje form — ERR, ERO, !! packets)
		if (!string.IsNullOrEmpty(_state.MensajeText))
		{
			_dialogManager?.ShowMensaje(_state.MensajeText, GetViewportRect().Size);
			_state.MensajeText = "";
		}

		// Dismiss Mensaje dialog with Enter key
		if (_dialogManager != null && _dialogManager.IsMensajeVisible)
		{
			if (Input.IsActionJustPressed("ui_accept"))
				_dialogManager.OnMensajeAccept();
		}

		// Account creation success → auto-switch to login after timer
		if (_accountCreateScreen != null && _accountCreateScreen.SuccessTimer > 0 && _state.CurrentScreen == Screen.AccountCreate)
		{
			_accountCreateScreen.SuccessTimer -= delta;
			if (_accountCreateScreen.SuccessTimer <= 0)
			{
				_tcp?.Dispose();
				_tcp = null;
				_packetHandler = null;
				_connecting = false;

				_state.CurrentScreen = Screen.Login;
				HandleScreenChange(Screen.Login);
				_lastScreen = Screen.Login;
				_dialogManager?.ShowMensaje("Cuenta creada exitosamente. Ingrese sus datos.", GetViewportRect().Size);
			}
		}

		// Map loading is now handled immediately in HandleChangeMap via OnMapLoad callback.
		// This ensures BQ/HO packets apply to the correct MapData.

		// After LOGGED, transition to game
		if (_state.IsLogged && _state.CurrentScreen != Screen.Game)
		{
			_state.CurrentScreen = Screen.Game;
			HandleScreenChange(Screen.Game);
			_lastScreen = Screen.Game;
		}

		// Update animations
		_animator.Update((float)delta, _gameData);

		// Update particle simulation
		_particleSystem.Update((float)delta, _state);

		// Update arrow projectiles (move toward target, remove on arrival)
		UpdateArrowProjectiles((float)delta);

		// Recalculate lighting when dirty (lights added/removed/map changed)
		if (_state.LightsDirty)
		{
			_lightSystem.RecalculateLights(_state);
			_state.LightsDirty = false;
			_worldRenderer?.MarkLightmapDirty();
		}

		if (_state.CurrentScreen == Screen.Game)
		{
			// VB6 order: CheckKeys BEFORE ShowNextFrame.
			// Input runs first so there's a 1-frame gap between scroll completion
			// and the next move — matching VB6's timer tick behavior.
			_inputHandler?.Process(delta);

			// Update work/spell macros (auto-repeat timers)
			_state.WorkMacro.Update(delta);
			_state.SpellMacro.Update(delta);

			UpdateGameUI();
			_chatSystem?.UpdateConsoleMessages();

			// Sync panel visibility with state flags
			_panelSync?.Update((float)delta);
		}

		// Movement update AFTER input (VB6: ShowNextFrame after CheckKeys)
		UpdateMovement((float)delta);
	}

	/// <summary>
	/// Update stats bars, labels, and inventory from GameState.
	/// </summary>
	private void UpdateGameUI() => _gameUIUpdater?.UpdateGameUI();
	private void UpdateArrowProjectiles(float delta) => _gameUIUpdater?.UpdateArrowProjectiles(delta);


	public override void _Input(InputEvent @event)
	{
		// Block all input during startup preload
		if (!_startupPreloadDone) return;

		// Borderless window drag: click+drag on non-interactive areas moves the window.
		// Uses position-based check instead of GuiGetHoveredControl (which is unreliable).
		if (_state.Config.DragWindowEnabled
			&& DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed)
		{
			if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					// Block drag on viewport (game clicks). Allow drag on sidebar
					// empty space but not on interactive controls (buttons, etc.).
					float cx = mb.Position.X;
					float cy = mb.Position.Y;
					bool inViewport = cx >= ResolutionManager.LeftMargin
						&& cx < ResolutionManager.LeftMargin + ResolutionManager.ViewportW
						&& cy >= ResolutionManager.TopMargin
						&& cy < ResolutionManager.TopMargin + ResolutionManager.ViewportH;
					if (!inViewport && !_state.AnyFormOpen)
					{
						var hovered = GetViewport().GuiGetHoveredControl();
						bool onControl = hovered is BaseButton or Godot.Range
							or LineEdit or TextEdit or InventoryPanel or SpellPanel;
						// Also block if hovered control is inside an RpgBaseForm
						if (!onControl && hovered != null)
						{
							var p = hovered.GetParent();
							while (p != null) { if (p is UI.RpgBaseForm) { onControl = true; break; } p = p.GetParent(); }
						}
						if (!onControl)
						{
							_windowDragging = true;
							_windowDragStart = DisplayServer.MouseGetPosition();
						}
					}
				}
				else
				{
					_windowDragging = false;
				}
			}
			else if (@event is InputEventMouseMotion && _windowDragging)
			{
				var mouseNow = DisplayServer.MouseGetPosition();
				var delta = mouseNow - _windowDragStart;
				DisplayServer.WindowSetPosition(DisplayServer.WindowGetPosition() + delta);
				_windowDragStart = mouseNow;
			}
		}

		// Escape on login -> quit game
		if (@event is InputEventKey escKey && escKey.Pressed && !escKey.Echo
			&& escKey.Keycode == Key.Escape
			&& _state.CurrentScreen == Screen.Login)
		{
			GetTree().Quit();
			return;
		}

		// Escape on char select -> disconnect back to login
		if (@event is InputEventKey escCharKey && escCharKey.Pressed && !escCharKey.Echo
			&& escCharKey.Keycode == Key.Escape
			&& _state.CurrentScreen == Screen.CharSelect)
		{
			HandleDisconnect("");
			return;
		}

		// Enter on login screen -> trigger connect button
		if (@event is InputEventKey enterLoginKey && enterLoginKey.Pressed && !enterLoginKey.Echo
			&& (enterLoginKey.Keycode == Key.Enter || enterLoginKey.Keycode == Key.KpEnter)
			&& _state.CurrentScreen == Screen.Login
			&& _loginForm?.ConnectButton != null && !_loginForm!.ConnectButton.Disabled)
		{
			_loginForm!.ConnectButton.EmitSignal("pressed");
			return;
		}

		// Enter on char select -> connect with selected character
		if (@event is InputEventKey enterCharKey && enterCharKey.Pressed && !enterCharKey.Echo
			&& (enterCharKey.Keycode == Key.Enter || enterCharKey.Keycode == Key.KpEnter)
			&& _state.CurrentScreen == Screen.CharSelect)
		{
			OnEnterPressed();
			return;
		}

		// F12: toggle fullscreen — works on ALL screens
		if (@event is InputEventKey f12Key && f12Key.Pressed && !f12Key.Echo
			&& f12Key.Keycode == Key.F12)
		{
			bool goFullscreen = DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen;
			if (goFullscreen) EnterFullscreen(); else ExitFullscreen();
			_state.Config.Fullscreen = goFullscreen;
			_state.Config.Save(_dataPath);
			GetViewport().SetInputAsHandled();
			// Re-center any visible form after fullscreen toggle
			if (_loginForm != null && _loginForm.Visible) _loginForm.ShowForm();
			if (_charSelectForm != null && _charSelectForm.Visible) _charSelectForm.ShowForm();
			if (_charCreateScreen?.Panel != null && _charCreateScreen.Panel.Visible)
				CenterPanelOnScreen(_charCreateScreen.Panel);
			if (_accountCreateScreen?.Panel != null && _accountCreateScreen.Panel.Visible)
				CenterPanelOnScreen(_accountCreateScreen.Panel);
			return;
		}

		if (_state.CurrentScreen != Screen.Game) return;

		// Delegate game-screen input to InputRouter
		_inputRouter?.HandleGameInput(@event, GetViewport(), GetTree());
	}

	/// <summary>
	/// Resolve data path when running inside the Godot editor.
	/// Priority:
	/// 1. res://Data/ if it contains .aopak files (dev with packed archives)
	/// 2. tools/resource-manager/data/ if it exists (dev with loose files)
	/// 3. res://Data/ as fallback
	/// </summary>
	private static string ResolveEditorDataPath()
	{
		string editorData = ProjectSettings.GlobalizePath("res://Data");

		// Check for .aopak files already present in editor Data/
		if (System.IO.Directory.Exists(editorData) &&
		    System.IO.Directory.GetFiles(editorData, "*.aopak").Length > 0)
		{
			return editorData;
		}

		// Check resource-manager/data/ — resources live there during development
		// editorData = <repo>/client/Data, so ../../ reaches <repo>/
		string resourceManagerData = System.IO.Path.GetFullPath(
			System.IO.Path.Combine(editorData, "..", "..", "tools", "resource-manager", "data"));
		if (System.IO.Directory.Exists(resourceManagerData))
			return resourceManagerData;

		return editorData;
	}

	public override void _ExitTree()
	{
		_tcp?.Dispose();
		_gameData?.Textures?.Cleanup();
	}
}

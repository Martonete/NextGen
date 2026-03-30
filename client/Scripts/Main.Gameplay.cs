using Godot;
using System;
using System.Threading.Tasks;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;

namespace ArgentumNextgen;

/// <summary>
/// Partial class containing gameplay logic: movement, map loading, screen transitions,
/// disconnect handling, config application, and login/account connection flows.
/// Split from Main.cs to keep the orchestrator under 800 lines.
/// </summary>
public partial class Main
{
    // VB6 movement constants
    private const float EngineBaseSpeed = 0.0172f;   // VB6 timerTicksPerFrame = deltaMs * 0.0172
    private const float ScrollPixelsPerFrame = 8f;   // VB6 ScrollPixelsPerFrameX/Y

    private void HandleScreenChange(Screen newScreen)
    {
        GD.Print($"[MAIN] Screen → {newScreen}");

        // Always hide delete confirm dialog on screen change
        _charCreateScreen?.HideDeleteConfirm();

        switch (newScreen)
        {
            case Screen.Login:
                _loginForm!.ShowForm();
                _charSelectForm!.HideForm();
                _charCreateScreen!.Panel!.Visible = false;
                _accountCreateScreen!.Panel!.Visible = false;
                _gameUI!.Visible = false;
                break;

            case Screen.CharSelect:
                _loginForm!.HideForm();
                _charSelectForm!.ShowForm();
                _charCreateScreen!.Panel!.Visible = false;
                _accountCreateScreen!.Panel!.Visible = false;
                _gameUI!.Visible = false;
                _charSelectForm!.EnterButton!.Disabled = false;
                _charSelectForm!.NoticeLabel!.Text = "";
                PopulateCharList();
                break;

            case Screen.AccountCreate:
                _loginForm!.HideForm();
                _charSelectForm!.HideForm();
                _charCreateScreen!.Panel!.Visible = false;
                _accountCreateScreen!.Panel!.Visible = true;
                _gameUI!.Visible = false;
                _accountCreateScreen?.ResetForm();
                CenterPanelOnScreen(_accountCreateScreen!.Panel!);
                break;

            case Screen.CharCreate:
                _loginForm!.HideForm();
                _charSelectForm!.HideForm();
                _charCreateScreen!.Panel!.Visible = true;
                _accountCreateScreen!.Panel!.Visible = false;
                _gameUI!.Visible = false;
                _charCreateScreen?.ResetForm();
                CenterPanelOnScreen(_charCreateScreen!.Panel!);
                break;

            case Screen.Game:
                _loginForm!.HideForm();
                _charSelectForm!.HideForm();
                _charCreateScreen!.Panel!.Visible = false;
                _accountCreateScreen!.Panel!.Visible = false;
                _gameUI!.Visible = true;
                // Initialize inventory/spell panels with TCP (only available after connect)
                if (_tcp != null)
                {
                    _inventoryPanel!.Init(_state, _gameData, _tcp);
                    _inventoryPanel!.OnDropOutside += OnInventoryDropOutside;
                    _spellPanel!.Init(_state, _gameData, _tcp);
                    _commercePanel!.Init(_state, _gameData, _tcp);
                    _tradePanel!.Init(_state, _gameData, _tcp);
                    _bankPanel!.Init(_state, _gameData, _tcp);
                    _vaultPanel!.Init(_state, _gameData, _tcp);
                    _guildBankPanel!.Init(_state, _gameData, _tcp);
                    _craftPanel!.Init(_state, _gameData, _tcp);
                    _travelPanel!.Init(_state, _tcp, _dataPath, _resources);
                    _deathPanel!.Init(_state, _tcp, _dataPath, _resources);
                    _guildPanel!.Init(_state, _tcp);
                    _guildFoundationPanel!.Init(_state, _tcp);
                    _forumPanel!.Init(_state, _tcp);
                    _partyPanel!.Init(_state, _tcp);
                    if (_chatSystem != null) _chatSystem.PartyPanel = _partyPanel;
                    _gameUIUpdater?.BindMinimap(_minimapPanel, _partyPanel);
                    _questPanel!.Init(_state, _tcp);
                    _trainerPanel!.Init(_state, _tcp);
                    _optionsPanel!.Init(_state, _state.Config, _dataPath, _tcp);
                    _statsPanel!.Init(_state, _tcp);
                    _charInfoPopup!.Init(_state);
                    _contextMenu!.Init(_state, _tcp);
                    _changePasswordPanel!.Init(_state, _tcp);
                    _contextMenu!.OnWhisper += (name) =>
                    {
                        // Activate chat in whisper mode
                        _state.ChatMode = 7;
                        _state.ChatModePrefix = "\\";
                        _state.WhisperTarget = name;
                        _chatSystem?.ShowChat();
                    };
                    // Wire TCP to new panels
                    _gmPanel?.SetTcp(_tcp);
                    _spawnListPanel?.SetTcp(_tcp);
                    _sosPanel?.SetTcp(_tcp);
                    _motdEditorPanel?.SetTcp(_tcp);
                    _guildAlignmentPanel?.SetTcp(_tcp);
                    _peaceProposalPanel?.SetTcp(_tcp);
                    _guildMemberPanel?.SetTcp(_tcp);

                    // Show tutorial for first-time players
                    if (_tutorialPanel != null && !_tutorialPanel.IsCompleted())
                        _tutorialPanel.Open();
                }
                GD.Print("[MAIN] Entered game world");
                break;
        }
    }

    /// <summary>
    /// Center a Control on the real screen (for panels inside CanvasLayer).
    /// </summary>
    private static void CenterPanelOnScreen(Control panel)
    {
        var ws = DisplayServer.WindowGetSize();
        panel.Position = (new Vector2(ws.X, ws.Y) - panel.Size * panel.Scale) / 2f;
    }

    /// <summary>
    /// Enter fullscreen with aspect ratio mode from config.
    /// 0 = 4:3 (keep aspect, black bars on wide screens)
    /// 1 = 16:9 (stretch to fill, no black bars)
    /// </summary>
    private void EnterFullscreen()
    {
        var root = GetTree().Root;
        // Set content scale to match the current resolution layout.
        // This tells Godot to scale our WindowWidth x WindowHeight content to fill the screen.
        root.ContentScaleSize = new Vector2I(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        root.ContentScaleStretch = Window.ContentScaleStretchEnum.Fractional;
        root.ContentScaleAspect = _state.Config.AspectRatioMode == 0
            ? Window.ContentScaleAspectEnum.Keep
            : Window.ContentScaleAspectEnum.Ignore;
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
    }

    /// <summary>
    /// Exit fullscreen to windowed mode at configured resolution, centered.
    /// </summary>
    private void ExitFullscreen()
    {
        var root = GetTree().Root;
        // Disable content scaling — in windowed mode we use real pixels
        root.ContentScaleSize = new Vector2I(0, 0);
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.ContentScaleStretch = Window.ContentScaleStretchEnum.Fractional;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
        // Borderless window — we have our own minimize/close buttons
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        var winSize = new Vector2I(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        DisplayServer.WindowSetSize(winSize);
        var screenSize = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition(new Vector2I(
            (screenSize.X - winSize.X) / 2,
            (screenSize.Y - winSize.Y) / 2));
    }

    /// <summary>
    /// Apply GameConfig values to all subsystems (called after options are saved).
    /// </summary>
    private void ApplyConfigToSystems()
    {
        var cfg = _state.Config;

        // Sync ShowNames to GameState (used by CharRenderer directly)
        _state.ShowNames = cfg.ShowNames;

        // Apply audio settings
        if (_soundManager != null)
        {
            _soundManager.MusicEnabled = cfg.MusicEnabled;
            _soundManager.SoundEnabled = cfg.SfxEnabled;
            _soundManager.SetMusicVolume(cfg.MusicVolume);
            _soundManager.SetSfxVolume(cfg.SfxVolume);
        }

        // Apply V-Sync
        DisplayServer.WindowSetVsyncMode(
            cfg.VsyncEnabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        // Apply FPS limit
        Engine.MaxFps = cfg.FpsLimit > 0 ? cfg.FpsLimit : 0;

        // Apply display mode
        if (cfg.Fullscreen)
            EnterFullscreen();
        else
            ExitFullscreen();

        // Apply day/night cycle toggle
        if (_dayNightCycle != null)
            _dayNightCycle.Enabled = cfg.ShowDayNight;

        // Apply minimap visibility + resize console accordingly
        if (_minimapPanel != null)
        {
            _minimapPanel.Visible = cfg.ShowMinimap;
            UpdateConsoleWidth();
        }

        // Apply form transparency
        float formAlpha = cfg.FormTransparency ? cfg.FormTransparencyAlpha / 100f : 1.0f;
        RpgBaseForm.ApplyGlobalAlpha(formAlpha);

        GD.Print($"[CFG] Applied config: VSync={cfg.VsyncEnabled}, FPS={cfg.FpsLimit}, Music={cfg.MusicEnabled}, Fullscreen={cfg.Fullscreen}, Aspect={cfg.AspectRatioMode}");
    }

    /// <summary>
    /// VB6 Socket1_Disconnect: clean up everything and return to login.
    /// Called when TCP connection is lost (server close, error, timeout).
    /// </summary>
    private void HandleDisconnect(string message)
    {
        GD.Print($"[MAIN] Disconnect: {message}");

        // Save server error and current screen before reset clears them
        string serverError = _state.LoginError;
        Screen previousScreen = _state.CurrentScreen;

        // Clean up TCP resources (VB6: Socket1.Disconnect + Socket1.Cleanup)
        _tcp?.Dispose();
        _tcp = null;
        _packetHandler = null;
        _inputHandler = null;
        _connecting = false;

        // Reset all game state (VB6: clear logged, skills, attributes, etc.)
        ResetGameState();

        // Hide chat input and clear console
        _chatSystem?.HideChat();
        _chatSystem?.ClearConsole();

        // Clear minimap
        // Close escape menu
        HideEscapeMenu();

        // Close all game panels and reset tracking state
        _panelSync?.CloseAll();
        CloseDropDialog();
        if (_blindOverlay != null) _blindOverlay.Color = new Color(0, 0, 0, 0);

        // Reset char create button state
        if (_charCreateScreen?.CreateButton != null)
            _charCreateScreen.CreateButton.Disabled = false;

        // Reset account create state
        _accountCreateScreen?.EnableCreateButton();
        if (_accountCreateScreen != null) _accountCreateScreen.SuccessTimer = 0;
        if (_accountCreateScreen?.Panel != null)
            _accountCreateScreen.Panel.Visible = false;

        // Reset spell/inventory tab to default (inventory)
        OnInventoryTabPressed();

        // If disconnect from CharCreate or CharSelect, go back to CharSelect so user can retry without re-logging
        if (previousScreen == Screen.CharCreate || previousScreen == Screen.CharSelect)
        {
            _state.CurrentScreen = Screen.CharSelect;
            HandleScreenChange(Screen.CharSelect);
            _lastScreen = Screen.CharSelect;
            if (!string.IsNullOrEmpty(serverError))
                _dialogManager?.ShowMensaje(serverError, GetViewportRect().Size);
        }
        else
        {
            // Switch to login screen with error message
            _state.CurrentScreen = Screen.Login;
            HandleScreenChange(Screen.Login);
            _lastScreen = Screen.Login;
            // Prefer server error message (e.g. "Password incorrecto") over generic disconnect
            string displayMsg = !string.IsNullOrEmpty(serverError) ? serverError : message;
            if (_loginForm?.StatusLabel != null) _loginForm.StatusLabel.Text = displayMsg;
            if (_loginForm?.ConnectButton != null) _loginForm.ConnectButton.Disabled = false;
        }

        // VB6: frmConnect.MousePointer = 1 (normal cursor)
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
    }

    /// <summary>
    /// Reset GameState to defaults (VB6: Socket1_Disconnect cleanup).
    /// Clears all character data, stats, inventory, spells, flags.
    /// </summary>
    private void ResetGameState()
    {
        _state.IsLogged = false;
        _state.Paused = false;
        _state.LoginError = "";
        _state.CoordCipher = null;
        _state.ServerNotice = "";
        _state.SecurityCode = "";
        _state.CharacterList.Clear();
        _state.SelectedCharIndex = -1;

        // Map
        _state.CurrentMap = 0;
        _state.MapName = "";
        _state.MapColorR = 200;
        _state.MapColorG = 200;
        _state.MapColorB = 200;
        _state.MapData = null;
        _state.NeedMapLoad = false;

        // Position & movement
        _state.UserPosX = 0;
        _state.UserPosY = 0;
        _state.UserCharIndex = 0;
        _state.UserName = "";
        _state.UserParalyzed = false;
        _state.ParalysisTimer = 0;
        _state.UserNavigating = false;
        _state.UserStopped = false;
        _state.UsingSkill = 0;
        _state.ChatActive = false;
        _state.Comerciando = false;
        _state.Trading = false;
        _state.Dead = false;
        _state.ShowDeathPanel = false;
        _state.Resting = false;
        _state.Meditating = false;
        _state.SafeMode = false;
        _state.SeguroResu = false;
        _state.DropDialogOpen = false;
        _state.ShowTravelPanel = false;
        _state.UserMoving = false;
        _state.AddToUserPosX = 0;
        _state.AddToUserPosY = 0;
        _state.ScreenOffsetX = 0;
        _state.ScreenOffsetY = 0;
        _state.PtCooldownUntilMs = 0;
        _state.PendingMoves = 0;

        // Characters & objects
        _state.Characters.Clear();
        _state.GroundObjects.Clear();

        // Stats
        _state.MaxHp = 0; _state.MinHp = 0;
        _state.MaxMana = 0; _state.MinMana = 0;
        _state.MaxSta = 0; _state.MinSta = 0;
        _state.MaxAgua = 0; _state.MinAgua = 0;
        _state.MaxHam = 0; _state.MinHam = 0;
        _state.Gold = 0;
        _state.Level = 0;
        _state.Exp = 0; _state.ExpNext = 0;
        _state.Reputation = 0;
        _state.Privileges = 0;
        _state.MusicId = 0;
        _state.OnlineCount = 0;
        _state.Strength = 0;
        _state.Agility = 0;
        _state.AttackMin = 0; _state.AttackMax = 0;
        _state.DefenseMin = 0; _state.DefenseMax = 0;
        _state.MagDefMin = 0; _state.MagDefMax = 0;
        _state.WeaponEqpSlot = 0; _state.ArmourEqpSlot = 0;
        _state.ShieldEqpSlot = 0; _state.HelmEqpSlot = 0;
        _state.WeaponLabel = "0/0"; _state.ArmourLabel = "0/0";
        _state.ShieldLabel = "0/0"; _state.HelmLabel = "0/0";

        // Inventory & spells
        for (int i = 0; i < 25; i++)
            _state.Inventory[i] = new InventorySlot();
        for (int i = 0; i < 20; i++)
            _state.Spells[i] = new SpellSlot();

        // Commerce
        _state.NpcShopCount = 0;
        for (int i = 0; i < 50; i++)
            _state.NpcShopItems[i] = new NpcShopItem();

        // Bank
        _state.BankItemCount = 0;
        _state.BankGold = 0;
        _state.Banqueando = false;
        _state.BovedaAbierta = false;

        // Guild Bank
        _state.ShowGuildBank = false;
        _state.GuildBankGold = 0;
        _state.GuildBankCanObj = false;
        _state.GuildBankCanGold = false;
        for (int i = 0; i < _state.GuildBankItems.Length; i++)
            _state.GuildBankItems[i] = new GuildBankSlot();
        for (int i = 0; i < 40; i++)
            _state.BankItems[i] = new BankItem();

        // Chat queue and history
        _state.ChatMessages.Clear();
        _state.ChatHistory.Clear();
        _state.ActiveChatFilter = -1;
        _state.ChatFilterDirty = false;
        _chatSystem?.UpdateChatTabHighlight();
    }

    /// <summary>
    /// VB6-accurate movement interpolation.
    /// timerTicksPerFrame = deltaMs * EngineBaseSpeed (0.0172)
    /// scrollPixels = ScrollPixelsPerFrame (8) * timerTicksPerFrame
    /// Full tile (32px) ≈ 14 frames ≈ 233ms at 60fps.
    ///
    /// Delta is capped to prevent lag spikes from completing scrolls in one frame,
    /// which would let the client send moves faster than intended.
    /// VB6 timer was fixed ~17ms; we allow up to 50ms (3 frames) for flexibility.
    /// </summary>
    private void UpdateMovement(float delta)
    {
        // Cap delta to prevent lag-spike acceleration (VB6 timer was ~17ms fixed)
        float deltaMs = Math.Min(delta * 1000f, 50f);
        float ticksPerFrame = deltaMs * EngineBaseSpeed;
        float scrollPixels = ScrollPixelsPerFrame * ticksPerFrame;

        // Camera scroll (VB6 ShowNextFrame → OffsetCounterX/Y)
        if (_state.UserMoving)
        {
            _state.ScreenOffsetX += ScrollPixelsPerFrame * _state.AddToUserPosX * ticksPerFrame;
            _state.ScreenOffsetY += ScrollPixelsPerFrame * _state.AddToUserPosY * ticksPerFrame;

            // Complete when offset reaches a full tile (32px)
            bool doneX = _state.AddToUserPosX == 0 || Math.Abs(_state.ScreenOffsetX) >= 32f;
            bool doneY = _state.AddToUserPosY == 0 || Math.Abs(_state.ScreenOffsetY) >= 32f;

            if (doneX && doneY)
            {
                _state.ScreenOffsetX = 0;
                _state.ScreenOffsetY = 0;
                _state.AddToUserPosX = 0;
                _state.AddToUserPosY = 0;
                _state.UserMoving = false;

                // Scroll completed — assume server accepted the move
                if (_state.PendingMoves > 0)
                    _state.PendingMoves--;

                // Sync: force self char's MoveOffset to complete too (avoid 1-frame glitch)
                if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh))
                {
                    selfCh.MoveOffsetX = 0;
                    selfCh.MoveOffsetY = 0;
                    selfCh.Moving = false;
                    selfCh.ScrollDirectionX = 0;
                    selfCh.ScrollDirectionY = 0;
                }
            }
        }

        // Character sprite interpolation + per-character walk animation
        foreach (var kvp in _state.Characters)
        {
            var ch = kvp.Value;

            // Advance walk animation frame only while Moving (VB6: per-char FrameCounter)
            if (ch.Moving && ch.Body > 0 && ch.Body < _gameData.Bodies.Length)
            {
                int heading = ch.Heading;
                if (heading < 1 || heading > 4) heading = 3;
                int walkGrh = _gameData.Bodies[ch.Body].Walk[heading];
                if (walkGrh > 0 && walkGrh < _gameData.Grhs.Length)
                {
                    var grh = _gameData.Grhs[walkGrh];
                    if (grh.NumFrames > 1)
                    {
                        float speed = grh.Speed > 0 ? grh.Speed : 100f;
                        ch.WalkFrame += (deltaMs * grh.NumFrames / speed) * 0.7f;
                        if (ch.WalkFrame >= grh.NumFrames)
                            ch.WalkFrame %= grh.NumFrames;
                    }
                }
            }

            if (!ch.Moving && ch.MoveOffsetX == 0 && ch.MoveOffsetY == 0)
                continue;

            // Interpolate X using ScrollDirection
            if (ch.MoveOffsetX != 0)
            {
                ch.MoveOffsetX += scrollPixels * ch.ScrollDirectionX;
                // Complete when offset crosses zero (moved past destination)
                if ((ch.ScrollDirectionX > 0 && ch.MoveOffsetX >= 0) ||
                    (ch.ScrollDirectionX < 0 && ch.MoveOffsetX <= 0) ||
                    ch.ScrollDirectionX == 0)
                {
                    ch.MoveOffsetX = 0;
                }
            }

            // Interpolate Y using ScrollDirection
            if (ch.MoveOffsetY != 0)
            {
                ch.MoveOffsetY += scrollPixels * ch.ScrollDirectionY;
                if ((ch.ScrollDirectionY > 0 && ch.MoveOffsetY >= 0) ||
                    (ch.ScrollDirectionY < 0 && ch.MoveOffsetY <= 0) ||
                    ch.ScrollDirectionY == 0)
                {
                    ch.MoveOffsetY = 0;
                }
            }

            if (ch.MoveOffsetX == 0 && ch.MoveOffsetY == 0)
            {
                ch.Moving = false;
                ch.ScrollDirectionX = 0;
                ch.ScrollDirectionY = 0;
            }
        }
    }

    private void LoadCurrentMap()
    {
        string mapDir;
        if (OS.HasFeature("editor"))
            mapDir = ProjectSettings.GlobalizePath("res://Data/Maps");
        else
            mapDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".",
                "Data", "Maps"
            );

        try
        {
            _state.MapData = MapLoader.Load(_resources, _state.CurrentMap);
            _animator.Clear(); // Resets global clock — all tile anims restart from frame 0
            _gameData.Textures?.ResetPreload(); // Allow re-evaluation of preload state on map change

            // Load particles and lights embedded in tile data (byFlags bits 5/6)
            LoadTileParticlesAndLights(_state);

            // Pre-compute spatial maps for O(1) lookups during rendering
            _worldRenderer?.RebuildWaterMap();
            _worldRenderer?.BuildRoofRegions();

            GD.Print($"[MAIN] Map {_state.CurrentMap} loaded OK");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load map {_state.CurrentMap}: {ex.Message}");
        }
    }

    private void LoadTileParticlesAndLights(GameState state)
    {
        if (state.MapData == null) return;
        var map = state.MapData;
        for (int y = 1; y <= map.Height; y++)
        {
            for (int x = 1; x <= map.Width; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (tile.ParticleGroup > 0)
                    ParticleSystem.CreateMapStream(state, tile.ParticleGroup, x, y);
                if (tile.LightRange > 0)
                {
                    state.MapLights.Add(new MapLight
                    {
                        X = x, Y = y, Range = tile.LightRange,
                        R = (byte)tile.LightR, G = (byte)tile.LightG, B = (byte)tile.LightB,
                        Active = true
                    });
                    state.LightsDirty = true;
                }
            }
        }
    }

    private void LoadInvEquTextures(string dataPath)
    {
        // CentroInventario/CentroHechizos removed — HUD frame is now UIKit-based
    }

    private void PopulateCharList()
    {
        _charSelectForm!.CharList!.Clear();
        foreach (var ch in _state.CharacterList)
        {
            string label = $"{ch.Name} — Lvl {ch.Level} ({ch.Class})";
            if (ch.Dead) label += " [MUERTO]";
            _charSelectForm!.CharList!.AddItem(label);
        }
        if (!string.IsNullOrEmpty(_state.ServerNotice))
            _charSelectForm!.NoticeLabel!.Text = _state.ServerNotice;
    }

    private void OnEnterPressed()
    {
        if (_charSelectForm!.CharList!.IsAnythingSelected())
        {
            int[] selected = _charSelectForm!.CharList!.GetSelectedItems();
            if (selected.Length > 0 && selected[0] < _state.CharacterList.Count)
            {
                var charPreview = _state.CharacterList[selected[0]];
                string charName = charPreview.Name;
                string account = _state.AccountName;
                string code = _state.SecurityCode;

                // Pre-populate name/class/race from preview (server may overwrite later)
                _state.UserName = charPreview.Name;
                _state.UserClassName = charPreview.Class;
                _state.UserRaceName = charPreview.Race;

                _charSelectForm!.EnterButton!.Disabled = true;
                _charSelectForm!.NoticeLabel!.Text = "Entrando al mundo...";

                _tcp!.SendPacket(ClientPackets.WriteCharacterLogin(charName, account, code));
                GD.Print($"[MAIN] Sent: CharacterLogin {charName}");
            }
        }
        else
        {
            _charSelectForm!.NoticeLabel!.Text = "Seleccione un personaje";
        }
    }

    /// <summary>
    /// Double-click on character list → enter game (same as clicking Enter button).
    /// </summary>
    private void OnCharListDoubleClick(long index)
    {
        OnEnterPressed();
    }

    /// <summary>
    /// Called when BankPanel's "Abrir Bóveda" button is clicked.
    /// Opens VaultPanel (the item vault).
    /// </summary>
    private void OnBankOpenVault()
    {
        _vaultPanel?.OpenVault();
    }

    private void OnClanesButtonPressed()
    {
        // VB6 imgClanes_Click: sends WriteRequestGuildLeaderInfo
        _tcp?.SendPacket(ClientPackets.WriteGuildInfo());
    }

    private void OnCrearCuentaPressed()
    {
        _state.CurrentScreen = Screen.AccountCreate;
        HandleScreenChange(Screen.AccountCreate);
        _lastScreen = Screen.AccountCreate;
    }

    private void OnAccountCreateBack()
    {
        // Disconnect if we connected for account creation
        _tcp?.Dispose();
        _tcp = null;
        _packetHandler = null;
        _connecting = false;
        if (_accountCreateScreen != null) _accountCreateScreen.SuccessTimer = 0;

        _state.CurrentScreen = Screen.Login;
        HandleScreenChange(Screen.Login);
        _lastScreen = Screen.Login;
    }

    private void OnWindowModeChosen(bool windowed)
    {
        _state.Config.Fullscreen = !windowed;
        if (!windowed)
            _state.Config.AspectRatioMode = 1;

        if (!windowed)
            EnterFullscreen();
        else
            ExitFullscreen();

        _state.Config.Save(_dataPath);
        _dialogManager?.HideWindowModeDialog();

        if (_loginForm != null)
            _loginForm.ShowForm();

        CallDeferred(MethodName.FocusAccountInput);
    }

    private async Task ConnectAndLogin(string account, string password)
    {
        try
        {
            GD.Print($"[MAIN] Connecting to {ServerHost}:{ServerPort}...");
            await _tcp!.ConnectAsync(ServerHost, ServerPort);
            _connecting = false;
            if (_loginForm != null) _loginForm.Connecting = false;
            GD.Print("[MAIN] Connected! Sending login...");

            if (_loginForm?.StatusLabel != null) _loginForm.StatusLabel.Text = "Enviando login...";

            await Task.Delay(100);
            _tcp.SendPacket(ClientPackets.WriteHardwareCheck());

            await Task.Delay(50);
            _tcp.SendPacket(ClientPackets.WriteAccountLogin(account, password));
            GD.Print("[MAIN] Sent: AccountLogin (binary)");

            _ = Task.Run(async () =>
            {
                await Task.Delay(8000);
                if (_state.CurrentScreen == Screen.Login && !_connecting
                    && _loginForm?.StatusLabel?.Text == "Enviando login...")
                {
                    CallDeferred(nameof(LoginTimeout));
                }
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Connection failed: {ex}");
            _connecting = false;
            if (_loginForm != null) _loginForm.Connecting = false;
            _tcp?.Dispose();
            _tcp = null;
            _inputHandler = null;

            _dialogManager?.ShowMensaje(FriendlyConnectionError(ex), GetViewportRect().Size);
            if (_loginForm?.ConnectButton != null)
                _loginForm.ConnectButton.Disabled = false;
        }
    }

    private void LoginTimeout()
    {
        GD.PrintErr("[MAIN] Login timeout — server did not respond");
        _tcp?.Dispose();
        _tcp = null;
        _inputHandler = null;
        _loginForm?.LoginTimeout();
    }

    private async Task ConnectAndCreateAccount(string account, string password, string pin)
    {
        try
        {
            // Dispose any existing connection
            _tcp?.Dispose();

            _tcp = new AoTcpClient();
            _packetHandler = new PacketHandler(_state);
            _packetHandler.OnMapLoad = () => { _soundManager?.StopAllSfx(); LoadCurrentMap(); };
            _connecting = true;

            GD.Print($"[MAIN] Connecting for account creation...");
            await _tcp.ConnectAsync(ServerHost, ServerPort);
            _connecting = false;
            GD.Print("[MAIN] Connected! Sending CreateAccount...");

            await Task.Delay(100);
            _tcp.SendPacket(ClientPackets.WriteHardwareCheck());

            await Task.Delay(50);
            _tcp.SendPacket(ClientPackets.WriteCreateAccount(account, password, pin));
            GD.Print("[MAIN] Sent: CreateAccount (binary)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Account creation connection failed: {ex}");
            _connecting = false;
            _tcp?.Dispose();
            _tcp = null;
            _accountCreateScreen?.ShowError(FriendlyConnectionError(ex));
            _accountCreateScreen?.EnableCreateButton();
        }
    }

    /// <summary>
    /// Load patch PCK files from the exe directory.
    /// Files named "patch.pck" or "patch_*.pck" override base resources,
    /// allowing delta updates without replacing the full game.
    /// Patches are loaded in alphabetical order so later patches win.
    /// </summary>
    private void LoadPatchPacks(string exeDir)
    {
        // Single patch file
        string singlePatch = System.IO.Path.Combine(exeDir, "patch.pck");
        if (System.IO.File.Exists(singlePatch))
        {
            bool ok = ProjectSettings.LoadResourcePack(singlePatch, true);
            GD.Print($"[PATCH] patch.pck: {(ok ? "loaded" : "FAILED")}");
        }

        // Numbered patches: patch_001.pck, patch_002.pck, etc.
        string[] patchFiles;
        try
        {
            patchFiles = System.IO.Directory.GetFiles(exeDir, "patch_*.pck");
            System.Array.Sort(patchFiles);
        }
        catch { return; }

        foreach (string pf in patchFiles)
        {
            bool ok = ProjectSettings.LoadResourcePack(pf, true);
            string name = System.IO.Path.GetFileName(pf);
            GD.Print($"[PATCH] {name}: {(ok ? "loaded" : "FAILED")}");
        }
    }
}

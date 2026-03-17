using Godot;
using System;
using ArgentumNextgen.Network;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Game;

/// <summary>
/// Handles keyboard and mouse input routing for the game screen.
/// Escape dialog closing, Enter/chat, F-key panels, mouse clicks on viewport.
/// Extracted from Main._Input().
/// </summary>
public class InputRouter
{
    private readonly GameState _state;

    // Panel references for Escape closing
    private KeyBindPanel? _keyBindPanel;
    private StatsPanel? _statsPanel;
    private OptionsPanel? _optionsPanel;
    private MacroPanel? _macroPanel;
    private QuestPanel? _questPanel;
    private TrainerPanel? _trainerPanel;
    private ContextMenu? _contextMenu;

    // Chat system reference
    private ChatSystem? _chatSystem;

    // Track double-click to avoid sending LC on the release after a dbl-click
    private bool _dblClickHandled;

    /// <summary>Callback to send a packet via TCP.</summary>
    public Action<byte[]>? SendPacket;

    /// <summary>Callback to close drop dialog.</summary>
    public Action? CloseDropDialog;

    /// <summary>Callback to show/hide escape menu.</summary>
    public Action? ShowEscapeMenu;
    public Action? HideEscapeMenu;

    /// <summary>Callback to disconnect back to login.</summary>
    public Action<string>? HandleDisconnect;

    /// <summary>InputHandler reference for mouse click routing.</summary>
    public InputHandler? InputHandler;

    /// <summary>Data path for config save.</summary>
    public string DataPath = "";

    /// <summary>Callbacks for fullscreen toggle (wired by Main).</summary>
    public Action? OnEnterFullscreen;
    public Action? OnExitFullscreen;

    public InputRouter(GameState state)
    {
        _state = state;
    }

    /// <summary>Bind panel references needed for Escape key handling.</summary>
    public void BindPanels(
        KeyBindPanel? keyBindPanel, StatsPanel? statsPanel,
        OptionsPanel? optionsPanel, MacroPanel? macroPanel,
        QuestPanel? questPanel, TrainerPanel? trainerPanel,
        ContextMenu? contextMenu, ChatSystem? chatSystem)
    {
        _keyBindPanel = keyBindPanel;
        _statsPanel = statsPanel;
        _optionsPanel = optionsPanel;
        _macroPanel = macroPanel;
        _questPanel = questPanel;
        _trainerPanel = trainerPanel;
        _contextMenu = contextMenu;
        _chatSystem = chatSystem;
    }

    /// <summary>
    /// Handle input event on the game screen. Returns true if the event was consumed.
    /// </summary>
    public bool HandleGameInput(InputEvent @event, Viewport viewport, SceneTree tree)
    {
        if (_chatSystem == null) return false;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Escape: close dialogs, chat input, or show escape menu
            if (key.Keycode == Key.Escape)
            {
                return HandleEscape(viewport);
            }

            // Block game keys when any form is open or a text field has focus
            var focusOwner = viewport.GuiGetFocusOwner();
            bool uiTextFocused = focusOwner is LineEdit && focusOwner != _chatSystem.ChatInput;
            if (_state.AnyFormOpen || uiTextFocused)
            {
                return false;
            }

            // Enter: open chat input, or submit if already open
            if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
            {
                return HandleEnter(viewport);
            }

            // Numpad 0-8: chat mode switching (VB6: HablaNumerico)
            if (!_state.ChatActive && key.Keycode >= Key.Kp0 && key.Keycode <= Key.Kp8)
            {
                int mode = (int)key.Keycode - (int)Key.Kp0;
                _chatSystem.SetChatMode(mode);
                viewport.SetInputAsHandled();
                return true;
            }

            // F5: toggle stats panel
            if (key.Keycode == Key.F5 && !_state.ChatActive)
            {
                if (_statsPanel != null)
                {
                    if (_state.StatsPanelOpen) _statsPanel.Close();
                    else _statsPanel.Open();
                }
                viewport.SetInputAsHandled();
                return true;
            }

            // F9: toggle macro panel
            if (key.Keycode == Key.F9 && !_state.ChatActive)
            {
                if (_macroPanel != null)
                {
                    if (_state.MacroPanelOpen) _macroPanel.Close();
                    else _macroPanel.Open();
                }
                viewport.SetInputAsHandled();
                return true;
            }

            // F10: toggle options panel
            if (key.Keycode == Key.F10 && !_state.ChatActive)
            {
                if (_optionsPanel != null)
                {
                    if (_state.OptionsPanelOpen) _optionsPanel.Close();
                    else _optionsPanel.Open();
                }
                viewport.SetInputAsHandled();
                return true;
            }

            // F12: toggle fullscreen/windowed
            if (key.Keycode == Key.F12)
            {
                HandleF12Toggle(tree, viewport);
                return true;
            }
        }

        // VB6 Form_KeyUp: attack fires on key RELEASE, not key down
        InputHandler?.HandleInputEvent(@event);

        // Block mouse clicks on game world when any modal panel is open
        if (_state.Comerciando || _state.Banqueando || _state.BovedaAbierta
            || _state.Trading
            || _state.MacroPanelOpen || _state.OptionsPanelOpen || _state.KeyBindPanelOpen
            || _state.ShowTravelPanel)
        {
            if (@event is InputEventMouseButton) return false;
        }

        // Mouse clicks on the game viewport area
        if (@event is InputEventMouseButton mb)
        {
            HandleMouseClick(mb, viewport);
        }

        return false;
    }

    private bool HandleEscape(Viewport viewport)
    {
        if (_state.KeyBindPanelOpen)
        {
            _keyBindPanel?.Close();
            _state.KeyBindPanelOpen = false;
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.StatsPanelOpen)
        {
            _statsPanel?.Close();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.OptionsPanelOpen)
        {
            _optionsPanel?.Close();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.MacroPanelOpen)
        {
            _macroPanel?.Close();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_questPanel != null && _questPanel.Visible)
        {
            _questPanel.Hide();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_trainerPanel != null && _trainerPanel.Visible)
        {
            _trainerPanel.Hide();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.Comerciando)
        {
            SendPacket?.Invoke(ClientPackets.WriteCommerceClose());
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.Trading)
        {
            SendPacket?.Invoke(ClientPackets.WriteTradeCancel());
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.Banqueando || _state.BovedaAbierta)
        {
            SendPacket?.Invoke(ClientPackets.WriteBankClose());
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.DropDialogOpen)
        {
            CloseDropDialog?.Invoke();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.EscapeMenuOpen)
        {
            HideEscapeMenu?.Invoke();
            viewport.SetInputAsHandled();
            return true;
        }
        if (_state.ChatActive)
        {
            _chatSystem?.HideChat();
            viewport.SetInputAsHandled();
            return true;
        }
        // No dialog open -> show escape menu
        ShowEscapeMenu?.Invoke();
        viewport.SetInputAsHandled();
        return true;
    }

    private bool HandleEnter(Viewport viewport)
    {
        if (_chatSystem == null) return false;

        if (_state.ChatActive && _chatSystem.IsChatInputVisible)
        {
            // Chat already open -> submit (fallback if TextSubmitted doesn't fire)
            _chatSystem.OnChatSubmitted(_chatSystem.ChatInput!.Text);
            viewport.SetInputAsHandled();
            return true;
        }
        else if (!_chatSystem.IsChatInputVisible)
        {
            // Open chat input
            _chatSystem.ShowChat();
            viewport.SetInputAsHandled();
            return true;
        }
        return false;
    }

    private void HandleF12Toggle(SceneTree tree, Viewport viewport)
    {
        bool goFullscreen = DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen;
        if (goFullscreen)
            OnEnterFullscreen?.Invoke();
        else
            OnExitFullscreen?.Invoke();
        _state.Config.Fullscreen = goFullscreen;
        _state.Config.Save(DataPath);
        viewport.SetInputAsHandled();
    }

    private void HandleMouseClick(InputEventMouseButton mb, Viewport viewport)
    {
        // Translate click position relative to the game viewport.
        // SubViewportContainer is at (LeftMargin, TopMargin).
        float clickX = mb.Position.X - ResolutionManager.LeftMargin;
        float clickY = mb.Position.Y - ResolutionManager.TopMargin;

        // Only handle clicks within the game viewport area
        if (clickX < 0 || clickX >= ResolutionManager.ViewportW || clickY < 0 || clickY >= ResolutionManager.ViewportH) return;

        var viewPos = new Vector2(clickX, clickY);

        // Close context menu on any left-click in viewport
        if (mb.Pressed && mb.ButtonIndex == MouseButton.Left && _contextMenu != null && _contextMenu.IsOpen)
        {
            _contextMenu.CloseMenu();
        }

        // Right-click: send packet to server (VB6 behavior — no context menu)
        if (mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            InputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
            viewport.SetInputAsHandled();
            return;
        }

        // On PRESS: handle double-click, shift+click (GM teleport)
        if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.DoubleClick)
            {
                InputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
                _dblClickHandled = true;
                viewport.SetInputAsHandled();
                return;
            }

            if (mb.ShiftPressed && _state.Privileges >= 1)
            {
                InputHandler?.HandleGmTeleport(viewPos, _state.UserPosX, _state.UserPosY, _state.CurrentMap);
                _dblClickHandled = true;
                viewport.SetInputAsHandled();
                return;
            }
        }

        // On release: handle single clicks
        if (!mb.Pressed)
        {
            // Skip the release after a double-click or shift+click
            if (_dblClickHandled)
            {
                _dblClickHandled = false;
                return;
            }

            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (_state.UsingSkill > 0)
                {
                    InputHandler?.HandleSpellClick(viewPos, _state.UserPosX, _state.UserPosY);
                    Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                }
                else
                {
                    InputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                }
            }
            // Right-click release: no action (press already handled above)
        }
    }
}

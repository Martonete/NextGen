using Godot;
using System;
using ArgentumNextgen.Network;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Game;

/// <summary>
/// Chat input/output, filtering, tabs. Extracted from Main.cs.
/// </summary>
public class ChatSystem
{
    private RichTextLabel? _console;
    private LineEdit? _chatInput;
    private HBoxContainer? _chatTabBar;
    private Button[] _chatTabButtons = new Button[7];

    private readonly GameState _state;

    /// <summary>Callback for slash commands that Main needs to handle.</summary>
    public Action<string>? OnSlashCommand;

    /// <summary>Callback to send a packet via TCP.</summary>
    public Action<byte[]>? SendPacket;

    /// <summary>Callback to toggle work macro.</summary>
    public Action? OnWorkMacroToggle;

    /// <summary>Callback to toggle spell macro.</summary>
    public Action? OnSpellMacroToggle;

    /// <summary>Callback for /PASSWD.</summary>
    public Action? OnPasswdCommand;

    /// <summary>Callback to toggle GM panel.</summary>
    public Action? OnGmPanelToggle;

    /// <summary>Callback to open the local item search panel.</summary>
    public Action<string>? OnItemSearchCommand;

    /// <summary>Callback for voluntary logout (/SALIR).</summary>
    public Action? OnLogoutCommand;

    /// <summary>Callback to toggle SOS panel.</summary>
    public Action? OnSosPanelToggle;

    public bool IsChatInputVisible => _chatInput != null && _chatInput.Visible;
    public LineEdit? ChatInput => _chatInput;

    /// <summary>For party panel integration.</summary>
    public PartyPanel? PartyPanel { get; set; }

    public ChatSystem(GameState state)
    {
        _state = state;
    }

    /// <summary>Bind to existing console and chat input nodes.</summary>
    public void BindNodes(RichTextLabel console, LineEdit chatInput)
    {
        _console = console;
        _chatInput = chatInput;
    }

    /// <summary>Create chat tab filter bar above the console.</summary>
    public void CreateChatTabs(Control gameUI)
    {
        _chatTabBar = new HBoxContainer();
        _chatTabBar.Position = new Vector2(10, 4);
        _chatTabBar.Size = new Vector2(547, 18);
        _chatTabBar.AddThemeConstantOverride("separation", 2);
        gameUI.AddChild(_chatTabBar);

        string[] tabLabels = { "Todo", "Global", "Party", "Clan", "Whisper", "Combat", "Sistema" };
        int[] tabFilters = { -1, (int)ChatType.Global, (int)ChatType.Party, (int)ChatType.Clan, (int)ChatType.Whisper, (int)ChatType.Combat, (int)ChatType.System };
        for (int i = 0; i < tabLabels.Length; i++)
        {
            int filterVal = tabFilters[i];
            var tabBtn = new Button();
            tabBtn.Text = tabLabels[i];
            tabBtn.CustomMinimumSize = new Vector2(0, 16);
            tabBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            tabBtn.AddThemeFontSizeOverride("font_size", 8);
            tabBtn.Pressed += () => SetChatFilter(filterVal);
            _chatTabBar.AddChild(tabBtn);
            _chatTabButtons[i] = tabBtn;
        }
        UpdateChatTabHighlight();
    }

    public void OnChatSubmitted(string text)
    {
        if (text.Contains('\n') || text.Contains('\r'))
        {
            foreach (string line in text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                OnChatSubmitted(line);
            }
            return;
        }

        text = text.Trim();

        if (text.StartsWith("/"))
        {
            if (text.Equals("/PING", StringComparison.OrdinalIgnoreCase))
            {
                _state.PingSentMs = Time.GetTicksMsec();
            }
            else if (text.Equals("/MACRO", StringComparison.OrdinalIgnoreCase))
            {
                OnWorkMacroToggle?.Invoke();
                HideChat();
                return;
            }
            else if (text.Equals("/MACROSP", StringComparison.OrdinalIgnoreCase))
            {
                OnSpellMacroToggle?.Invoke();
                HideChat();
                return;
            }
            else if (text.Equals("/PASSWD", StringComparison.OrdinalIgnoreCase))
            {
                OnPasswdCommand?.Invoke();
                HideChat();
                return;
            }
            else if (text.Equals("/PANELGM", StringComparison.OrdinalIgnoreCase))
            {
                OnGmPanelToggle?.Invoke();
                HideChat();
                return;
            }
            else if (TryReadCommandArgument(text, "/BUSCARITEMS", out string itemQuery)
                || TryReadCommandArgument(text, "/BUSCARITEM", out itemQuery)
                || TryReadCommandArgument(text, "/ITEMS", out itemQuery))
            {
                OnItemSearchCommand?.Invoke(itemQuery);
                HideChat();
                return;
            }
            else if (text.Equals("/SOSPANEL", StringComparison.OrdinalIgnoreCase))
            {
                OnSosPanelToggle?.Invoke();
                HideChat();
                return;
            }
            else if (text.Equals("/SALIR", StringComparison.OrdinalIgnoreCase))
            {
                OnLogoutCommand?.Invoke();
                HideChat();
                return;
            }
            else if (text.Equals("/AURAS", StringComparison.OrdinalIgnoreCase))
            {
                OnSlashCommand?.Invoke(text);
                HideChat();
                return;
            }
            else if (text.Equals("/MAPA", StringComparison.OrdinalIgnoreCase))
            {
                OnSlashCommand?.Invoke(text);
                HideChat();
                return;
            }
            SendPacket?.Invoke(ClientPackets.WriteTalk(text));
        }
        else if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0)
        {
            byte[] chatPkt = _state.ChatModePrefix switch
            {
                "-" => ClientPackets.WriteYell(text),
                "\\" => ClientPackets.WriteWhisper("", text),
                _ => ClientPackets.WriteTalk(text),
            };
            SendPacket?.Invoke(chatPkt);
        }
        else
        {
            SendPacket?.Invoke(ClientPackets.WriteTalk(" "));
        }

        HideChat();
    }

    private static bool TryReadCommandArgument(string text, string command, out string argument)
    {
        argument = "";
        if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Length == command.Length)
            return true;

        if (!char.IsWhiteSpace(text[command.Length]))
            return false;

        argument = text[(command.Length + 1)..].Trim();
        return true;
    }

    public void SetChatMode(int mode)
    {
        _state.ChatMode = mode;
        switch (mode)
        {
            case 0: _state.ChatModePrefix = ";"; break;
            case 1: _state.ChatModePrefix = "-"; break;
            case 2: _state.ChatModePrefix = ";/cmsg "; break;
            case 3: _state.ChatModePrefix = ";/GLOBAL "; break;
            case 4: _state.ChatModePrefix = ";/pmsg "; break;
            case 5: _state.ChatModePrefix = ";/FMSG "; break;
            case 6: _state.ChatModePrefix = ";/gmsg "; break;
            case 7:
                _state.ChatModePrefix = $"\\{_state.WhisperTarget}@";
                break;
            default: _state.ChatModePrefix = ";"; break;
        }
        string[] modeNames = { "Normal", "Gritar", "Clan", "Global", "Party", "Facción", "GM", "Privado" };
        string modeName = mode >= 0 && mode < modeNames.Length ? modeNames[mode] : "Normal";
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Modo de habla: {modeName}",
            Color = "FFFFFF"
        });
    }

    /// <summary>Drain chat message queue, store in history, display in console.</summary>
    public void UpdateConsoleMessages()
    {
        if (_console == null) return;

        bool hadNew = false;
        int processed = 0;
        while (_state.ChatMessages.Count > 0 && processed < 50)
        {
            processed++;
            var msg = _state.ChatMessages.Dequeue();
            PartyPanel?.TryParsePartyMessage(msg.Text);
            _state.ChatHistory.Add(msg);
            if (_state.ChatHistory.Count > GameState.MaxChatHistory)
                _state.ChatHistory.RemoveAt(0);

            if (PassesChatFilter(msg, _state.ActiveChatFilter))
            {
                string safeText = EscapeBbcode(msg.Text);
                _console.AppendText($"[b][color=#{msg.Color}]{safeText}[/color][/b]\n");
            }
            hadNew = true;
        }

        if (_state.ChatFilterDirty)
        {
            _state.ChatFilterDirty = false;
            RebuildConsoleFromHistory();
            hadNew = true;
        }

        if (hadNew)
        {
            _console.ScrollFollowing = true;
            var vbar = _console.GetVScrollBar();
            vbar.Value = vbar.MaxValue;
        }
    }

    public void ClearConsole()
    {
        _console?.Clear();
    }

    /// <summary>No-op kept for API compat.</summary>
    public void ExpandConsole() { }

    public void ShowChat()
    {
        if (_chatInput == null) return;
        _chatInput.Visible = true;
        _chatInput.GrabFocus();
        _chatInput.SelectAll();
        _state.ChatActive = true;
    }

    public void HideChat()
    {
        if (_chatInput == null) return;
        _chatInput.Text = "";
        _chatInput.Visible = false;
        _chatInput.ReleaseFocus();
        _state.ChatActive = false;
    }

    public void SetChatFilter(int filter)
    {
        if (_state.ActiveChatFilter == filter) return;
        _state.ActiveChatFilter = filter;
        _state.ChatFilterDirty = true;
        UpdateChatTabHighlight();
    }

    public void UpdateChatTabHighlight()
    {
        int[] tabFilters = { -1, (int)ChatType.Global, (int)ChatType.Party, (int)ChatType.Clan, (int)ChatType.Whisper, (int)ChatType.Combat, (int)ChatType.System };
        var activeColor = new Color(0.9f, 0.8f, 0.5f);
        var normalColor = new Color(0.7f, 0.7f, 0.7f);
        for (int i = 0; i < _chatTabButtons.Length; i++)
        {
            if (_chatTabButtons[i] != null)
            {
                bool active = tabFilters[i] == _state.ActiveChatFilter;
                _chatTabButtons[i].AddThemeColorOverride("font_color", active ? activeColor : normalColor);
            }
        }
    }

    private static string EscapeBbcode(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("[", "[lb]");
    }

    private static bool PassesChatFilter(ChatMessage msg, int filter)
    {
        if (filter < 0) return true;
        return (int)msg.Type == filter;
    }

    private void RebuildConsoleFromHistory()
    {
        if (_console == null) return;
        _console.Clear();
        foreach (var msg in _state.ChatHistory)
        {
            if (PassesChatFilter(msg, _state.ActiveChatFilter))
            {
                string safeText = EscapeBbcode(msg.Text);
                _console.AppendText($"[b][color=#{msg.Color}]{safeText}[/color][/b]\n");
            }
        }
    }
}

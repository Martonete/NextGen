using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmPanelGM — 8-tab GM administration panel.
/// Tabs: User Info, Teleport, Spawn, Moderation, Messages, Server, Map, Logs.
/// All actions send slash commands via WriteTalk.
/// Toggle with /PANELGM command or GM sidebar button.
/// </summary>
public partial class GmPanel : PanelContainer
{
    private const int PanelW = 500;
    private const int PanelH = 450;
    private const int TitleBarH = 28;
    private const int TabBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Tab system
    private int _activeTab;
    private Control?[] _tabs = new Control?[8];
    private Button?[] _tabButtons = new Button?[8];

    // Tab 0: User Info
    private LineEdit? _userSearchEdit;
    private Label? _userInfoLabel;

    // Tab 1: Teleport
    private LineEdit? _teleMapEdit;
    private LineEdit? _teleXEdit;
    private LineEdit? _teleYEdit;
    private LineEdit? _telePlayerEdit;

    // Tab 2: Spawn
    private LineEdit? _spawnNpcIndexEdit;
    private LineEdit? _spawnNpcCountEdit;
    private LineEdit? _spawnItemIndexEdit;

    // Tab 3: Moderation
    private LineEdit? _modPlayerEdit;

    // Tab 4: Messages
    private TextEdit? _msgTextEdit;

    // Tab 5: Server (no extra state needed)

    // Tab 6: Map
    private LineEdit? _mapTriggerXEdit;
    private LineEdit? _mapTriggerYEdit;
    private LineEdit? _mapTriggerValueEdit;

    // Tab 7: Logs
    private TextEdit? _logTextArea;
    private readonly List<string> _logEntries = new();

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        style.BorderColor = new Color(0.6f, 0.2f, 0.2f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Panel GM";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += Close;
        titleBar.AddChild(closeBtn);

        // Tab bar (two rows of 4 tabs for space)
        var tabRow1 = new HBoxContainer();
        tabRow1.CustomMinimumSize = new Vector2(0, TabBarH);
        tabRow1.AddThemeConstantOverride("separation", 2);
        root.AddChild(tabRow1);

        var tabRow2 = new HBoxContainer();
        tabRow2.CustomMinimumSize = new Vector2(0, TabBarH);
        tabRow2.AddThemeConstantOverride("separation", 2);
        root.AddChild(tabRow2);

        string[] tabNames = { "Usuario", "Teleport", "Spawn", "Moderacion", "Mensajes", "Servidor", "Mapa", "Logs" };
        for (int i = 0; i < 8; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = tabNames[i];
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.CustomMinimumSize = new Vector2(0, TabBarH);
            btn.AddThemeFontSizeOverride("font_size", 10);
            btn.Pressed += () => SetTab(idx);
            _tabButtons[i] = btn;
            if (i < 4) tabRow1.AddChild(btn);
            else tabRow2.AddChild(btn);
        }

        // Content area
        var contentArea = new Control();
        contentArea.SizeFlagsVertical = SizeFlags.ExpandFill;
        contentArea.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentArea.CustomMinimumSize = new Vector2(PanelW - 8, PanelH - TitleBarH - TabBarH * 2 - 12);
        root.AddChild(contentArea);

        _tabs[0] = BuildUserInfoTab();
        _tabs[1] = BuildTeleportTab();
        _tabs[2] = BuildSpawnTab();
        _tabs[3] = BuildModerationTab();
        _tabs[4] = BuildMessagesTab();
        _tabs[5] = BuildServerTab();
        _tabs[6] = BuildMapTab();
        _tabs[7] = BuildLogsTab();

        for (int i = 0; i < 8; i++)
            contentArea.AddChild(_tabs[i]!);

        SetTab(0);
    }

    private void SetTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < 8; i++)
        {
            if (_tabs[i] != null) _tabs[i]!.Visible = i == index;
        }
        var normalColor = new Color(0.7f, 0.7f, 0.7f);
        var activeColor = new Color(1f, 0.4f, 0.4f);
        for (int i = 0; i < 8; i++)
            _tabButtons[i]?.AddThemeColorOverride("font_color", i == index ? activeColor : normalColor);
    }

    // ── Tab 0: User Info ──────────────────────────────────────

    private Control BuildUserInfoTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Buscar jugador:");
        var searchRow = new HBoxContainer();
        vbox.AddChild(searchRow);

        _userSearchEdit = new LineEdit();
        _userSearchEdit.PlaceholderText = "Nombre del jugador";
        _userSearchEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        searchRow.AddChild(_userSearchEdit);

        var searchBtn = new Button();
        searchBtn.Text = "Buscar";
        searchBtn.Pressed += () =>
        {
            string name = _userSearchEdit?.Text.Trim() ?? "";
            if (name.Length > 0)
            {
                SendGmCommand($"/MIRAR {name}");
                AddLog($"Searched user: {name}");
            }
        };
        searchRow.AddChild(searchBtn);

        _userInfoLabel = new Label();
        _userInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _userInfoLabel.CustomMinimumSize = new Vector2(PanelW - 32, 100);
        _userInfoLabel.AddThemeFontSizeOverride("font_size", 11);
        _userInfoLabel.AddThemeColorOverride("font_color", Colors.White);
        _userInfoLabel.Text = "Use /MIRAR to view player info in the chat console.";
        vbox.AddChild(_userInfoLabel);

        return scroll;
    }

    // ── Tab 1: Teleport ───────────────────────────────────────

    private Control BuildTeleportTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Teleport a coordenadas:");
        var coordRow = new HBoxContainer();
        coordRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(coordRow);

        AddLabel(coordRow, "Mapa:");
        _teleMapEdit = new LineEdit();
        _teleMapEdit.CustomMinimumSize = new Vector2(50, 0);
        _teleMapEdit.PlaceholderText = "1";
        coordRow.AddChild(_teleMapEdit);

        AddLabel(coordRow, "X:");
        _teleXEdit = new LineEdit();
        _teleXEdit.CustomMinimumSize = new Vector2(40, 0);
        _teleXEdit.PlaceholderText = "50";
        coordRow.AddChild(_teleXEdit);

        AddLabel(coordRow, "Y:");
        _teleYEdit = new LineEdit();
        _teleYEdit.CustomMinimumSize = new Vector2(40, 0);
        _teleYEdit.PlaceholderText = "50";
        coordRow.AddChild(_teleYEdit);

        var teleCoordsBtn = new Button();
        teleCoordsBtn.Text = "Teleport";
        teleCoordsBtn.Pressed += () =>
        {
            string map = _teleMapEdit?.Text.Trim() ?? "1";
            string x = _teleXEdit?.Text.Trim() ?? "50";
            string y = _teleYEdit?.Text.Trim() ?? "50";
            SendGmCommand($"/TELEP YO {map} {x} {y}");
            AddLog($"Teleported self to Map {map} ({x},{y})");
        };
        vbox.AddChild(teleCoordsBtn);

        AddSeparator(vbox);

        AddLabel(vbox, "Teleport a jugador:");
        var telePlayerRow = new HBoxContainer();
        vbox.AddChild(telePlayerRow);

        _telePlayerEdit = new LineEdit();
        _telePlayerEdit.PlaceholderText = "Nombre del jugador";
        _telePlayerEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        telePlayerRow.AddChild(_telePlayerEdit);

        var teleToBtn = new Button();
        teleToBtn.Text = "Ir a el";
        teleToBtn.Pressed += () =>
        {
            string name = _telePlayerEdit?.Text.Trim() ?? "";
            if (name.Length > 0)
            {
                SendGmCommand($"/IRA {name}");
                AddLog($"Teleported to player: {name}");
            }
        };
        telePlayerRow.AddChild(teleToBtn);

        var summonBtn = new Button();
        summonBtn.Text = "Traerlo";
        summonBtn.Pressed += () =>
        {
            string name = _telePlayerEdit?.Text.Trim() ?? "";
            if (name.Length > 0)
            {
                SendGmCommand($"/SUM {name}");
                AddLog($"Summoned player: {name}");
            }
        };
        vbox.AddChild(summonBtn);

        return scroll;
    }

    // ── Tab 2: Spawn ──────────────────────────────────────────

    private Control BuildSpawnTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Spawn NPC:");
        var npcRow = new HBoxContainer();
        npcRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(npcRow);

        AddLabel(npcRow, "Index:");
        _spawnNpcIndexEdit = new LineEdit();
        _spawnNpcIndexEdit.CustomMinimumSize = new Vector2(60, 0);
        _spawnNpcIndexEdit.PlaceholderText = "1";
        npcRow.AddChild(_spawnNpcIndexEdit);

        AddLabel(npcRow, "Cantidad:");
        _spawnNpcCountEdit = new LineEdit();
        _spawnNpcCountEdit.CustomMinimumSize = new Vector2(40, 0);
        _spawnNpcCountEdit.PlaceholderText = "1";
        _spawnNpcCountEdit.Text = "1";
        npcRow.AddChild(_spawnNpcCountEdit);

        var spawnNpcBtn = new Button();
        spawnNpcBtn.Text = "Spawn NPC";
        spawnNpcBtn.Pressed += () =>
        {
            string idx = _spawnNpcIndexEdit?.Text.Trim() ?? "1";
            string count = _spawnNpcCountEdit?.Text.Trim() ?? "1";
            SendGmCommand($"/ACC {idx} {count}");
            AddLog($"Spawned NPC {idx} x{count}");
        };
        vbox.AddChild(spawnNpcBtn);

        AddSeparator(vbox);

        AddLabel(vbox, "Spawn Item:");
        var itemRow = new HBoxContainer();
        itemRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(itemRow);

        AddLabel(itemRow, "Index:");
        _spawnItemIndexEdit = new LineEdit();
        _spawnItemIndexEdit.CustomMinimumSize = new Vector2(60, 0);
        _spawnItemIndexEdit.PlaceholderText = "1";
        itemRow.AddChild(_spawnItemIndexEdit);

        var spawnItemBtn = new Button();
        spawnItemBtn.Text = "Crear Item";
        spawnItemBtn.Pressed += () =>
        {
            string idx = _spawnItemIndexEdit?.Text.Trim() ?? "1";
            SendGmCommand($"/CI {idx}");
            AddLog($"Created item {idx}");
        };
        vbox.AddChild(spawnItemBtn);

        // Spawn list button
        var spawnListBtn = new Button();
        spawnListBtn.Text = "Ver lista de NPCs";
        spawnListBtn.Pressed += OnOpenSpawnList;
        vbox.AddChild(spawnListBtn);

        return scroll;
    }

    /// <summary>Event raised when the spawn list button is pressed.</summary>
    public Action? OnOpenSpawnListRequested;

    private void OnOpenSpawnList()
    {
        OnOpenSpawnListRequested?.Invoke();
    }

    // ── Tab 3: Moderation ─────────────────────────────────────

    private Control BuildModerationTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Jugador objetivo:");
        _modPlayerEdit = new LineEdit();
        _modPlayerEdit.PlaceholderText = "Nombre del jugador";
        vbox.AddChild(_modPlayerEdit);

        string[] actions = { "Kick", "Ban", "Jail", "Mute", "Unmute", "Revivir", "Curar" };
        string[] commands = { "/KICK", "/BAN", "/CARCEL", "/SILENCIAR", "/DESILENCIAR", "/REVIVIR", "/CURAR" };

        var grid = new GridContainer();
        grid.Columns = 3;
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        vbox.AddChild(grid);

        for (int i = 0; i < actions.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = actions[i];
            btn.CustomMinimumSize = new Vector2(100, 28);
            btn.Pressed += () =>
            {
                string name = _modPlayerEdit?.Text.Trim() ?? "";
                if (name.Length > 0)
                {
                    SendGmCommand($"{commands[idx]} {name}");
                    AddLog($"{actions[idx]}: {name}");
                }
            };
            grid.AddChild(btn);
        }

        return scroll;
    }

    // ── Tab 4: Messages ───────────────────────────────────────

    private Control BuildMessagesTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Mensaje:");
        _msgTextEdit = new TextEdit();
        _msgTextEdit.CustomMinimumSize = new Vector2(PanelW - 32, 80);
        _msgTextEdit.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(_msgTextEdit);

        string[] msgTypes = { "Global", "Faccion", "Ciudadano", "Criminal" };
        string[] msgCmds = { "/GMSG", "/FMSG", "/RMSG", "/CMSG" };

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(btnRow);

        for (int i = 0; i < msgTypes.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = $"Enviar {msgTypes[i]}";
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.Pressed += () =>
            {
                string msg = _msgTextEdit?.Text.Trim() ?? "";
                if (msg.Length > 0)
                {
                    SendGmCommand($"{msgCmds[idx]} {msg}");
                    AddLog($"Sent {msgTypes[idx]} message");
                    if (_msgTextEdit != null) _msgTextEdit.Text = "";
                }
            };
            btnRow.AddChild(btn);
        }

        return scroll;
    }

    // ── Tab 5: Server ─────────────────────────────────────────

    private Control BuildServerTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Acciones del servidor:");

        var btnSaveAll = new Button();
        btnSaveAll.Text = "Guardar Todo";
        btnSaveAll.Pressed += () => { SendGmCommand("/GRABAR"); AddLog("Requested save all"); };
        vbox.AddChild(btnSaveAll);

        var btnOnline = new Button();
        btnOnline.Text = "Ver Online";
        btnOnline.Pressed += () => { SendGmCommand("/ONLINE"); AddLog("Requested online list"); };
        vbox.AddChild(btnOnline);

        var btnReload = new Button();
        btnReload.Text = "Recargar NPCs";
        btnReload.Pressed += () => { SendGmCommand("/RELOADNPCS"); AddLog("Requested NPC reload"); };
        vbox.AddChild(btnReload);

        var btnReloadSpells = new Button();
        btnReloadSpells.Text = "Recargar Hechizos";
        btnReloadSpells.Pressed += () => { SendGmCommand("/RELOADSPELLS"); AddLog("Requested spell reload"); };
        vbox.AddChild(btnReloadSpells);

        AddSeparator(vbox);

        var btnShutdown = new Button();
        btnShutdown.Text = "Apagar Servidor";
        btnShutdown.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        btnShutdown.Pressed += () => { SendGmCommand("/APAGAR"); AddLog("Requested server shutdown"); };
        vbox.AddChild(btnShutdown);

        return scroll;
    }

    // ── Tab 6: Map ────────────────────────────────────────────

    private Control BuildMapTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        AddLabel(vbox, "Tile (posicion actual o especificada):");

        var coordRow = new HBoxContainer();
        coordRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(coordRow);

        AddLabel(coordRow, "X:");
        _mapTriggerXEdit = new LineEdit();
        _mapTriggerXEdit.CustomMinimumSize = new Vector2(40, 0);
        _mapTriggerXEdit.PlaceholderText = "50";
        coordRow.AddChild(_mapTriggerXEdit);

        AddLabel(coordRow, "Y:");
        _mapTriggerYEdit = new LineEdit();
        _mapTriggerYEdit.CustomMinimumSize = new Vector2(40, 0);
        _mapTriggerYEdit.PlaceholderText = "50";
        coordRow.AddChild(_mapTriggerYEdit);

        AddLabel(coordRow, "Value:");
        _mapTriggerValueEdit = new LineEdit();
        _mapTriggerValueEdit.CustomMinimumSize = new Vector2(40, 0);
        _mapTriggerValueEdit.PlaceholderText = "0";
        coordRow.AddChild(_mapTriggerValueEdit);

        var btnBlock = new Button();
        btnBlock.Text = "Toggle Bloqueado";
        btnBlock.Pressed += () =>
        {
            SendGmCommand("/BLOQ");
            AddLog("Toggled tile block");
        };
        vbox.AddChild(btnBlock);

        var btnTrigger = new Button();
        btnTrigger.Text = "Set Trigger";
        btnTrigger.Pressed += () =>
        {
            string val = _mapTriggerValueEdit?.Text.Trim() ?? "0";
            SendGmCommand($"/TRIGGER {val}");
            AddLog($"Set trigger value: {val}");
        };
        vbox.AddChild(btnTrigger);

        var btnSaveMap = new Button();
        btnSaveMap.Text = "Guardar Mapa";
        btnSaveMap.Pressed += () =>
        {
            SendGmCommand("/GUARDARMAPA");
            AddLog("Saved current map");
        };
        vbox.AddChild(btnSaveMap);

        return scroll;
    }

    // ── Tab 7: Logs ───────────────────────────────────────────

    private Control BuildLogsTab()
    {
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 4);

        AddLabel(vbox, "Log de acciones GM:");

        _logTextArea = new TextEdit();
        _logTextArea.Editable = false;
        _logTextArea.SizeFlagsVertical = SizeFlags.ExpandFill;
        _logTextArea.CustomMinimumSize = new Vector2(PanelW - 32, 200);
        _logTextArea.AddThemeFontSizeOverride("font_size", 10);
        _logTextArea.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        vbox.AddChild(_logTextArea);

        var clearBtn = new Button();
        clearBtn.Text = "Limpiar Log";
        clearBtn.Pressed += () =>
        {
            _logEntries.Clear();
            if (_logTextArea != null) _logTextArea.Text = "";
        };
        vbox.AddChild(clearBtn);

        return vbox;
    }

    // ── Helpers ────────────────────────────────────────────────

    private void SendGmCommand(string command)
    {
        _tcp?.SendPacket(ClientPackets.WriteTalk(command));
    }

    private void AddLog(string entry)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string line = $"[{timestamp}] {entry}";
        _logEntries.Add(line);
        if (_logEntries.Count > 200) _logEntries.RemoveAt(0);
        if (_logTextArea != null)
        {
            _logTextArea.Text += line + "\n";
        }
    }

    private static void AddLabel(Control parent, string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 11);
        lbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
        parent.AddChild(lbl);
    }

    private static void AddSeparator(VBoxContainer parent)
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        parent.AddChild(sep);
    }

    // ── Open / Close ──────────────────────────────────────────

    public void Open()
    {
        Visible = true;
        SetTab(0);
    }

    public void Close()
    {
        Visible = false;
    }

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    // ── Dragging ──────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                    AcceptEvent();
                }
                else if (!mb.Pressed)
                    _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Relative;
            AcceptEvent();
        }
    }
}

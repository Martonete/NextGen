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
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class GmPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Tab system
    private int _activeTab;
    private Control?[] _tabs = new Control?[8];
    private HBoxContainer? _tabBar1;
    private HBoxContainer? _tabBar2;

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

    public GmPanel() : base("Panel GM", new Vector2(500, 450), "v2") { }

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn(0);
        ContentContainer.AddChild(root);

        // Tab bars (two rows of 4 tabs for space)
        string[] tabNames1 = { "Usuario", "Teleport", "Spawn", "Moderacion" };
        string[] tabNames2 = { "Mensajes", "Servidor", "Mapa", "Logs" };

        _tabBar1 = RpgTheme.CreateTabBar(tabNames1, idx => SetTab(idx));
        root.AddChild(_tabBar1);

        _tabBar2 = RpgTheme.CreateTabBar(tabNames2, idx => SetTab(idx + 4));
        root.AddChild(_tabBar2);

        // Content area
        var contentArea = new Control();
        contentArea.SizeFlagsVertical = SizeFlags.ExpandFill;
        contentArea.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentArea.CustomMinimumSize = new Vector2(0, 280);
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
        // Update tab bar styling
        if (index < 4)
        {
            RpgTheme.SetTabBarActive(_tabBar1!, index);
            // Deselect all in second row by setting an out-of-range index
            RpgTheme.SetTabBarActive(_tabBar2!, -1);
        }
        else
        {
            RpgTheme.SetTabBarActive(_tabBar2!, index - 4);
            RpgTheme.SetTabBarActive(_tabBar1!, -1);
        }
    }

    // ── Tab 0: User Info ──────────────────────────────────────

    private Control BuildUserInfoTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Buscar jugador:", 12));
        var searchRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        vbox.AddChild(searchRow);

        _userSearchEdit = RpgTheme.CreateRpgInput("Nombre del jugador");
        searchRow.AddChild(_userSearchEdit);

        var searchBtn = RpgTheme.CreateRpgButton("Buscar", false, 13);
        searchBtn.CustomMinimumSize = new Vector2(90, 30);
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

        _userInfoLabel = RpgTheme.CreateInfoLabel("Use /MIRAR to view player info in the chat console.", 11);
        _userInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _userInfoLabel.CustomMinimumSize = new Vector2(0, 100);
        _userInfoLabel.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_userInfoLabel);

        return scroll;
    }

    // ── Tab 1: Teleport ───────────────────────────────────────

    private Control BuildTeleportTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Teleport a coordenadas:", 12));
        var coordRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(coordRow);

        coordRow.AddChild(RpgTheme.CreateInfoLabel("Mapa:", 12));
        _teleMapEdit = RpgTheme.CreateRpgInput("1", 50);
        coordRow.AddChild(_teleMapEdit);

        coordRow.AddChild(RpgTheme.CreateInfoLabel("X:", 12));
        _teleXEdit = RpgTheme.CreateRpgInput("50", 40);
        coordRow.AddChild(_teleXEdit);

        coordRow.AddChild(RpgTheme.CreateInfoLabel("Y:", 12));
        _teleYEdit = RpgTheme.CreateRpgInput("50", 40);
        coordRow.AddChild(_teleYEdit);

        var teleCoordsBtn = RpgTheme.CreateRpgButton("Teleport", false, 13);
        teleCoordsBtn.CustomMinimumSize = new Vector2(110, 30);
        teleCoordsBtn.Pressed += () =>
        {
            string map = _teleMapEdit?.Text.Trim() ?? "1";
            string x = _teleXEdit?.Text.Trim() ?? "50";
            string y = _teleYEdit?.Text.Trim() ?? "50";
            SendGmCommand($"/TELEP YO {map} {x} {y}");
            AddLog($"Teleported self to Map {map} ({x},{y})");
        };
        vbox.AddChild(teleCoordsBtn);

        vbox.AddChild(RpgTheme.CreateSeparator());

        vbox.AddChild(RpgTheme.CreateInfoLabel("Teleport a jugador:", 12));
        var telePlayerRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        vbox.AddChild(telePlayerRow);

        _telePlayerEdit = RpgTheme.CreateRpgInput("Nombre del jugador");
        telePlayerRow.AddChild(_telePlayerEdit);

        var teleToBtn = RpgTheme.CreateRpgButton("Ir a el", false, 13);
        teleToBtn.CustomMinimumSize = new Vector2(90, 30);
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

        var summonBtn = RpgTheme.CreateRpgButton("Traerlo", false, 13);
        summonBtn.CustomMinimumSize = new Vector2(110, 30);
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

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Spawn NPC:", 12));
        var npcRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(npcRow);

        npcRow.AddChild(RpgTheme.CreateInfoLabel("Index:", 12));
        _spawnNpcIndexEdit = RpgTheme.CreateRpgInput("1", 60);
        npcRow.AddChild(_spawnNpcIndexEdit);

        npcRow.AddChild(RpgTheme.CreateInfoLabel("Cantidad:", 12));
        _spawnNpcCountEdit = RpgTheme.CreateRpgInput("1", 40);
        _spawnNpcCountEdit.Text = "1";
        npcRow.AddChild(_spawnNpcCountEdit);

        var spawnNpcBtn = RpgTheme.CreateRpgButton("Spawn NPC", false, 13);
        spawnNpcBtn.CustomMinimumSize = new Vector2(120, 30);
        spawnNpcBtn.Pressed += () =>
        {
            string idx = _spawnNpcIndexEdit?.Text.Trim() ?? "1";
            string count = _spawnNpcCountEdit?.Text.Trim() ?? "1";
            SendGmCommand($"/ACC {idx} {count}");
            AddLog($"Spawned NPC {idx} x{count}");
        };
        vbox.AddChild(spawnNpcBtn);

        vbox.AddChild(RpgTheme.CreateSeparator());

        vbox.AddChild(RpgTheme.CreateInfoLabel("Spawn Item:", 12));
        var itemRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(itemRow);

        itemRow.AddChild(RpgTheme.CreateInfoLabel("Index:", 12));
        _spawnItemIndexEdit = RpgTheme.CreateRpgInput("1", 60);
        itemRow.AddChild(_spawnItemIndexEdit);

        var spawnItemBtn = RpgTheme.CreateRpgButton("Crear Item", false, 13);
        spawnItemBtn.CustomMinimumSize = new Vector2(120, 30);
        spawnItemBtn.Pressed += () =>
        {
            string idx = _spawnItemIndexEdit?.Text.Trim() ?? "1";
            SendGmCommand($"/CI {idx}");
            AddLog($"Created item {idx}");
        };
        vbox.AddChild(spawnItemBtn);

        // Spawn list button
        var spawnListBtn = RpgTheme.CreateRpgButton("Ver lista de NPCs", false, 13);
        spawnListBtn.CustomMinimumSize = new Vector2(160, 30);
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

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Jugador objetivo:", 12));
        _modPlayerEdit = RpgTheme.CreateRpgInput("Nombre del jugador");
        vbox.AddChild(_modPlayerEdit);

        string[] actions = { "Kick", "Ban", "Jail", "Mute", "Unmute", "Revivir", "Curar" };
        string[] commands = { "/KICK", "/BAN", "/CARCEL", "/SILENCIAR", "/DESILENCIAR", "/REVIVIR", "/CURAR" };

        var grid = RpgTheme.CreateGrid(3, RpgTheme.SpacingSm, RpgTheme.SpacingSm);
        vbox.AddChild(grid);

        for (int i = 0; i < actions.Length; i++)
        {
            int idx = i;
            var btn = RpgTheme.CreateRpgButton(actions[i], false, 12);
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

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Mensaje:", 12));
        _msgTextEdit = RpgTheme.CreateRpgTextEdit("", 0, 80);
        vbox.AddChild(_msgTextEdit);

        string[] msgTypes = { "Global", "Faccion", "Ciudadano", "Criminal" };
        string[] msgCmds = { "/GMSG", "/FMSG", "/RMSG", "/CMSG" };

        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(btnRow);

        for (int i = 0; i < msgTypes.Length; i++)
        {
            int idx = i;
            var btn = RpgTheme.CreateRpgButton($"Enviar {msgTypes[i]}", false, 10);
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.CustomMinimumSize = new Vector2(0, 28);
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

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Acciones del servidor:", 12));

        var btnSaveAll = RpgTheme.CreateRpgButton("Guardar Todo", false, 13);
        btnSaveAll.CustomMinimumSize = new Vector2(160, 30);
        btnSaveAll.Pressed += () => { SendGmCommand("/GRABAR"); AddLog("Requested save all"); };
        vbox.AddChild(btnSaveAll);

        var btnOnline = RpgTheme.CreateRpgButton("Ver Online", false, 13);
        btnOnline.CustomMinimumSize = new Vector2(160, 30);
        btnOnline.Pressed += () => { SendGmCommand("/ONLINE"); AddLog("Requested online list"); };
        vbox.AddChild(btnOnline);

        var btnReload = RpgTheme.CreateRpgButton("Recargar NPCs", false, 13);
        btnReload.CustomMinimumSize = new Vector2(160, 30);
        btnReload.Pressed += () => { SendGmCommand("/RELOADNPCS"); AddLog("Requested NPC reload"); };
        vbox.AddChild(btnReload);

        var btnReloadSpells = RpgTheme.CreateRpgButton("Recargar Hechizos", false, 13);
        btnReloadSpells.CustomMinimumSize = new Vector2(160, 30);
        btnReloadSpells.Pressed += () => { SendGmCommand("/RELOADSPELLS"); AddLog("Requested spell reload"); };
        vbox.AddChild(btnReloadSpells);

        vbox.AddChild(RpgTheme.CreateSeparator());

        var btnShutdown = RpgTheme.CreateRpgButton("Apagar Servidor", false, 13);
        btnShutdown.CustomMinimumSize = new Vector2(160, 30);
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

        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        scroll.AddChild(vbox);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Tile (posicion actual o especificada):", 12));

        var coordRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(coordRow);

        coordRow.AddChild(RpgTheme.CreateInfoLabel("X:", 12));
        _mapTriggerXEdit = RpgTheme.CreateRpgInput("50", 40);
        coordRow.AddChild(_mapTriggerXEdit);

        coordRow.AddChild(RpgTheme.CreateInfoLabel("Y:", 12));
        _mapTriggerYEdit = RpgTheme.CreateRpgInput("50", 40);
        coordRow.AddChild(_mapTriggerYEdit);

        coordRow.AddChild(RpgTheme.CreateInfoLabel("Value:", 12));
        _mapTriggerValueEdit = RpgTheme.CreateRpgInput("0", 40);
        coordRow.AddChild(_mapTriggerValueEdit);

        var btnBlock = RpgTheme.CreateRpgButton("Toggle Bloqueado", false, 13);
        btnBlock.CustomMinimumSize = new Vector2(160, 30);
        btnBlock.Pressed += () =>
        {
            SendGmCommand("/BLOQ");
            AddLog("Toggled tile block");
        };
        vbox.AddChild(btnBlock);

        var btnTrigger = RpgTheme.CreateRpgButton("Set Trigger", false, 13);
        btnTrigger.CustomMinimumSize = new Vector2(160, 30);
        btnTrigger.Pressed += () =>
        {
            string val = _mapTriggerValueEdit?.Text.Trim() ?? "0";
            SendGmCommand($"/TRIGGER {val}");
            AddLog($"Set trigger value: {val}");
        };
        vbox.AddChild(btnTrigger);

        var btnSaveMap = RpgTheme.CreateRpgButton("Guardar Mapa", false, 13);
        btnSaveMap.CustomMinimumSize = new Vector2(160, 30);
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
        vbox.AddThemeConstantOverride("separation", RpgTheme.SpacingSm);

        vbox.AddChild(RpgTheme.CreateInfoLabel("Log de acciones GM:", 12));

        _logTextArea = RpgTheme.CreateRpgTextEdit("", 0, 200, readOnly: true);
        vbox.AddChild(_logTextArea);

        var clearBtn = RpgTheme.CreateRpgButton("Limpiar Log", false, 13);
        clearBtn.CustomMinimumSize = new Vector2(130, 30);
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

    // ── User list ─────────────────────────────────────────────

    /// <summary>
    /// Display the online user list from the UserNameList packet.
    /// Format: "name1,name2,name3,..."
    /// Shown in the User Info tab label area as a scrollable text block.
    /// TODO: replace with a proper ItemList control when a dedicated tab is needed.
    /// </summary>
    public void PopulateUserList(string data)
    {
        if (_userInfoLabel == null) return;
        if (string.IsNullOrEmpty(data))
        {
            _userInfoLabel.Text = "(no users online)";
            return;
        }
        var names = data.Split(',', StringSplitOptions.RemoveEmptyEntries);
        _userInfoLabel.Text = $"Online ({names.Length}):\n" + string.Join(", ", names);
    }

    // ── Open / Close ──────────────────────────────────────────

    public void Open()
    {
        SetTab(0);
        ShowForm();
    }

    public void Close()
    {
        HideForm();
    }

    public new void Toggle()
    {
        if (Visible) Close();
        else Open();
    }
}

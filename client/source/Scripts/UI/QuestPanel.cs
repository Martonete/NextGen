using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Quest entry data parsed from server string packets (QuestList, QuestCurrent, QuestSelected).
/// Server sends quest data as pipe-delimited strings.
/// </summary>
public class QuestEntry
{
    public int Id;
    public string Name = "";
    public string Description = "";
    public int Type;          // 1=Kill NPC, 2=Kill Players
    public int TargetCount;   // Required kills
    public int CurrentCount;  // Current progress
    public int GoldReward;
    public int CreditsReward;
    public bool Active;       // Currently accepted
    public bool Completed;    // Already finished
}

/// <summary>
/// Quest panel (VB6: frmQuest) — shows available/active quests with details.
/// Split-panel layout: quest list on the left, detail on the right.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class QuestPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // UI controls — left side (quest list)
    private VBoxContainer? _questListBox;

    // UI controls — right side (quest detail)
    private VBoxContainer? _detailColumn;
    private Label? _detailTitle;
    private Label? _detailType;
    private TextEdit? _detailDesc;
    private Label? _detailProgress;
    private Label? _detailRewards;
    private TextureButton? _acceptBtn;
    private TextureButton? _abandonBtn;

    // Data
    private readonly List<QuestEntry> _quests = new();
    private QuestEntry? _selectedQuest;

    public QuestPanel() : base("Misiones", new Vector2(560, 420), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var mainRow = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        ContentContainer.AddChild(mainRow);

        // ── Left side: quest list ────────────────────────────────
        var leftCol = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        leftCol.CustomMinimumSize = new Vector2(190, 0);
        mainRow.AddChild(leftCol);

        _questListBox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _questListBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftCol.AddChild(_questListBox);

        var refreshBtn = RpgTheme.CreateRpgButton("Actualizar", false, 11);
        refreshBtn.CustomMinimumSize = new Vector2(100, 28);
        refreshBtn.Pressed += OnRefresh;
        leftCol.AddChild(refreshBtn);

        // ── Right side: quest detail ─────────────────────────────
        _detailColumn = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        _detailColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _detailColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        mainRow.AddChild(_detailColumn);

        _detailTitle = RpgTheme.CreateTitleLabel("", 14);
        _detailTitle.HorizontalAlignment = HorizontalAlignment.Left;
        _detailColumn.AddChild(_detailTitle);

        _detailType = RpgTheme.CreateInfoLabel("", 11);
        _detailType.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.9f));
        _detailColumn.AddChild(_detailType);

        _detailDesc = RpgTheme.CreateRpgTextEdit("Selecciona una mision de la lista.", 0, 140, readOnly: true);
        _detailColumn.AddChild(_detailDesc);

        _detailProgress = RpgTheme.CreateInfoLabel("", 12);
        _detailProgress.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.3f));
        _detailColumn.AddChild(_detailProgress);

        _detailRewards = RpgTheme.CreateInfoLabel("", 11);
        _detailRewards.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        _detailColumn.AddChild(_detailRewards);

        // Action buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _detailColumn.AddChild(btnRow);

        _acceptBtn = RpgTheme.CreateRpgButton("Aceptar Mision", false, 12);
        _acceptBtn.CustomMinimumSize = new Vector2(130, 30);
        _acceptBtn.Pressed += OnAccept;
        _acceptBtn.Visible = false;
        btnRow.AddChild(_acceptBtn);

        _abandonBtn = RpgTheme.CreateRpgButton("Abandonar", false, 12);
        _abandonBtn.CustomMinimumSize = new Vector2(100, 30);
        _abandonBtn.Pressed += OnAbandon;
        _abandonBtn.Visible = false;
        btnRow.AddChild(_abandonBtn);
    }

    /// <summary>
    /// Open the quest panel. Requests quest list from server.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
        {
            HideForm();
            _selectedQuest = null;
            ClearDetail();
        }
        else
        {
            ShowForm();
            RequestQuestList();
        }
    }

    /// <summary>
    /// Open the panel (called from server trigger or button press).
    /// </summary>
    public void OpenPanel()
    {
        ShowForm();
        RequestQuestList();
    }

    /// <summary>
    /// Feed quest data from server packets. Called by PacketHandler.
    /// tag: "QuestList", "QuestCurrent", "QuestSelected"
    /// </summary>
    public void HandleQuestData(string tag, string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        switch (tag)
        {
            case "QuestList":
                ParseQuestList(data);
                BuildQuestList();
                break;
            case "QuestCurrent":
                ParseQuestCurrent(data);
                BuildQuestList();
                break;
            case "QuestSelected":
                ParseQuestSelected(data);
                break;
        }
    }

    /// <summary>
    /// Parse quest list data from server (pipe-delimited: id|name|active|completed|...).
    /// Format is server-defined; we handle both empty and populated strings.
    /// </summary>
    private void ParseQuestList(string data)
    {
        _quests.Clear();
        if (string.IsNullOrWhiteSpace(data)) return;

        // Expected format: entries separated by '#', fields by '|'
        // id|name|type|targetCount|currentCount|goldReward|creditsReward|active|completed
        var entries = data.Split('#', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var fields = entry.Split('|');
            if (fields.Length < 2) continue;

            var quest = new QuestEntry();
            quest.Id = fields.Length > 0 ? int.TryParse(fields[0], out int id) ? id : 0 : 0;
            quest.Name = fields.Length > 1 ? fields[1] : "";
            quest.Type = fields.Length > 2 ? (int.TryParse(fields[2], out int t) ? t : 0) : 0;
            quest.TargetCount = fields.Length > 3 ? (int.TryParse(fields[3], out int tc) ? tc : 0) : 0;
            quest.CurrentCount = fields.Length > 4 ? (int.TryParse(fields[4], out int cc) ? cc : 0) : 0;
            quest.GoldReward = fields.Length > 5 ? (int.TryParse(fields[5], out int gr) ? gr : 0) : 0;
            quest.CreditsReward = fields.Length > 6 ? (int.TryParse(fields[6], out int cr) ? cr : 0) : 0;
            quest.Active = fields.Length > 7 && fields[7] == "1";
            quest.Completed = fields.Length > 8 && fields[8] == "1";
            _quests.Add(quest);
        }
    }

    /// <summary>
    /// Parse current quest update (progress update for active quest).
    /// </summary>
    private void ParseQuestCurrent(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        var fields = data.Split('|');
        if (fields.Length < 3) return;

        int questId = int.TryParse(fields[0], out int id) ? id : 0;
        int current = fields.Length > 1 ? (int.TryParse(fields[1], out int c) ? c : 0) : 0;

        // Update matching quest in our list
        foreach (var q in _quests)
        {
            if (q.Id == questId)
            {
                q.CurrentCount = current;
                if (fields.Length > 2 && fields[2] == "1")
                    q.Completed = true;
                break;
            }
        }

        // Update detail view if this quest is selected
        if (_selectedQuest != null && _selectedQuest.Id == questId)
            ShowDetail(_selectedQuest);
    }

    /// <summary>
    /// Parse selected quest detail data.
    /// </summary>
    private void ParseQuestSelected(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        var fields = data.Split('|');
        if (fields.Length < 2) return;

        int questId = int.TryParse(fields[0], out int id) ? id : 0;
        string desc = fields.Length > 1 ? fields[1] : "";

        // Find quest and set description
        foreach (var q in _quests)
        {
            if (q.Id == questId)
            {
                q.Description = desc;
                if (_selectedQuest != null && _selectedQuest.Id == questId)
                    ShowDetail(q);
                break;
            }
        }
    }

    private void BuildQuestList()
    {
        if (_questListBox == null) return;

        // Clear existing
        foreach (var child in _questListBox.GetChildren())
            child.QueueFree();

        if (_quests.Count == 0)
        {
            var emptyLbl = RpgTheme.CreateInfoLabel("No hay misiones.", 11);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _questListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var quest in _quests)
        {
            var row = RpgTheme.CreateRpgButton("", false, 11);
            row.CustomMinimumSize = new Vector2(175, 30);

            string prefix = "";
            if (quest.Completed)
                prefix = "[OK] ";
            else if (quest.Active)
                prefix = "[>>] ";

            // Set label text via the button's child label
            if (row.GetChildCount() > 0 && row.GetChild(0) is Label lbl)
            {
                lbl.Text = $"{prefix}{quest.Name}";
                lbl.HorizontalAlignment = HorizontalAlignment.Left;
                if (quest.Completed)
                    lbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 0.5f));
                else if (quest.Active)
                    lbl.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
            }

            var captured = quest;
            row.Pressed += () => OnQuestSelected(captured);
            _questListBox.AddChild(row);
        }
    }

    private void OnQuestSelected(QuestEntry quest)
    {
        _selectedQuest = quest;
        ShowDetail(quest);

        // Request detailed info from server
        _tcp?.SendPacket(ClientPackets.WriteQuestInfo(quest.Id));
    }

    private void ShowDetail(QuestEntry quest)
    {
        _detailTitle!.Text = quest.Name;

        string typeStr = quest.Type switch
        {
            1 => "Tipo: Matar NPCs",
            2 => "Tipo: Matar Jugadores",
            _ => $"Tipo: {quest.Type}"
        };
        _detailType!.Text = typeStr;

        _detailDesc!.Text = string.IsNullOrEmpty(quest.Description)
            ? "Cargando descripcion..."
            : quest.Description;

        if (quest.Active && !quest.Completed)
            _detailProgress!.Text = $"Progreso: {quest.CurrentCount} / {quest.TargetCount}";
        else if (quest.Completed)
            _detailProgress!.Text = "Completada!";
        else
            _detailProgress!.Text = $"Objetivo: {quest.TargetCount}";

        string rewards = "";
        if (quest.GoldReward > 0)
            rewards += $"Oro: {quest.GoldReward:N0}  ";
        if (quest.CreditsReward > 0)
            rewards += $"Creditos: {quest.CreditsReward}";
        if (string.IsNullOrEmpty(rewards))
            rewards = "Sin recompensas listadas";
        _detailRewards!.Text = $"Recompensas: {rewards}";

        // Button states
        _acceptBtn!.Visible = !quest.Active && !quest.Completed;
        _abandonBtn!.Visible = quest.Active && !quest.Completed;
    }

    private void ClearDetail()
    {
        if (_detailTitle != null) _detailTitle.Text = "";
        if (_detailType != null) _detailType.Text = "";
        if (_detailDesc != null) _detailDesc.Text = "Selecciona una mision de la lista.";
        if (_detailProgress != null) _detailProgress.Text = "";
        if (_detailRewards != null) _detailRewards.Text = "";
        if (_acceptBtn != null) _acceptBtn.Visible = false;
        if (_abandonBtn != null) _abandonBtn.Visible = false;
    }

    private void OnAccept()
    {
        if (_selectedQuest == null || _tcp == null) return;
        _tcp.SendPacket(ClientPackets.WriteQuestAccept(_selectedQuest.Id, true));
        _selectedQuest.Active = true;
        ShowDetail(_selectedQuest);
        BuildQuestList();
    }

    private void OnAbandon()
    {
        if (_selectedQuest == null || _tcp == null) return;
        _tcp.SendPacket(ClientPackets.WriteQuestAccept(_selectedQuest.Id, false));
        _selectedQuest.Active = false;
        _selectedQuest.CurrentCount = 0;
        ShowDetail(_selectedQuest);
        BuildQuestList();
    }

    private void OnRefresh()
    {
        RequestQuestList();
    }

    private void RequestQuestList()
    {
        _tcp?.SendPacket(ClientPackets.WriteQuestList());
    }
}

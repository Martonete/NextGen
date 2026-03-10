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
/// Server sends QuestListResp (ID 200), QuestCurrent (ID 201), QuestSelected (ID 202).
/// Client sends QuestList (ID 120) to request list, QuestInfo (ID 121) for details,
/// QuestAccept (ID 122) to accept/abandon.
/// </summary>
public partial class QuestPanel : Control
{
    private const int PanelW = 560;
    private const int PanelH = 420;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // UI controls — left side (quest list)
    private Label? _titleLabel;
    private Button? _closeBtn;
    private ScrollContainer? _questListScroll;
    private VBoxContainer? _questListBox;

    // UI controls — right side (quest detail)
    private Panel? _detailPanel;
    private Label? _detailTitle;
    private Label? _detailType;
    private RichTextLabel? _detailDesc;
    private Label? _detailProgress;
    private Label? _detailRewards;
    private Button? _acceptBtn;
    private Button? _abandonBtn;

    // Data
    private readonly List<QuestEntry> _quests = new();
    private QuestEntry? _selectedQuest;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        // Background
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Title
        _titleLabel = new Label();
        _titleLabel.Text = "Misiones";
        _titleLabel.Position = new Vector2(10, 4);
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_titleLabel);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Text = "X";
        _closeBtn.Position = new Vector2(PanelW - 28, 2);
        _closeBtn.Size = new Vector2(24, 24);
        _closeBtn.Pressed += OnClose;
        AddChild(_closeBtn);

        // Left side: quest list (scrollable)
        _questListScroll = new ScrollContainer();
        _questListScroll.Position = new Vector2(8, 30);
        _questListScroll.Size = new Vector2(200, PanelH - 80);
        AddChild(_questListScroll);

        _questListBox = new VBoxContainer();
        _questListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _questListScroll.AddChild(_questListBox);

        // Right side: detail panel
        _detailPanel = new Panel();
        _detailPanel.Position = new Vector2(216, 30);
        _detailPanel.Size = new Vector2(PanelW - 224, PanelH - 80);
        AddChild(_detailPanel);

        // Detail: title
        _detailTitle = new Label();
        _detailTitle.Position = new Vector2(8, 8);
        _detailTitle.Size = new Vector2(PanelW - 240, 22);
        _detailTitle.AddThemeFontSizeOverride("font_size", 13);
        _detailPanel.AddChild(_detailTitle);

        // Detail: type label
        _detailType = new Label();
        _detailType.Position = new Vector2(8, 32);
        _detailType.Size = new Vector2(PanelW - 240, 18);
        _detailType.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.9f));
        _detailType.AddThemeFontSizeOverride("font_size", 11);
        _detailPanel.AddChild(_detailType);

        // Detail: description
        _detailDesc = new RichTextLabel();
        _detailDesc.Position = new Vector2(8, 54);
        _detailDesc.Size = new Vector2(PanelW - 240, 160);
        _detailDesc.BbcodeEnabled = false;
        _detailDesc.ScrollActive = true;
        _detailPanel.AddChild(_detailDesc);

        // Detail: progress
        _detailProgress = new Label();
        _detailProgress.Position = new Vector2(8, 220);
        _detailProgress.Size = new Vector2(PanelW - 240, 22);
        _detailProgress.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.3f));
        _detailPanel.AddChild(_detailProgress);

        // Detail: rewards
        _detailRewards = new Label();
        _detailRewards.Position = new Vector2(8, 244);
        _detailRewards.Size = new Vector2(PanelW - 240, 40);
        _detailRewards.AddThemeFontSizeOverride("font_size", 11);
        _detailRewards.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        _detailPanel.AddChild(_detailRewards);

        // Accept button
        _acceptBtn = new Button();
        _acceptBtn.Text = "Aceptar Mision";
        _acceptBtn.Position = new Vector2(8, PanelH - 100);
        _acceptBtn.Size = new Vector2(140, 30);
        _acceptBtn.Pressed += OnAccept;
        _detailPanel.AddChild(_acceptBtn);

        // Abandon button
        _abandonBtn = new Button();
        _abandonBtn.Text = "Abandonar";
        _abandonBtn.Position = new Vector2(156, PanelH - 100);
        _abandonBtn.Size = new Vector2(110, 30);
        _abandonBtn.Pressed += OnAbandon;
        _detailPanel.AddChild(_abandonBtn);

        // Bottom bar: Refresh + Close
        var refreshBtn = new Button { Text = "Actualizar" };
        refreshBtn.Position = new Vector2(8, PanelH - 42);
        refreshBtn.Size = new Vector2(100, 30);
        refreshBtn.Pressed += OnRefresh;
        AddChild(refreshBtn);

        var closeBottomBtn = new Button { Text = "Cerrar" };
        closeBottomBtn.Position = new Vector2(PanelW - 80, PanelH - 42);
        closeBottomBtn.Size = new Vector2(70, 30);
        closeBottomBtn.Pressed += OnClose;
        AddChild(closeBottomBtn);

        ClearDetail();
    }

    /// <summary>
    /// Open the quest panel. Requests quest list from server.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Show();
            RequestQuestList();
        }
    }

    /// <summary>
    /// Open the panel (called from server trigger or button press).
    /// </summary>
    public void OpenPanel()
    {
        Show();
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
            var emptyLbl = new Label { Text = "No hay misiones." };
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            emptyLbl.AddThemeFontSizeOverride("font_size", 11);
            _questListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var quest in _quests)
        {
            var row = new Button();
            row.CustomMinimumSize = new Vector2(185, 32);
            row.Alignment = HorizontalAlignment.Left;
            row.ClipText = true;
            row.AddThemeFontSizeOverride("font_size", 11);

            string prefix = "";
            if (quest.Completed)
            {
                prefix = "[OK] ";
                row.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 0.5f));
            }
            else if (quest.Active)
            {
                prefix = "[>>] ";
                row.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
            }

            row.Text = $"{prefix}{quest.Name}";

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

    private void OnClose()
    {
        Visible = false;
        _selectedQuest = null;
        ClearDetail();
    }

    // ── Dragging ─────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < 28)
                {
                    _dragging = true;
                    _dragOffset = mb.GlobalPosition - GlobalPosition;
                }
                else
                {
                    _dragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            GlobalPosition = mm.GlobalPosition - _dragOffset;
        }
    }
}

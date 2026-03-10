using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Trainer creature for taming list.
/// </summary>
public class TrainerCreature
{
    public int Index;        // 1-based list index (sent to server)
    public string Name = ""; // Creature display name
}

/// <summary>
/// Trainer/Pet panel (VB6: frmEntrenador) — shows player's current pets and
/// trainer NPC creature list for taming.
/// Server sends TrainerCreatureList (ID 72) with pipe-delimited creature names.
/// Client sends Train command with selected creature index.
/// </summary>
public partial class TrainerPanel : Control
{
    private const int PanelW = 380;
    private const int PanelH = 400;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // UI controls
    private Label? _titleLabel;
    private Button? _closeBtn;

    // Pet list section
    private Label? _petHeader;
    private ScrollContainer? _petListScroll;
    private VBoxContainer? _petListBox;

    // Pet command buttons
    private HBoxContainer? _petCmdRow;
    private Button? _cmdAttackBtn;
    private Button? _cmdFollowBtn;
    private Button? _cmdStayBtn;
    private Button? _cmdReleaseBtn;

    // Trainer creature list section (visible when trainer NPC interaction)
    private Label? _trainerHeader;
    private ScrollContainer? _creatureListScroll;
    private VBoxContainer? _creatureListBox;
    private Button? _tameBtn;

    // Data
    private readonly List<TrainerCreature> _creatures = new();
    private int _selectedCreatureIndex = -1;
    private int _selectedPetIndex = -1;
    private bool _trainerMode; // true when opened via trainer NPC

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
        _titleLabel.Text = "Mascotas";
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

        // Pet header
        _petHeader = new Label();
        _petHeader.Text = "Tus mascotas:";
        _petHeader.Position = new Vector2(8, 30);
        _petHeader.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_petHeader);

        // Pet list (scrollable)
        _petListScroll = new ScrollContainer();
        _petListScroll.Position = new Vector2(8, 50);
        _petListScroll.Size = new Vector2(PanelW - 16, 100);
        AddChild(_petListScroll);

        _petListBox = new VBoxContainer();
        _petListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _petListScroll.AddChild(_petListBox);

        // Pet command buttons
        _petCmdRow = new HBoxContainer();
        _petCmdRow.Position = new Vector2(8, 155);
        _petCmdRow.Size = new Vector2(PanelW - 16, 30);
        AddChild(_petCmdRow);

        _cmdAttackBtn = new Button { Text = "Atacar" };
        _cmdAttackBtn.CustomMinimumSize = new Vector2(75, 28);
        _cmdAttackBtn.Pressed += () => SendPetCommand("ATACAR");
        _petCmdRow.AddChild(_cmdAttackBtn);

        _cmdFollowBtn = new Button { Text = "Seguir" };
        _cmdFollowBtn.CustomMinimumSize = new Vector2(75, 28);
        _cmdFollowBtn.Pressed += () => SendPetCommand("SEGUIR");
        _petCmdRow.AddChild(_cmdFollowBtn);

        _cmdStayBtn = new Button { Text = "Quieto" };
        _cmdStayBtn.CustomMinimumSize = new Vector2(75, 28);
        _cmdStayBtn.Pressed += () => SendPetCommand("QUIETO");
        _petCmdRow.AddChild(_cmdStayBtn);

        _cmdReleaseBtn = new Button { Text = "Liberar" };
        _cmdReleaseBtn.CustomMinimumSize = new Vector2(75, 28);
        _cmdReleaseBtn.Pressed += () => SendPetCommand("LIBERAR");
        _petCmdRow.AddChild(_cmdReleaseBtn);

        // Trainer header
        _trainerHeader = new Label();
        _trainerHeader.Text = "Criaturas disponibles:";
        _trainerHeader.Position = new Vector2(8, 192);
        _trainerHeader.AddThemeFontSizeOverride("font_size", 12);
        _trainerHeader.Visible = false;
        AddChild(_trainerHeader);

        // Creature list (scrollable — visible in trainer mode)
        _creatureListScroll = new ScrollContainer();
        _creatureListScroll.Position = new Vector2(8, 212);
        _creatureListScroll.Size = new Vector2(PanelW - 16, 130);
        _creatureListScroll.Visible = false;
        AddChild(_creatureListScroll);

        _creatureListBox = new VBoxContainer();
        _creatureListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _creatureListScroll.AddChild(_creatureListBox);

        // Tame button
        _tameBtn = new Button { Text = "Entrenar" };
        _tameBtn.Position = new Vector2(8, PanelH - 72);
        _tameBtn.Size = new Vector2(100, 30);
        _tameBtn.Pressed += OnTame;
        _tameBtn.Visible = false;
        AddChild(_tameBtn);

        // Close bottom button
        var closeBottomBtn = new Button { Text = "Cerrar" };
        closeBottomBtn.Position = new Vector2(PanelW - 80, PanelH - 42);
        closeBottomBtn.Size = new Vector2(70, 30);
        closeBottomBtn.Pressed += OnClose;
        AddChild(closeBottomBtn);
    }

    /// <summary>
    /// Toggle panel visibility.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            _trainerMode = false;
            ShowPetMode();
            Show();
            RefreshPetList();
        }
    }

    /// <summary>
    /// Open in trainer mode with creature list from server.
    /// Called when TrainerCreatureList packet arrives.
    /// </summary>
    public void OpenTrainer(string creatureData)
    {
        _trainerMode = true;
        ParseCreatureList(creatureData);
        ShowTrainerMode();
        Show();
        RefreshPetList();
        BuildCreatureList();
    }

    private void ShowPetMode()
    {
        _titleLabel!.Text = "Mascotas";
        _trainerHeader!.Visible = false;
        _creatureListScroll!.Visible = false;
        _tameBtn!.Visible = false;
    }

    private void ShowTrainerMode()
    {
        _titleLabel!.Text = "Entrenador";
        _trainerHeader!.Visible = true;
        _creatureListScroll!.Visible = true;
        _tameBtn!.Visible = true;
    }

    /// <summary>
    /// Parse creature list from server (pipe-delimited names: "Lobo|Oso Pardo|Serpiente").
    /// </summary>
    private void ParseCreatureList(string data)
    {
        _creatures.Clear();
        _selectedCreatureIndex = -1;

        if (string.IsNullOrWhiteSpace(data)) return;

        var names = data.Split('|', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < names.Length; i++)
        {
            _creatures.Add(new TrainerCreature
            {
                Index = i + 1, // 1-based for server
                Name = names[i].Trim()
            });
        }
    }

    private void RefreshPetList()
    {
        if (_petListBox == null || _state == null) return;

        // Clear
        foreach (var child in _petListBox.GetChildren())
            child.QueueFree();

        // Build pet list from GameState (mascotas data)
        var pets = _state.PetList;
        if (pets.Count == 0)
        {
            var emptyLbl = new Label { Text = "No tienes mascotas." };
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            emptyLbl.AddThemeFontSizeOverride("font_size", 11);
            _petListBox.AddChild(emptyLbl);
            _selectedPetIndex = -1;
            UpdatePetCommandButtons();
            return;
        }

        for (int i = 0; i < pets.Count; i++)
        {
            var pet = pets[i];
            var row = new HBoxContainer();
            row.CustomMinimumSize = new Vector2(PanelW - 24, 28);

            // Pet name
            var nameLabel = new Label();
            nameLabel.Text = pet.Name;
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(nameLabel);

            // HP bar
            var hpBar = new ProgressBar();
            hpBar.CustomMinimumSize = new Vector2(80, 18);
            hpBar.MaxValue = 100;
            hpBar.Value = pet.HpPercent;
            hpBar.ShowPercentage = false;

            var styleFill = new StyleBoxFlat();
            styleFill.BgColor = new Color(0.2f, 0.7f, 0.2f);
            hpBar.AddThemeStyleboxOverride("fill", styleFill);

            var styleBg = new StyleBoxFlat();
            styleBg.BgColor = new Color(0.3f, 0.1f, 0.1f);
            hpBar.AddThemeStyleboxOverride("background", styleBg);

            row.AddChild(hpBar);
            _petListBox.AddChild(row);

            // Click handler for selecting pet
            int petIdx = i;
            var btn = new Button();
            btn.Text = $"{pet.Name}";
            btn.CustomMinimumSize = new Vector2(PanelW - 24, 28);
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", 11);

            bool isSelected = (petIdx == _selectedPetIndex);
            if (isSelected)
                btn.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));

            btn.Pressed += () =>
            {
                _selectedPetIndex = petIdx;
                UpdatePetCommandButtons();
                RefreshPetList();
            };

            // Replace row with button for clickability
            row.QueueFree();
            _petListBox.AddChild(btn);
        }

        UpdatePetCommandButtons();
    }

    private void BuildCreatureList()
    {
        if (_creatureListBox == null) return;

        // Clear
        foreach (var child in _creatureListBox.GetChildren())
            child.QueueFree();

        if (_creatures.Count == 0)
        {
            var emptyLbl = new Label { Text = "No hay criaturas disponibles." };
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            emptyLbl.AddThemeFontSizeOverride("font_size", 11);
            _creatureListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var creature in _creatures)
        {
            var btn = new Button();
            btn.Text = creature.Name;
            btn.CustomMinimumSize = new Vector2(PanelW - 30, 26);
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", 11);

            bool isSelected = (creature.Index == _selectedCreatureIndex);
            if (isSelected)
                btn.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));

            var captured = creature;
            btn.Pressed += () =>
            {
                _selectedCreatureIndex = captured.Index;
                BuildCreatureList(); // Rebuild to update highlight
            };
            _creatureListBox.AddChild(btn);
        }
    }

    private void UpdatePetCommandButtons()
    {
        bool hasPet = _selectedPetIndex >= 0 && _state != null && _selectedPetIndex < _state.PetList.Count;
        _cmdAttackBtn!.Disabled = !hasPet;
        _cmdFollowBtn!.Disabled = !hasPet;
        _cmdStayBtn!.Disabled = !hasPet;
        _cmdReleaseBtn!.Disabled = !hasPet;
    }

    private void SendPetCommand(string command)
    {
        if (_tcp == null || _selectedPetIndex < 0) return;
        // VB6: pet commands via slash commands
        _tcp.SendPacket(ClientPackets.WriteSlashCommand($"/{command} {_selectedPetIndex + 1}"));
    }

    private void OnTame()
    {
        if (_tcp == null || _selectedCreatureIndex < 0) return;
        // VB6: WriteTrain(listIndex + 1) — sends Train packet with creature index
        _tcp.SendPacket(ClientPackets.WriteTrain(_selectedCreatureIndex));
        OnClose();
    }

    private void OnClose()
    {
        Visible = false;
        _trainerMode = false;
        _selectedCreatureIndex = -1;
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

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
/// Now uses RpgBaseForm for consistent RPG chrome (frame, title, close, drag).
/// </summary>
public partial class TrainerPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Pet list section
    private Label? _petHeader;
    private ScrollContainer? _petListScroll;
    private VBoxContainer? _petListBox;

    // Pet command buttons
    private TextureButton? _cmdAttackBtn;
    private TextureButton? _cmdFollowBtn;
    private TextureButton? _cmdStayBtn;
    private TextureButton? _cmdReleaseBtn;

    // Trainer creature list section (visible when trainer NPC interaction)
    private Label? _trainerHeader;
    private ScrollContainer? _creatureListScroll;
    private VBoxContainer? _creatureListBox;
    private TextureButton? _tameBtn;
    private TextureButton? _closeBottomBtn;

    // Data
    private readonly List<TrainerCreature> _creatures = new();
    private int _selectedCreatureIndex = -1;
    private int _selectedPetIndex = -1;
    private bool _trainerMode; // true when opened via trainer NPC

    public TrainerPanel() : base("Mascotas", new Vector2(380, 420), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // Pet header
        _petHeader = RpgTheme.CreateInfoLabel("Tus mascotas:", 13);
        vbox.AddChild(_petHeader);

        // Pet list (scrollable)
        _petListScroll = new ScrollContainer();
        _petListScroll.CustomMinimumSize = new Vector2(0, 100);
        _petListScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _petListScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        vbox.AddChild(_petListScroll);

        _petListScroll.Ready += () => RpgTheme.StyleScrollbar(_petListScroll);

        _petListBox = new VBoxContainer();
        _petListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _petListScroll.AddChild(_petListBox);

        // Pet command buttons
        var petCmdRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        petCmdRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(petCmdRow);

        _cmdAttackBtn = RpgTheme.CreateRpgButton("Atacar", false, 11);
        _cmdAttackBtn.CustomMinimumSize = new Vector2(70, 26);
        _cmdAttackBtn.Pressed += () => SendPetCommand("ATACAR");
        petCmdRow.AddChild(_cmdAttackBtn);

        _cmdFollowBtn = RpgTheme.CreateRpgButton("Seguir", false, 11);
        _cmdFollowBtn.CustomMinimumSize = new Vector2(70, 26);
        _cmdFollowBtn.Pressed += () => SendPetCommand("SEGUIR");
        petCmdRow.AddChild(_cmdFollowBtn);

        _cmdStayBtn = RpgTheme.CreateRpgButton("Quieto", false, 11);
        _cmdStayBtn.CustomMinimumSize = new Vector2(70, 26);
        _cmdStayBtn.Pressed += () => SendPetCommand("QUIETO");
        petCmdRow.AddChild(_cmdStayBtn);

        _cmdReleaseBtn = RpgTheme.CreateRpgButton("Liberar", false, 11);
        _cmdReleaseBtn.CustomMinimumSize = new Vector2(70, 26);
        _cmdReleaseBtn.Pressed += () => SendPetCommand("LIBERAR");
        petCmdRow.AddChild(_cmdReleaseBtn);

        // Trainer header (hidden by default)
        _trainerHeader = RpgTheme.CreateInfoLabel("Criaturas disponibles:", 13);
        _trainerHeader.Visible = false;
        vbox.AddChild(_trainerHeader);

        // Creature list (scrollable — visible in trainer mode)
        _creatureListScroll = new ScrollContainer();
        _creatureListScroll.CustomMinimumSize = new Vector2(0, 110);
        _creatureListScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _creatureListScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _creatureListScroll.Visible = false;
        vbox.AddChild(_creatureListScroll);

        _creatureListScroll.Ready += () => RpgTheme.StyleScrollbar(_creatureListScroll);

        _creatureListBox = new VBoxContainer();
        _creatureListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _creatureListScroll.AddChild(_creatureListBox);

        // Footer buttons
        var footerRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        footerRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(footerRow);

        // Tame button (hidden by default)
        _tameBtn = RpgTheme.CreateRpgButton("Entrenar", false, 14);
        _tameBtn.CustomMinimumSize = new Vector2(110, 30);
        _tameBtn.Pressed += OnTame;
        _tameBtn.Visible = false;
        footerRow.AddChild(_tameBtn);

        // Close bottom button
        _closeBottomBtn = RpgTheme.CreateRpgButton("Cerrar", false, 14);
        _closeBottomBtn.CustomMinimumSize = new Vector2(90, 30);
        _closeBottomBtn.Pressed += OnClose;
        footerRow.AddChild(_closeBottomBtn);
    }

    /// <summary>
    /// Toggle panel visibility.
    /// </summary>
    public void TogglePanel()
    {
        if (Visible)
        {
            HideForm();
        }
        else
        {
            _trainerMode = false;
            ShowPetMode();
            ShowForm();
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
        ShowForm();
        RefreshPetList();
        BuildCreatureList();
    }

    private void ShowPetMode()
    {
        TitleText = "Mascotas";
        _trainerHeader!.Visible = false;
        _creatureListScroll!.Visible = false;
        _tameBtn!.Visible = false;
    }

    private void ShowTrainerMode()
    {
        TitleText = "Entrenador";
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
            var emptyLbl = RpgTheme.CreateInfoLabel("No tienes mascotas.", 11);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _petListBox.AddChild(emptyLbl);
            _selectedPetIndex = -1;
            UpdatePetCommandButtons();
            return;
        }

        for (int i = 0; i < pets.Count; i++)
        {
            var pet = pets[i];
            int petIdx = i;

            var btn = new Button();
            btn.Text = $"{pet.Name}";
            btn.CustomMinimumSize = new Vector2(0, 28);
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.FocusMode = FocusModeEnum.None;

            bool isSelected = (petIdx == _selectedPetIndex);
            if (isSelected)
                btn.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));

            btn.Pressed += () =>
            {
                _selectedPetIndex = petIdx;
                UpdatePetCommandButtons();
                RefreshPetList();
            };

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
            var emptyLbl = RpgTheme.CreateInfoLabel("No hay criaturas disponibles.", 11);
            emptyLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _creatureListBox.AddChild(emptyLbl);
            return;
        }

        foreach (var creature in _creatures)
        {
            var btn = new Button();
            btn.Text = creature.Name;
            btn.CustomMinimumSize = new Vector2(0, 26);
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.FocusMode = FocusModeEnum.None;

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
        // VB6: WriteTrain(listIndex + 1) -- sends Train packet with creature index
        _tcp.SendPacket(ClientPackets.WriteTrain(_selectedCreatureIndex));
        OnClose();
    }

    private void OnClose()
    {
        HideForm();
        _trainerMode = false;
        _selectedCreatureIndex = -1;
    }
}

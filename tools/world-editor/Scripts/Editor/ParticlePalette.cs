#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Left sidebar panel for particle-group selection.
/// Shows a scrollable list of all particle definitions loaded from Particles.ini.
/// Selecting an entry makes it the active group for painting.
/// </summary>
public partial class ParticlePalette : VBoxContainer
{
    [Signal] public delegate void ParticleSelectedEventHandler(int groupId);

    public ParticleEngine? Engine;
    public EditorState? State;
    public GrhData[]? Grhs;
    public TextureManager? Textures;

    private ParticlePreview? _preview;
    private LineEdit? _searchBox;
    private ScrollContainer? _scroll;
    private VBoxContainer? _listContainer;
    private Label? _infoLabel;
    private VBoxContainer? _emptyState;

    private int _selectedGroup;
    private readonly List<Button> _itemButtons = new();

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        AddThemeConstantOverride("separation", 3);

        // Preview area (live animated particle preview)
        var previewLabel = EditorTheme.MakeLabel("Preview", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        previewLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(previewLabel);

        _preview = new ParticlePreview();
        AddChild(_preview);

        // Search box
        _searchBox = new LineEdit
        {
            PlaceholderText = "Buscar partícula...",
            ClearButtonEnabled = true,
        };
        _searchBox.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _searchBox.TextChanged += _ => RebuildList();
        AddChild(_searchBox);

        // Info label
        _infoLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _infoLabel.CustomMinimumSize = new Vector2(0, 18);
        AddChild(_infoLabel);

        // Scrollable list
        _scroll = new ScrollContainer();
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.CustomMinimumSize = new Vector2(0, 200);
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scroll.ClipContents = true;
        AddChild(_scroll);

        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 1);
        _listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_listContainer);

        // Empty state (shown when no particle data loaded)
        _emptyState = new VBoxContainer();
        _emptyState.AddThemeConstantOverride("separation", 8);
        _emptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
        _emptyState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _emptyState.Visible = false;

        var emptyLabel = EditorTheme.MakeLabel(
            "No hay definiciones de partículas.\nCarga los recursos del cliente.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _emptyState.AddChild(emptyLabel);

        AddChild(_emptyState);
    }

    /// <summary>Rebuild the particle list after data is loaded or filter changes.</summary>
    public void Rebuild()
    {
        RebuildList();
    }

    private void RebuildList()
    {
        if (_listContainer == null) return;

        // Clear old items
        foreach (var child in _listContainer.GetChildren())
            child.QueueFree();
        _itemButtons.Clear();

        if (Engine == null || Engine.Defs.Length == 0)
        {
            if (_infoLabel != null) _infoLabel.Text = "";
            if (_emptyState != null) _emptyState.Visible = true;
            if (_scroll != null) _scroll.Visible = false;
            return;
        }
        if (_emptyState != null) _emptyState.Visible = false;
        if (_scroll != null) _scroll.Visible = true;

        string search = _searchBox?.Text?.Trim().ToLowerInvariant() ?? "";

        int count = 0;
        // Particle definitions are 1-indexed; index 0 is unused placeholder
        for (int i = 1; i < Engine.Defs.Length; i++)
        {
            var def = Engine.Defs[i];
            if (def == null) continue;

            if (search.Length > 0)
            {
                bool matchName = def.Name.ToLowerInvariant().Contains(search);
                bool matchId = i.ToString().Contains(search);
                if (!matchName && !matchId) continue;
            }

            var btn = CreateParticleButton(i, def);
            _listContainer.AddChild(btn);
            _itemButtons.Add(btn);
            count++;
        }

        if (_infoLabel != null)
            _infoLabel.Text = $"{count} partículas";

        // Restore selection highlight
        SyncSelectionHighlight();
    }

    private Button CreateParticleButton(int groupId, ParticleStreamDef def)
    {
        var btn = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 32),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.MouseFilter = MouseFilterEnum.Ignore;

        // Particle icon (small colored diamond to match map overlay)
        var icon = EditorTheme.MakeLabel("\u25c6", new Color(1f, 1f, 0f), EditorTheme.FONT_SM);
        icon.CustomMinimumSize = new Vector2(20, 20);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.MouseFilter = MouseFilterEnum.Ignore;
        hbox.AddChild(icon);

        // Text info
        var vtext = new VBoxContainer();
        vtext.AddThemeConstantOverride("separation", 0);
        vtext.MouseFilter = MouseFilterEnum.Ignore;

        string displayName = string.IsNullOrWhiteSpace(def.Name) ? $"Partícula {groupId}" : def.Name;
        var nameLabel = EditorTheme.MakeLabel(
            $"#{groupId} {displayName}",
            EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        vtext.AddChild(nameLabel);

        hbox.AddChild(vtext);
        btn.AddChild(hbox);

        // Style
        btn.AddThemeStyleboxOverride("normal",
            EditorTheme.FlatBox(EditorTheme.BG_PANEL, 2, 4, 2));
        btn.AddThemeStyleboxOverride("hover",
            EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 2, 4, 2));
        btn.AddThemeStyleboxOverride("pressed",
            EditorTheme.FlatBox(EditorTheme.BG_TOOL_ACTIVE, 2, 4, 2, EditorTheme.ACCENT, 1));

        int capturedId = groupId;
        btn.Pressed += () => OnParticleClicked(capturedId, btn);

        // Restore pressed state if this was previously selected
        if (groupId == _selectedGroup)
            btn.SetPressedNoSignal(true);

        return btn;
    }

    private void OnParticleClicked(int groupId, Button btn)
    {
        _selectedGroup = groupId;

        // Deselect all others
        foreach (var b in _itemButtons)
            if (b != btn) b.SetPressedNoSignal(false);

        btn.SetPressedNoSignal(true);

        if (_preview != null)
        {
            _preview.Grhs = Grhs;
            _preview.Textures = Textures;
            _preview.Engine = Engine;
            _preview.SetDefinition(groupId);
        }

        if (State != null)
        {
            State.SelectedParticleGroup = groupId;
            State.ActiveTool = EditorTool.Particle;
        }

        EmitSignal(SignalName.ParticleSelected, groupId);
    }

    private void SyncSelectionHighlight()
    {
        // No-op: buttons restore their pressed state in CreateParticleButton via _selectedGroup
    }

    public int SelectedGroup => _selectedGroup;

    // -------------------------------------------------------------------------
    // Inner class: live particle preview control
    // -------------------------------------------------------------------------

    /// <summary>
    /// A fixed-size Control that simulates and renders a single particle stream
    /// for live preview of the selected particle definition.
    /// </summary>
    private sealed partial class ParticlePreview : Control
    {
        private const int PreviewWidth  = 160;
        private const int PreviewHeight = 120;
        private const float CenterX = PreviewWidth  / 2f;
        private const float CenterY = PreviewHeight / 2f;

        public GrhData[]?      Grhs;
        public TextureManager? Textures;
        public ParticleEngine? Engine;

        private EditorParticleStream? _stream;
        private int _defIndex;
        private bool _hasDefinition;
        private Label? _placeholder;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(PreviewWidth, PreviewHeight);
            Size              = new Vector2(PreviewWidth, PreviewHeight);
            ClipContents      = true;

            // Dark background panel
            var bg = new Panel();
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            bg.AddThemeStyleboxOverride("panel",
                EditorTheme.FlatBox(EditorTheme.BG_DARK, 2, 0, 0));
            bg.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(bg);

            // Placeholder label shown when nothing is selected
            _placeholder = EditorTheme.MakeLabel(
                "Selecciona una partícula",
                EditorTheme.TEXT_MUTED,
                EditorTheme.FONT_SM);
            _placeholder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
            _placeholder.VerticalAlignment   = VerticalAlignment.Center;
            _placeholder.AutowrapMode        = TextServer.AutowrapMode.WordSmart;
            _placeholder.MouseFilter         = MouseFilterEnum.Ignore;
            AddChild(_placeholder);

            // Additive-blend material, same as ParticleOverlay in MapViewport
            Material = new CanvasItemMaterial
            {
                BlendMode = CanvasItemMaterial.BlendModeEnum.Add
            };
        }

        /// <summary>Switch to previewing a new particle definition.</summary>
        public void SetDefinition(int defIndex)
        {
            _defIndex      = defIndex;
            _hasDefinition = false;
            _stream        = null;

            if (Engine == null || defIndex < 1 || defIndex >= Engine.Defs.Length) return;
            var def = Engine.Defs[defIndex];
            if (def == null || def.NumParticles <= 0) return;

            var stream = new EditorParticleStream
            {
                DefIndex  = defIndex,
                Particles = new EditorParticle[def.NumParticles],
                Active    = true
            };
            for (int i = 0; i < def.NumParticles; i++)
                stream.Particles[i] = new EditorParticle();

            _stream        = stream;
            _hasDefinition = true;

            if (_placeholder != null) _placeholder.Visible = false;
        }

        public override void _Process(double delta)
        {
            if (!_hasDefinition || _stream == null || Engine == null) return;
            if (_defIndex < 1 || _defIndex >= Engine.Defs.Length) return;

            var def = Engine.Defs[_defIndex];
            if (def == null) return;

            ParticleEngine.UpdateSingleStream(_stream, def, (float)(delta * 1000.0));
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (!_hasDefinition || _stream == null || Grhs == null || Textures == null) return;

            foreach (var p in _stream.Particles)
            {
                if (!p.Alive || p.GrhIndex <= 0 || p.GrhIndex >= Grhs.Length) continue;
                var grh = Grhs[p.GrhIndex];

                if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
                {
                    int frameIdx = grh.Frames[0];
                    if (frameIdx <= 0 || frameIdx >= Grhs.Length) continue;
                    grh = Grhs[frameIdx];
                }

                if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) continue;
                var texture = Textures.GetTexture(grh.FileNum);
                if (texture == null) continue;

                var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
                float drawX = CenterX + p.X - grh.PixelWidth  / 2f;
                float drawY = CenterY + p.Y - grh.PixelHeight / 2f;
                var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
                var color    = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, p.Alpha);
                DrawTextureRectRegion(texture, destRect, srcRect, color);
            }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Embeddable control for building a particle's Grh_List: a browsable grid of
/// sprites plus a reorderable list of the chosen ones. The same GRH may appear
/// more than once — repeats weight how often it is picked at spawn time (see
/// ParticleEngine.SpawnParticle).
///
/// Sprites are addressed by raw GRH id, which nobody memorises, so browsing is
/// organised by source sheet instead: the picker scans Graficos.ind on load and
/// groups every sprite by the PNG it comes from. That way new atlases show up
/// automatically rather than needing a hardcoded id range.
/// </summary>
public partial class GrhMultiPicker : VBoxContainer
{
    public GrhData[]? Grhs;
    public TextureManager? Textures;

    /// <summary>Fires whenever the chosen list changes (add/remove/reorder).</summary>
    public Action? OnChanged;

    private readonly List<int> _chosen = new();
    private readonly Dictionary<int, Texture2D?> _previewCache = new();

    /// <summary>Sprites grouped by the sheet (FileNum) they are cut from.</summary>
    private readonly Dictionary<int, List<int>> _bySheet = new();
    private List<int> _sheetOrder = new();   // every sheet, best candidates first
    private List<int> _listedSheets = new(); // what the dropdown currently offers
    private List<int> _visible = new();      // ids currently listed, after filtering
    /// <summary>Multi-frame GRHs — the animated effects spells are built from.</summary>
    private readonly List<int> _animated = new();
    /// <summary>The curated spell-effect list from Fxs.ind, when available.</summary>
    private readonly List<int> _spellFx = new();

    /// <summary>Path to INIT/, needed to read the curated effect list.</summary>
    public string InitPath = "";

    /// <summary>Sentinels used in place of a FileNum to select a virtual group.</summary>
    private const int AnimatedGroup = -2;
    private const int SpellFxGroup = -3;

    private LineEdit? _searchBox;
    private OptionButton? _sheetSelect;
    private OptionButton? _sizeSelect;
    private GridContainer? _grid;
    private VBoxContainer? _chosenList;
    private Label? _chosenCountLabel;
    private Label? _pageLabel;
    private Label? _hintLabel;
    private Button? _moreBtn;
    private ScrollContainer? _scroll;
    private CheckBox? _litBgCheck;
    private CheckBox? _particlesOnlyCheck;
    private Panel? _gridBackdrop;

    private const int PreviewSize = 64;   // large enough to tell smoke from sparks
    private const int Columns = 5;
    /// <summary>
    /// Rows appended each time the grid nears its end. Sheets are small enough
    /// to scroll through, but "Todas" spans thousands of sprites, so they are
    /// streamed in as the user scrolls rather than paged or built up front.
    /// </summary>
    private const int ChunkRows = 24;

    private int _shownCount;              // how many of _visible are built
    private int _sheetFilter = -1;        // -1 = every sheet
    /// <summary>
    /// Guards against OptionButton.Clear()/Selected firing ItemSelected while
    /// the dropdown is being rebuilt — the handler would re-enter and read a
    /// half-built list.
    /// </summary>
    private bool _rebuildingSheets;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        AddChild(EditorTheme.SectionLabel("Sprites (Grh_List)"));

        _hintLabel = EditorTheme.MakeLabel(
            "★ Efectos de hechizos: los 106 que ya usa el juego — es por donde conviene empezar.\n" +
            "Los marcados con ▶ se animan solos. Clic para agregar; repetir uno lo hace más frecuente.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_hintLabel);

        // --- Sheet selector: the primary way to navigate ---
        var sheetRow = new HBoxContainer();
        sheetRow.AddThemeConstantOverride("separation", 4);
        sheetRow.AddChild(EditorTheme.MakeLabel("Lámina:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _sheetSelect = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sheetSelect.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _sheetSelect.ItemSelected += idx =>
        {
            if (_rebuildingSheets) return;
            // Leading virtual groups shift the sheet indices, and the spell-fx
            // row only exists when Fxs.ind was found.
            int lead = _spellFx.Count > 0 ? 2 : 1;
            int i = (int)idx - lead - 1;
            if (_spellFx.Count > 0 && idx == 0) _sheetFilter = SpellFxGroup;
            else if (idx == lead - 1) _sheetFilter = AnimatedGroup;
            else if (idx == lead) _sheetFilter = -1;
            else _sheetFilter = i >= 0 && i < _listedSheets.Count ? _listedSheets[i] : -1;
            ApplyFilter();
        };
        sheetRow.AddChild(_sheetSelect);
        AddChild(sheetRow);

        _particlesOnlyCheck = new CheckBox { Text = "Solo láminas de partículas", ButtonPressed = true };
        _particlesOnlyCheck.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _particlesOnlyCheck.TooltipText =
            "Muestra solo atlas de 64/128px, que es donde vive el arte de partículas.\nDestildá para ver las 5000+ láminas del juego.";
        _particlesOnlyCheck.Toggled += _ => { RebuildSheetOptions(); ApplyFilter(); };
        AddChild(_particlesOnlyCheck);

        _searchBox = new LineEdit
        {
            PlaceholderText = "Filtrar por GRH o rango (2745 · 27450-27460)",
            ClearButtonEnabled = true,
        };
        _searchBox.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _searchBox.TextChanged += _ => ApplyFilter();
        AddChild(_searchBox);

        // With thousands of animations, size is the practical way to narrow
        // down: big effects are spell casts, small ones are sparks and motes.
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 4);
        sizeRow.AddChild(EditorTheme.MakeLabel("Tamaño:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _sizeSelect = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sizeSelect.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _sizeSelect.AddItem("Cualquiera");
        _sizeSelect.AddItem("Chico (≤ 32px)");
        _sizeSelect.AddItem("Mediano (33-96px)");
        _sizeSelect.AddItem("Grande (> 96px)");
        _sizeSelect.ItemSelected += _ => ApplyFilter();
        sizeRow.AddChild(_sizeSelect);
        AddChild(sizeRow);

        // Particle art is mostly additive: bright shapes on black. On a dark
        // panel that reads as an empty square, so the grid gets its own lighter
        // backdrop that can be toggled when a sprite has real transparency.
        var bgRow = new HBoxContainer();
        _litBgCheck = new CheckBox { Text = "Fondo claro", ButtonPressed = true };
        _litBgCheck.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _litBgCheck.TooltipText = "Las partículas suelen ser brillos sobre negro.\nUn fondo claro las hace visibles en esta grilla.";
        _litBgCheck.Toggled += _ => UpdateBackdrop();
        bgRow.AddChild(_litBgCheck);
        AddChild(bgRow);

        _gridBackdrop = new Panel { CustomMinimumSize = new Vector2(0, 300) };
        AddChild(_gridBackdrop);

        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _gridBackdrop.AddChild(_scroll);

        _grid = new GridContainer { Columns = Columns };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_grid);

        _pageLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_pageLabel);

        _moreBtn = EditorTheme.MakeButton("Ver más", GrowGrid);
        _moreBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _moreBtn.Visible = false;
        AddChild(_moreBtn);

        AddChild(EditorTheme.MakeHSeparator());

        _chosenCountLabel = EditorTheme.MakeLabel("0 sprites elegidos", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        AddChild(_chosenCountLabel);

        var chosenScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 110),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(chosenScroll);

        _chosenList = new VBoxContainer();
        _chosenList.AddThemeConstantOverride("separation", 2);
        chosenScroll.AddChild(_chosenList);

        LoadSpellFx();
        BuildSheetIndex();
        UpdateBackdrop();
        ApplyFilter();
    }

    /// <summary>Reset the chosen list (e.g. when opening the edit popup).</summary>
    public void SetChosen(int[] grhList)
    {
        _chosen.Clear();
        _chosen.AddRange(grhList);
        RebuildChosenList();
    }

    public int[] GetChosen() => _chosen.ToArray();

    // ── Sheet index ───────────────────────────────────────────────────

    /// <summary>
    /// Groups every static sprite by source sheet. Built once, so a newly
    /// indexed atlas appears in the dropdown without touching this code.
    /// </summary>
    /// <summary>
    /// Reads Fxs.ind, the game's curated list of spell effects. Each entry
    /// points at an animated GRH, so it is a far shorter and more relevant
    /// starting set than every animation in the atlas.
    /// </summary>
    private void LoadSpellFx()
    {
        _spellFx.Clear();
        if (InitPath.Length == 0) return;
        string path = System.IO.Path.Combine(InitPath, "Fxs.ind");
        if (!System.IO.File.Exists(path)) return;

        try
        {
            using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path));
            r.BaseStream.Seek(263, System.IO.SeekOrigin.Begin);   // legacy MiCabecera
            short count = r.ReadInt16();
            var seen = new HashSet<int>();
            for (int i = 1; i <= count; i++)
            {
                short anim = r.ReadInt16();
                r.ReadInt16(); r.ReadInt16();                      // offsets, unused here
                if (anim > 0 && Grhs != null && anim < Grhs.Length
                    && Grhs[anim].IsValid && seen.Add(anim))
                    _spellFx.Add(anim);
            }
        }
        catch (Exception e)
        {
            GD.Print($"[GrhMultiPicker] No se pudo leer Fxs.ind: {e.Message}");
        }
    }

    private void BuildSheetIndex()
    {
        _bySheet.Clear();
        if (Grhs == null) return;

        _animated.Clear();
        for (int id = 1; id < Grhs.Length; id++)
        {
            var grh = Grhs[id];
            if (!grh.IsValid) continue;

            // Animated GRHs are exactly what spell effects use, so they belong
            // here: the engine picks one per particle and the renderer cycles
            // its frames. Group them separately since they have no own FileNum.
            if (grh.NumFrames > 1)
            {
                if (grh.Frames is { Length: > 0 }) _animated.Add(id);
                continue;
            }

            if (grh.FileNum <= 0) continue;
            if (!_bySheet.TryGetValue(grh.FileNum, out var list))
            {
                list = new List<int>();
                _bySheet[grh.FileNum] = list;
            }
            list.Add(id);
        }

        // Likely particle atlases first, then everything else by number. A 32px
        // grid is nearly always map tiles, so only 64/128 rank as "particle".
        _sheetOrder = _bySheet.Keys
            .OrderByDescending(f => ParticleScore(f))
            .ThenBy(f => f)
            .ToList();

        RebuildSheetOptions();
    }

    private void RebuildSheetOptions()
    {
        if (_sheetSelect == null || Grhs == null) return;
        bool onlyParticles = _particlesOnlyCheck?.ButtonPressed ?? true;

        _listedSheets = onlyParticles
            ? _sheetOrder.Where(f => ParticleScore(f) > 0).ToList()
            : new List<int>(_sheetOrder);

        _rebuildingSheets = true;
        _sheetSelect.Clear();
        int total = _listedSheets.Sum(f => _bySheet[f].Count);

        // Ordered by how likely each group is to hold what someone wants:
        // curated spell effects, then every animation, then still sprites.
        bool hasFx = _spellFx.Count > 0;
        if (hasFx)
            _sheetSelect.AddItem($"★ Efectos de hechizos — {_spellFx.Count} (los del juego)");
        _sheetSelect.AddItem($"Todas las animaciones — {_animated.Count}");
        _sheetSelect.AddItem($"Sprites sueltos ({_listedSheets.Count} láminas · {total})");
        foreach (int file in _listedSheets)
        {
            var ids = _bySheet[file];
            int side = Grhs[ids[0]].PixelWidth;
            _sheetSelect.AddItem($"    {file}.png — {ids.Count} de {side}px");
        }
        _sheetFilter = hasFx ? SpellFxGroup : AnimatedGroup;
        _sheetSelect.Selected = 0;
        _rebuildingSheets = false;
    }

    /// <summary>
    /// Ranks how likely a sheet is to hold particle art. Particle atlases are
    /// uniform squares; 128px and 64px cells are the sizes the effect sheets
    /// use, while 32px grids are overwhelmingly map tiles.
    /// </summary>
    private int ParticleScore(int fileNum)
    {
        if (Grhs == null || !_bySheet.TryGetValue(fileNum, out var ids) || ids.Count < 4) return 0;
        var first = Grhs[ids[0]];
        if (first.PixelWidth != first.PixelHeight) return 0;
        int side = first.PixelWidth;
        if (side != 64 && side != 128) return 0;

        foreach (int id in ids)
        {
            var g = Grhs[id];
            if (g.PixelWidth != side || g.PixelHeight != side) return 0;
        }
        return side == 128 ? 2 : 1;
    }

    // ── Filtering / paging ────────────────────────────────────────────

    private void ApplyFilter()
    {
        // Can be reached from a dropdown callback before _Ready finishes wiring
        // the grid, so bail out until the UI actually exists.
        if (_grid == null) return;

        _visible = new List<int>();
        if (Grhs == null) { PopulateGrid(); return; }

        string query = (_searchBox?.Text ?? "").Trim();
        int lo = -1, hi = -1;
        if (query.Length > 0)
        {
            int dash = query.IndexOf('-');
            if (dash > 0 && int.TryParse(query[..dash], out int a) && int.TryParse(query[(dash + 1)..], out int b))
            {
                lo = Math.Min(a, b); hi = Math.Max(a, b);
            }
            else if (int.TryParse(query, out int single)) { lo = single; hi = single; }
        }

        IEnumerable<int> source =
            _sheetFilter == SpellFxGroup ? _spellFx
            : _sheetFilter == AnimatedGroup ? _animated
            : _sheetFilter >= 0 && _bySheet.TryGetValue(_sheetFilter, out var sheetIds) ? sheetIds
            : _listedSheets.SelectMany(f => _bySheet[f]);

        int sizeMode = _sizeSelect?.Selected ?? 0;
        foreach (int id in source)
        {
            if (lo >= 0 && (id < lo || id > hi)) continue;
            if (sizeMode > 0 && !MatchesSize(id, sizeMode)) continue;
            _visible.Add(id);
        }

        PopulateGrid();
    }

    /// <summary>
    /// Size bucket test. Animated GRHs carry no dimensions of their own, so the
    /// first frame stands in for the whole effect.
    /// </summary>
    private bool MatchesSize(int grhId, int mode)
    {
        if (Grhs == null || grhId <= 0 || grhId >= Grhs.Length) return false;
        var grh = Grhs[grhId];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int f = grh.Frames[0];
            if (f <= 0 || f >= Grhs.Length) return false;
            grh = Grhs[f];
        }
        int side = Math.Max(grh.PixelWidth, grh.PixelHeight);
        return mode switch
        {
            1 => side <= 32,
            2 => side > 32 && side <= 96,
            3 => side > 96,
            _ => true,
        };
    }

    /// <summary>Rebuilds from scratch: clears the grid and shows the first chunk.</summary>
    private void PopulateGrid()
    {
        if (_grid == null) return;
        foreach (var child in _grid.GetChildren()) child.QueueFree();
        _shownCount = 0;
        if (_scroll != null) _scroll.ScrollVertical = 0;
        GrowGrid();
    }

    /// <summary>
    /// Appends the next chunk. Growth is only ever triggered by an explicit
    /// click: driving it from the scrollbar fed back into itself — adding rows
    /// changed MaxValue, which re-fired the handler and locked up the editor.
    /// </summary>
    private void GrowGrid()
    {
        if (_grid == null) return;
        int target = Math.Min(_shownCount + ChunkRows * Columns, _visible.Count);
        for (int i = _shownCount; i < target; i++)
            _grid.AddChild(BuildSpriteButton(_visible[i]));
        _shownCount = target;
        UpdateCountLabel();
    }

    private void UpdateCountLabel()
    {
        if (_pageLabel == null) return;
        bool complete = _shownCount >= _visible.Count;
        _pageLabel.Text = _visible.Count == 0
            ? "Sin sprites — probá otra lámina o destildá el filtro"
            : complete
                ? $"{_visible.Count} sprites"
                : $"Mostrando {_shownCount} de {_visible.Count}";
        if (_moreBtn != null)
        {
            _moreBtn.Visible = !complete;
            int remaining = _visible.Count - _shownCount;
            int next = Math.Min(ChunkRows * Columns, remaining);
            _moreBtn.Text = $"Ver {next} más  ({remaining} restantes)";
        }
    }

    private void UpdateBackdrop()
    {
        if (_gridBackdrop == null) return;
        bool lit = _litBgCheck?.ButtonPressed ?? true;
        var color = lit ? new Color(0.42f, 0.44f, 0.50f) : new Color(0.05f, 0.05f, 0.06f);
        _gridBackdrop.AddThemeStyleboxOverride("panel",
            EditorTheme.FlatBox(color, 3, 4, 4, EditorTheme.BORDER, 1));
    }

    private Control BuildSpriteButton(int grhId)
    {
        var btn = new TextureButton
        {
            CustomMinimumSize = new Vector2(PreviewSize, PreviewSize),
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            IgnoreTextureSize = true,
        };

        int usedTimes = _chosen.Count(c => c == grhId);
        var grh = Grhs != null && grhId > 0 && grhId < Grhs.Length ? Grhs[grhId] : null;
        bool animated = grh is { NumFrames: > 1 };
        string spec = grh == null ? "sin datos"
            : animated ? $"animación de {grh.NumFrames} cuadros"
            : $"{grh.PixelWidth}x{grh.PixelHeight} de {grh.FileNum}.png";
        btn.TooltipText = usedTimes > 0
            ? $"GRH {grhId} — {spec}\nYa usado {usedTimes}x — clic para repetir"
            : $"GRH {grhId} — {spec}\nClic para agregar";

        var preview = GetOrCreatePreview(grhId);
        if (preview != null) btn.TextureNormal = preview;
        btn.Pressed += () =>
        {
            _chosen.Add(grhId);
            RebuildChosenList();
            RefreshBadges();    // cheap in-place update, keeps scroll position
            OnChanged?.Invoke();
        };

        // Every cell is wrapped so a count badge can be shown or hidden later
        // without rebuilding the grid and losing the user's scroll position.
        var wrap = new Control { CustomMinimumSize = new Vector2(PreviewSize, PreviewSize) };
        wrap.SetMeta("grhId", grhId);
        btn.SetAnchorsPreset(LayoutPreset.FullRect);
        wrap.AddChild(btn);

        var badge = EditorTheme.MakeLabel(usedTimes > 0 ? $"x{usedTimes}" : "", Colors.White, EditorTheme.FONT_XS);
        badge.Name = "Badge";
        badge.Position = new Vector2(2, 2);
        badge.MouseFilter = MouseFilterEnum.Ignore;
        badge.AddThemeColorOverride("font_outline_color", Colors.Black);
        badge.AddThemeConstantOverride("outline_size", 3);
        badge.Visible = usedTimes > 0;
        wrap.AddChild(badge);

        if (animated)
        {
            // Films the cell so an animated effect is obvious next to still art.
            var mark = EditorTheme.MakeLabel("▶", new Color(1f, 0.85f, 0.3f), EditorTheme.FONT_XS);
            mark.Position = new Vector2(PreviewSize - 14, PreviewSize - 16);
            mark.MouseFilter = MouseFilterEnum.Ignore;
            mark.AddThemeColorOverride("font_outline_color", Colors.Black);
            mark.AddThemeConstantOverride("outline_size", 3);
            wrap.AddChild(mark);
        }
        return wrap;
    }

    /// <summary>Updates the "already chosen" counters without touching the grid layout.</summary>
    private void RefreshBadges()
    {
        if (_grid == null) return;
        foreach (var child in _grid.GetChildren())
        {
            if (child is not Control cell) continue;
            var meta = cell.GetMeta("grhId", -1);
            int grhId = (int)meta;
            if (grhId < 0) continue;
            if (cell.GetNodeOrNull<Label>("Badge") is not { } badge) continue;

            int n = _chosen.Count(c => c == grhId);
            badge.Text = n > 0 ? $"x{n}" : "";
            badge.Visible = n > 0;
        }
    }

    // ── Chosen list ───────────────────────────────────────────────────

    private void RebuildChosenList()
    {
        if (_chosenList == null) return;
        foreach (var child in _chosenList.GetChildren()) child.QueueFree();

        if (_chosenCountLabel != null)
        {
            _chosenCountLabel.Text = _chosen.Count == 1
                ? "1 sprite elegido"
                : $"{_chosen.Count} sprites elegidos";
        }

        for (int i = 0; i < _chosen.Count; i++)
        {
            int grhId = _chosen[i];
            int capturedIndex = i;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            // Thumbnails need the same lighter ground as the grid to be legible.
            var thumbBg = new Panel { CustomMinimumSize = new Vector2(28, 28) };
            thumbBg.AddThemeStyleboxOverride("panel",
                EditorTheme.FlatBox(new Color(0.42f, 0.44f, 0.50f), 2, 0, 0));
            var thumb = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            thumb.SetAnchorsPreset(LayoutPreset.FullRect);
            var preview = GetOrCreatePreview(grhId);
            if (preview != null) thumb.Texture = preview;
            thumbBg.AddChild(thumb);
            row.AddChild(thumbBg);

            var label = EditorTheme.MakeLabel($"GRH {grhId}", EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);

            var upBtn = EditorTheme.MakeButton("↑");
            upBtn.CustomMinimumSize = new Vector2(24, 20);
            upBtn.Disabled = capturedIndex == 0;
            upBtn.Pressed += () => MoveChosen(capturedIndex, capturedIndex - 1);
            row.AddChild(upBtn);

            var downBtn = EditorTheme.MakeButton("↓");
            downBtn.CustomMinimumSize = new Vector2(24, 20);
            downBtn.Disabled = capturedIndex == _chosen.Count - 1;
            downBtn.Pressed += () => MoveChosen(capturedIndex, capturedIndex + 1);
            row.AddChild(downBtn);

            var dupBtn = EditorTheme.MakeButton("+1");
            dupBtn.TooltipText = "Repetir (aumenta la probabilidad de que aparezca este sprite)";
            dupBtn.CustomMinimumSize = new Vector2(28, 20);
            dupBtn.Pressed += () =>
            {
                _chosen.Insert(capturedIndex + 1, grhId);
                RebuildChosenList();
                RefreshBadges();
                OnChanged?.Invoke();
            };
            row.AddChild(dupBtn);

            var delBtn = EditorTheme.DangerButton("X");
            delBtn.CustomMinimumSize = new Vector2(22, 20);
            delBtn.Pressed += () =>
            {
                _chosen.RemoveAt(capturedIndex);
                RebuildChosenList();
                RefreshBadges();
                OnChanged?.Invoke();
            };
            row.AddChild(delBtn);

            _chosenList.AddChild(row);
        }
    }

    private void MoveChosen(int from, int to)
    {
        if (to < 0 || to >= _chosen.Count) return;
        (_chosen[from], _chosen[to]) = (_chosen[to], _chosen[from]);
        RebuildChosenList();
        OnChanged?.Invoke();
    }

    private Texture2D? GetOrCreatePreview(int grhId)
    {
        if (_previewCache.TryGetValue(grhId, out var cached)) return cached;

        Texture2D? preview = null;
        if (Grhs != null && Textures != null && grhId > 0 && grhId < Grhs.Length)
        {
            var grh = Grhs[grhId];
            if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
            {
                int fIdx = grh.Frames[0];
                if (fIdx > 0 && fIdx < Grhs.Length) grh = Grhs[fIdx];
            }
            if (grh.FileNum > 0 && grh.PixelWidth > 0 && grh.PixelHeight > 0)
            {
                var srcTex = Textures.GetTexture(grh.FileNum);
                if (srcTex != null)
                {
                    int cropW = Math.Min(grh.PixelWidth, srcTex.GetWidth() - grh.SX);
                    int cropH = Math.Min(grh.PixelHeight, srcTex.GetHeight() - grh.SY);
                    if (grh.SX >= 0 && grh.SY >= 0 && cropW > 0 && cropH > 0)
                    {
                        preview = new AtlasTexture
                        {
                            Atlas = srcTex,
                            Region = new Rect2(grh.SX, grh.SY, cropW, cropH),
                        };
                    }
                }
            }
        }
        _previewCache[grhId] = preview;
        return preview;
    }
}

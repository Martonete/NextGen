#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Embeddable control for building a particle's Grh_List. A navigable grid of GRH
/// sprites (browse-by-default, no need to know IDs) plus a reorderable side list of
/// chosen sprites that supports repeats (the same GRH can appear more than once in
/// Grh_List to weight how often it's picked at spawn — see ParticleEngine.SpawnParticle).
///
/// Particle sprites live in the graphics atlas by raw GRH ID; real particle
/// definitions cluster in 27400–27700 (with a few in 20800s and 32700s), so the
/// grid opens on that "Partículas" preset. Presets + free ID/range search + paging
/// let the user actually SEE what's available instead of guessing numbers.
/// </summary>
public partial class GrhMultiPicker : VBoxContainer
{
    public GrhData[]? Grhs;
    public TextureManager? Textures;

    /// <summary>Fires whenever the chosen list changes (add/remove/reorder).</summary>
    public Action? OnChanged;

    private readonly List<int> _chosen = new();
    private readonly Dictionary<int, Texture2D?> _previewCache = new();

    private LineEdit? _searchBox;
    private GridContainer? _grid;
    private VBoxContainer? _chosenList;
    private Label? _chosenCountLabel;
    private Label? _pageLabel;
    private Button? _prevBtn, _nextBtn;

    private const int PreviewSize = 40;
    private const int Columns = 6;
    private const int PageSize = 48; // valid GRH shown per page

    // Preset ranges (min, max) covering where particle sprites actually live.
    private static readonly (string name, int lo, int hi)[] Presets =
    {
        ("Partículas", 27400, 27700),
        ("Set 20800", 20800, 20840),
        ("Todos", 1, int.MaxValue),
    };

    private int _rangeLo = 27400;
    private int _rangeHi = 27700;
    private int _pageStartId = 1;               // first GRH id of the current page
    private readonly List<int> _pageStack = new(); // page-start ids, for "prev"

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);

        AddChild(EditorTheme.SectionLabel("Sprites (Grh_List)"));

        // Preset range buttons — instant "show me these" filters.
        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 4);
        foreach (var (name, lo, hi) in Presets)
        {
            var b = EditorTheme.MakeButton(name);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            int clo = lo, chi = hi;
            b.Pressed += () =>
            {
                _rangeLo = clo; _rangeHi = chi;
                if (_searchBox != null) _searchBox.Text = "";
                ResetToFirstPage();
            };
            presetRow.AddChild(b);
        }
        AddChild(presetRow);

        _searchBox = new LineEdit { PlaceholderText = "Buscar ID o rango (ej. 27450 o 27450-27460)..." };
        _searchBox.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _searchBox.TextChanged += _ => OnSearchChanged();
        AddChild(_searchBox);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 150),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        _grid = new GridContainer { Columns = Columns };
        _grid.AddThemeConstantOverride("h_separation", 3);
        _grid.AddThemeConstantOverride("v_separation", 3);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_grid);

        // Pager
        var pager = new HBoxContainer();
        pager.AddThemeConstantOverride("separation", 6);
        _prevBtn = EditorTheme.MakeButton("◀", GoPrevPage);
        _prevBtn.CustomMinimumSize = new Vector2(32, 0);
        pager.AddChild(_prevBtn);
        _pageLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _pageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        pager.AddChild(_pageLabel);
        _nextBtn = EditorTheme.MakeButton("▶", GoNextPage);
        _nextBtn.CustomMinimumSize = new Vector2(32, 0);
        pager.AddChild(_nextBtn);
        AddChild(pager);

        AddChild(EditorTheme.MakeHSeparator());

        var chosenHeader = new HBoxContainer();
        chosenHeader.AddThemeConstantOverride("separation", 6);
        _chosenCountLabel = EditorTheme.MakeLabel("0 sprites elegidos", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _chosenCountLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        chosenHeader.AddChild(_chosenCountLabel);
        AddChild(chosenHeader);

        var chosenScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 100),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(chosenScroll);

        _chosenList = new VBoxContainer();
        _chosenList.AddThemeConstantOverride("separation", 2);
        chosenScroll.AddChild(_chosenList);

        ResetToFirstPage();
    }

    /// <summary>Reset the chosen list to a specific Grh_List (e.g. when opening the edit popup).</summary>
    public void SetChosen(int[] grhList)
    {
        _chosen.Clear();
        _chosen.AddRange(grhList);
        RebuildChosenList();
    }

    public int[] GetChosen() => _chosen.ToArray();

    // ── Browsing ──────────────────────────────────────────────────────

    private void OnSearchChanged()
    {
        string query = (_searchBox?.Text ?? "").Trim();
        if (query.Length == 0) { ResetToFirstPage(); return; }

        // A search overrides the browse range for as long as there's text.
        int dash = query.IndexOf('-');
        if (dash > 0 && int.TryParse(query[..dash], out int lo) && int.TryParse(query[(dash + 1)..], out int hi))
        {
            _rangeLo = Math.Min(lo, hi); _rangeHi = Math.Max(lo, hi);
            ResetToFirstPage();
        }
        else if (int.TryParse(query, out int single))
        {
            _rangeLo = single; _rangeHi = single;
            ResetToFirstPage();
        }
        // else: partial/garbage input — keep current page, don't clear.
    }

    private void ResetToFirstPage()
    {
        _pageStack.Clear();
        _pageStartId = Math.Max(_rangeLo, 1);
        PopulateGrid();
    }

    private void GoNextPage()
    {
        if (Grhs == null) return;
        // Find the id right after the last valid one shown this page.
        int lastShown = LastValidIdOnPage(_pageStartId);
        if (lastShown < 0) return;
        int next = lastShown + 1;
        if (next > _rangeHi || next >= Grhs.Length) return;
        _pageStack.Add(_pageStartId);
        _pageStartId = next;
        PopulateGrid();
    }

    private void GoPrevPage()
    {
        if (_pageStack.Count == 0) return;
        _pageStartId = _pageStack[^1];
        _pageStack.RemoveAt(_pageStack.Count - 1);
        PopulateGrid();
    }

    /// <summary>Id of the last valid GRH that fits on the page starting at startId.</summary>
    private int LastValidIdOnPage(int startId)
    {
        if (Grhs == null) return -1;
        int shown = 0, last = -1;
        int hi = Math.Min(_rangeHi, Grhs.Length - 1);
        for (int id = startId; id <= hi && shown < PageSize; id++)
        {
            if (!Grhs[id].IsValid) continue;
            last = id;
            shown++;
        }
        return last;
    }

    private void PopulateGrid()
    {
        if (_grid == null || Grhs == null) return;
        foreach (var child in _grid.GetChildren()) child.QueueFree();

        int shown = 0, count = 0;
        int hi = Math.Min(_rangeHi, Grhs.Length - 1);
        for (int id = _pageStartId; id <= hi && shown < PageSize; id++)
        {
            if (!Grhs[id].IsValid) continue;
            _grid.AddChild(BuildSpriteButton(id));
            shown++;
            count = id;
        }

        // Pager state
        if (_pageLabel != null)
            _pageLabel.Text = shown == 0
                ? "Sin sprites en este rango"
                : $"GRH {_pageStartId}–{count}  ({shown})";
        if (_prevBtn != null) _prevBtn.Disabled = _pageStack.Count == 0;
        if (_nextBtn != null)
        {
            int last = LastValidIdOnPage(_pageStartId);
            _nextBtn.Disabled = last < 0 || last + 1 > hi;
        }
    }

    private Control BuildSpriteButton(int grhId)
    {
        var btn = new TextureButton
        {
            CustomMinimumSize = new Vector2(PreviewSize, PreviewSize),
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            IgnoreTextureSize = true,
            TooltipText = $"GRH {grhId} — click para agregar",
        };
        var preview = GetOrCreatePreview(grhId);
        if (preview != null) btn.TextureNormal = preview;
        btn.Pressed += () =>
        {
            _chosen.Add(grhId);
            RebuildChosenList();
            OnChanged?.Invoke();
        };
        return btn;
    }

    // ── Chosen list ───────────────────────────────────────────────────

    private void RebuildChosenList()
    {
        if (_chosenList == null) return;
        foreach (var child in _chosenList.GetChildren()) child.QueueFree();

        if (_chosenCountLabel != null)
            _chosenCountLabel.Text = _chosen.Count == 1 ? "1 sprite elegido" : $"{_chosen.Count} sprites elegidos";

        for (int i = 0; i < _chosen.Count; i++)
        {
            int grhId = _chosen[i];
            int capturedIndex = i;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            var thumb = new TextureRect
            {
                CustomMinimumSize = new Vector2(24, 24),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            var preview = GetOrCreatePreview(grhId);
            if (preview != null) thumb.Texture = preview;
            row.AddChild(thumb);

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
                OnChanged?.Invoke();
            };
            row.AddChild(dupBtn);

            var delBtn = EditorTheme.DangerButton("X");
            delBtn.CustomMinimumSize = new Vector2(22, 20);
            delBtn.Pressed += () =>
            {
                _chosen.RemoveAt(capturedIndex);
                RebuildChosenList();
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

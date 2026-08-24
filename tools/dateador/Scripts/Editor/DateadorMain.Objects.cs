#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AODateador.Data;
using Godot;

namespace AODateador.Editor;

/// <summary>
/// The Objetos tab: browse obj.dat with each item's icon, and edit the fields
/// that apply to its type.
///
/// Split out of DateadorMain because it is the only tab that also loads the
/// graphics catalogue. The fields are generated from <see cref="FieldDefs"/>
/// rather than declared one by one, so a food item shows MinHAM and nothing
/// about weapons — the previous version showed every section at once, with the
/// wrong key names on half of them.
/// </summary>
public partial class DateadorMain
{
    // Graphics, loaded lazily the first time the tab needs them.
    private GrhData[]? _grhs;
    private TextureManager? _textures;
    private GrhCatalogIndex? _grhCatalog;
    private string _resourcesDir = "";

    private VBoxContainer? _objFields;
    private ItemList? _objItemList;
    private LineEdit? _objSearch;
    private OptionButton? _objTypeFilter;
    private CheckBox? _objOnlyDuplicates;
    private Label? _objListInfo;
    private TextureRect? _objPreview;
    private Label? _objPreviewLabel;

    /// <summary>Object numbers currently listed, by row.</summary>
    private readonly List<int> _objRows = new();

    /// <summary>How many objects use each GrhIndex, for the duplicate badge.</summary>
    private readonly Dictionary<int, int> _grhUsage = new();

    /// <summary>Suppresses change events while fields are being populated.</summary>
    private bool _objLoading;

    private Control BuildObjTab()
    {
        var split = new HSplitContainer { SplitOffset = 340 };
        split.AddThemeConstantOverride("separation", 4);

        split.AddChild(BuildObjListPane());
        split.AddChild(BuildObjInspectorPane());
        return split;
    }

    private Control BuildObjListPane()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);

        _objSearch = DateadorTheme.MakeLineEdit("Buscar por nombre o número…");
        _objSearch.TextChanged += _ => RefreshObjList();
        column.AddChild(_objSearch);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 4);

        var typeNames = new List<string> { "Todos los tipos" };
        typeNames.AddRange(FieldDefs.TypeNames.OrderBy(kv => kv.Key)
                                              .Select(kv => $"{kv.Key} {kv.Value}"));
        _objTypeFilter = DateadorTheme.MakeOptionButton(typeNames.ToArray());
        _objTypeFilter.ItemSelected += _ => RefreshObjList();
        filterRow.AddChild(_objTypeFilter);
        column.AddChild(filterRow);

        // The whole point of the pass: find items sharing an icon.
        _objOnlyDuplicates = DateadorTheme.MakeCheckBox("Sólo gráficos repetidos");
        _objOnlyDuplicates.Toggled += _ => RefreshObjList();
        column.AddChild(_objOnlyDuplicates);

        _objListInfo = DateadorTheme.MakeLabel("", DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
        column.AddChild(_objListInfo);

        _objItemList = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            IconMode = ItemList.IconModeEnum.Left,
            FixedIconSize = new Vector2I(28, 28),
        };
        _objItemList.AddThemeStyleboxOverride("panel",
            DateadorTheme.FlatBox(DateadorTheme.BG_INPUT, 4, 2, 2));
        _objItemList.ItemSelected += OnObjRowSelected;
        column.AddChild(_objItemList);

        return column;
    }

    private Control BuildObjInspectorPane()
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);

        // Header: the icon, big, next to the button that changes it.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);

        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", DateadorTheme.FlatBox(DateadorTheme.BG_INPUT, 4, 6, 6));
        _objPreview = new TextureRect
        {
            CustomMinimumSize = new Vector2(64, 64),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        frame.AddChild(_objPreview);
        header.AddChild(frame);

        var headerText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _objPreviewLabel = DateadorTheme.MakeLabel("Sin selección", DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
        _objPreviewLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        headerText.AddChild(_objPreviewLabel);
        headerText.AddChild(DateadorTheme.PrimaryButton("Cambiar gráfico…", OpenGrhPicker));
        header.AddChild(headerText);

        column.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);

        _objFields = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _objFields.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_objFields);

        return column;
    }

    /// <summary>
    /// Loads Graficos.ind and the sheet catalogue. Deferred until the tab is
    /// first used: it is a few hundred MB of textures the other tabs never need.
    /// </summary>
    private void EnsureGraphicsLoaded()
    {
        if (_grhs != null) return;

        _resourcesDir = FindResourcesDir();
        if (_resourcesDir.Length == 0)
        {
            GD.PushWarning("[Dateador] No encontré resources/data; los objetos se verán sin gráfico.");
            _grhs = Array.Empty<GrhData>();
            return;
        }

        string indPath = Path.Combine(_resourcesDir, "INIT", "Graficos.ind");
        if (File.Exists(indPath))
        {
            _grhs = GrhLoader.Load(indPath);
            _textures = new TextureManager(Path.Combine(_resourcesDir, "Graficos"));
            _grhCatalog = GrhCatalogIndex.Load(Path.Combine(_resourcesDir, "INIT"));
        }
        else
        {
            GD.PushWarning($"[Dateador] No encontré {indPath}");
            _grhs = Array.Empty<GrhData>();
        }
    }

    /// <summary>
    /// Walks up from the dat directory looking for resources/data, so the tool
    /// works wherever the repo lives.
    /// </summary>
    private string FindResourcesDir()
    {
        var dir = new DirectoryInfo(_datDir.Length > 0
            ? _datDir
            : ProjectSettings.GlobalizePath("res://"));

        for (int depth = 0; depth < 8 && dir != null; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "resources", "data");
            if (Directory.Exists(Path.Combine(candidate, "INIT"))) return candidate;
        }
        return "";
    }

    /// <summary>Recounts icon usage, which drives the duplicate badge.</summary>
    private void RecountGrhUsage()
    {
        _grhUsage.Clear();
        if (_objDb == null) return;
        foreach (var obj in _objDb.All)
        {
            int grh = obj.GrhIndex;
            if (grh > 0) _grhUsage[grh] = _grhUsage.GetValueOrDefault(grh) + 1;
        }
    }

    private void RefreshObjList()
    {
        if (_objItemList == null || _objDb == null) return;
        EnsureGraphicsLoaded();
        RecountGrhUsage();

        _objItemList.Clear();
        _objRows.Clear();

        string search = _objSearch?.Text.Trim() ?? "";
        int typeFilter = -1;
        if (_objTypeFilter != null && _objTypeFilter.Selected > 0)
        {
            string label = _objTypeFilter.GetItemText(_objTypeFilter.Selected);
            int.TryParse(label.Split(' ')[0], out typeFilter);
        }
        bool onlyDupes = _objOnlyDuplicates?.ButtonPressed ?? false;

        int shown = 0, duplicates = 0;
        foreach (var obj in _objDb.All)
        {
            if (typeFilter >= 0 && obj.ObjType != typeFilter) continue;

            int uses = _grhUsage.GetValueOrDefault(obj.GrhIndex);
            bool isDupe = obj.GrhIndex > 0 && uses > 1;
            if (isDupe) duplicates++;
            if (onlyDupes && !isDupe) continue;

            if (search.Length > 0
                && !obj.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !obj.Index.ToString().Contains(search, StringComparison.Ordinal))
                continue;

            string label = $"#{obj.Index}  {obj.Name}";
            if (isDupe) label += $"   ⚠ ×{uses}";
            if (obj.IsDirty) label = "● " + label;

            int row = _objItemList.AddItem(label, GrhIcons.Get(_grhs, _textures, obj.GrhIndex));
            if (isDupe) _objItemList.SetItemCustomFgColor(row, new Color(1f, 0.75f, 0.35f));
            _objRows.Add(obj.Index);
            shown++;
        }

        if (_objListInfo != null)
            _objListInfo.Text = $"{shown} objetos · {duplicates} con gráfico repetido";
    }

    private void OnObjRowSelected(long row)
    {
        if (_objDb == null || row < 0 || row >= _objRows.Count) return;
        _currentObj = _objDb.Objects.GetValueOrDefault(_objRows[(int)row]);
        ShowObjFields();
    }

    /// <summary>Rebuilds the inspector for the selected object's type.</summary>
    private void ShowObjFields()
    {
        if (_objFields == null) return;
        foreach (var child in _objFields.GetChildren()) child.QueueFree();

        if (_currentObj == null)
        {
            if (_objPreview != null) _objPreview.Texture = null;
            if (_objPreviewLabel != null) _objPreviewLabel.Text = "Sin selección";
            return;
        }

        UpdateObjPreview();

        _objLoading = true;
        string section = "";
        foreach (var field in FieldDefs.For(_currentObj.ObjType))
        {
            string fieldSection = FieldDefs.SectionOf(field);
            if (fieldSection != section)
            {
                section = fieldSection;
                _objFields.AddChild(SectionHeader(section));
            }
            _objFields.AddChild(BuildFieldRow(field));
        }

        _objFields.AddChild(SectionHeader("Clases que no pueden usarlo"));
        _objFields.AddChild(BuildClassRestrictions());
        _objLoading = false;
    }

    private Control SectionHeader(string text)
    {
        var label = DateadorTheme.MakeLabel(text.ToUpperInvariant(),
            DateadorTheme.TEXT_DIM, DateadorTheme.FONT_SM);
        label.CustomMinimumSize = new Vector2(0, 24);
        label.VerticalAlignment = VerticalAlignment.Bottom;
        return label;
    }

    private Control BuildFieldRow(FieldDef field)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var label = DateadorTheme.MakeLabel(field.Label, DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
        label.CustomMinimumSize = new Vector2(150, 0);
        if (field.Hint != null) label.TooltipText = field.Hint;
        row.AddChild(label);

        row.AddChild(BuildFieldEditor(field));
        return row;
    }

    private Control BuildFieldEditor(FieldDef field)
    {
        var obj = _currentObj!;

        switch (field.Kind)
        {
            case FieldKind.Text:
            {
                var edit = DateadorTheme.MakeLineEdit(field.Label);
                edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                edit.Text = obj.Get(field.Key) ?? "";
                edit.TextChanged += text => { if (!_objLoading) SetObjField(field.Key, text); };
                return edit;
            }

            case FieldKind.Bool:
            {
                var check = DateadorTheme.MakeCheckBox("");
                check.ButtonPressed = obj.GetBool(field.Key);
                check.Toggled += on => { if (!_objLoading) SetObjField(field.Key, on ? "1" : "0"); };
                return check;
            }

            case FieldKind.ObjTypeSelect:
            {
                var types = FieldDefs.TypeNames.OrderBy(kv => kv.Key).ToList();
                var picker = DateadorTheme.MakeOptionButton(
                    types.Select(kv => $"{kv.Key} {kv.Value}").ToArray());
                int index = types.FindIndex(kv => kv.Key == obj.ObjType);
                if (index >= 0) picker.Selected = index;
                picker.ItemSelected += selected =>
                {
                    if (_objLoading) return;
                    SetObjField(field.Key, types[(int)selected].Key.ToString());
                    // Changing the type changes which fields apply.
                    ShowObjFields();
                };
                return picker;
            }

            case FieldKind.Grh:
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 4);
                var spin = DateadorTheme.MakeSpinBox(0, int.MaxValue, 1, 120);
                spin.Value = obj.GrhIndex;
                spin.ValueChanged += value =>
                {
                    if (_objLoading) return;
                    SetObjField(field.Key, ((int)value).ToString());
                    UpdateObjPreview();
                };
                row.AddChild(spin);
                row.AddChild(DateadorTheme.SecondaryButton("Elegir…", OpenGrhPicker));
                return row;
            }

            default:
            {
                var spin = DateadorTheme.MakeSpinBox(field.Min, field.Max, 1, 120);
                spin.Value = obj.GetInt(field.Key);
                spin.ValueChanged += value =>
                {
                    if (!_objLoading) SetObjField(field.Key, ((int)value).ToString());
                };
                return spin;
            }
        }
    }

    /// <summary>
    /// Class restrictions as toggles over CP1..CP16. They are stored as class
    /// names packed from CP1, so the set is rewritten whole on every change.
    /// </summary>
    private Control BuildClassRestrictions()
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 2);

        var current = _currentObj!.ProhibitedClasses();
        foreach (string className in FieldDefs.ClassNames)
        {
            var check = DateadorTheme.MakeCheckBox(className);
            check.ButtonPressed = current.Contains(className, StringComparer.OrdinalIgnoreCase);
            check.AddThemeFontSizeOverride("font_size", DateadorTheme.FONT_SM);
            check.Toggled += _ =>
            {
                if (_objLoading) return;
                var selected = new List<string>();
                foreach (var node in flow.GetChildren())
                    if (node is CheckBox cb && cb.ButtonPressed) selected.Add(cb.Text);
                _currentObj.SetProhibitedClasses(selected);
                MarkObjDirty();
            };
            flow.AddChild(check);
        }
        return flow;
    }

    private void SetObjField(string key, string value)
    {
        if (_currentObj == null) return;
        _currentObj.Set(key, value);
        MarkObjDirty();
    }

    private void MarkObjDirty()
    {
        _dirty = true;
        // The row label carries the dirty marker and the duplicate count, both
        // of which may have just changed.
        int selected = _objItemList?.GetSelectedItems().FirstOrDefault() ?? -1;
        RefreshObjList();
        if (selected >= 0 && selected < (_objItemList?.ItemCount ?? 0))
            _objItemList!.Select(selected);
    }

    private void UpdateObjPreview()
    {
        if (_currentObj == null) return;
        EnsureGraphicsLoaded();

        if (_objPreview != null)
            _objPreview.Texture = GrhIcons.Get(_grhs, _textures, _currentObj.GrhIndex);

        if (_objPreviewLabel != null)
        {
            int uses = _grhUsage.GetValueOrDefault(_currentObj.GrhIndex);
            string shared = uses > 1 ? $"\n⚠ compartido con {uses - 1} objeto(s) más" : "";
            _objPreviewLabel.Text =
                $"#{_currentObj.Index} · {FieldDefs.TypeName(_currentObj.ObjType)}"
                + $"\nGRH {_currentObj.GrhIndex}{shared}";
        }
    }

    private void OpenGrhPicker()
    {
        if (_currentObj == null) return;
        EnsureGraphicsLoaded();

        var picker = new GrhPickerPopup
        {
            Grhs = _grhs,
            Textures = _textures,
            Catalog = _grhCatalog,
            CurrentGrh = _currentObj.GrhIndex,
            PreferredClass = GrhCatalogIndex.ClassForObjType(_currentObj.ObjType),
        };
        picker.GrhChosen += grhIndex =>
        {
            SetObjField("GrhIndex", grhIndex.ToString());
            ShowObjFields();
            picker.QueueFree();
        };
        AddChild(picker);
        picker.PopupCentered();
    }

    /// <summary>
    /// Writes obj.dat and keeps the sibling copies in step.
    ///
    /// Three copies exist — server/dat, resources/data/INIT and client/Data/INIT
    /// — and nothing in the repo syncs them; it has been done by hand until now,
    /// which is how they drift. Only the ones that already exist are written, so
    /// this never creates a copy where the checkout does not have one.
    /// </summary>
    private void SaveObjects()
    {
        if (_objDb == null || !_objDb.IsDirty) return;

        var mirrors = new List<string>();
        foreach (string relative in new[]
                 {
                     Path.Combine("resources", "data", "INIT", "Obj.dat"),
                     Path.Combine("client", "Data", "INIT", "obj.dat"),
                 })
        {
            string? path = FindRepoFile(relative);
            if (path != null && !PathsEqual(path, _objDb.SourcePath)) mirrors.Add(path);
        }

        int changed = _objDb.Save(mirrors);
        GD.Print($"[Dateador] obj.dat: {changed} objetos guardados"
               + (mirrors.Count > 0 ? $", {mirrors.Count} copias sincronizadas" : ""));

        RefreshObjList();
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a repo-relative path by walking up from the dat directory.</summary>
    private string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(_datDir.Length > 0
            ? _datDir
            : ProjectSettings.GlobalizePath("res://"));

        for (int depth = 0; depth < 8 && dir != null; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ── Data directory: remembered between runs ───────────────────────────

    private const string ConfigFileName = "dateador_config.ini";
    private const string DatDirKey = "dat_dir";

    private static string ConfigPath()
        => Path.Combine(ProjectSettings.GlobalizePath("res://"), ConfigFileName);

    private void SaveDatDirToConfig(string datDir)
    {
        try { File.WriteAllText(ConfigPath(), $"{DatDirKey}={datDir}\n"); }
        catch (Exception ex) { GD.PushWarning($"[Dateador] No pude guardar la config: {ex.Message}"); }
    }

    /// <summary>
    /// Directory to open on startup: whatever was used last, else server/dat
    /// found by walking up from the project. Without this the tool opens empty
    /// and the folder has to be picked by hand on every run.
    /// </summary>
    private string? ResolveStartupDatDir()
    {
        try
        {
            string config = ConfigPath();
            if (File.Exists(config))
            {
                foreach (string line in File.ReadAllLines(config))
                {
                    if (!line.StartsWith(DatDirKey + "=", StringComparison.Ordinal)) continue;
                    string saved = line[(DatDirKey.Length + 1)..].Trim();
                    if (Directory.Exists(saved)) return saved;
                }
            }
        }
        catch (Exception ex) { GD.PushWarning($"[Dateador] Config ilegible: {ex.Message}"); }

        var dir = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        for (int depth = 0; depth < 8 && dir != null; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "server", "dat");
            if (File.Exists(Path.Combine(candidate, "obj.dat"))) return candidate;
        }
        return null;
    }

    /// <summary>Adds an object at the end, seeded with the minimum fields.</summary>
    private void OnObjAdd()
    {
        GD.PushWarning("[Dateador] Crear objetos nuevos todavía no está implementado: "
                     + "hay que insertar la sección [OBJn] y actualizar NumOBJs.");
    }
}

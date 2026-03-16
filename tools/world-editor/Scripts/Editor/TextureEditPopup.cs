#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Modal popup for editing texture properties (name, category, layer, size).
/// </summary>
public partial class TextureEditPopup : Window
{
    [Signal] public delegate void SavedEventHandler();

    public TextureRef? TargetRef;
    public TextureCatalog? Catalog;
    public string IndicesPath = "";

    private LineEdit? _nameEdit;
    private OptionButton? _categoryOption;
    private SpinBox? _layerSpin;
    private SpinBox? _widthSpin;
    private SpinBox? _heightSpin;

    public override void _Ready()
    {
        Title = "Editar Textura";
        Size = new Vector2I(320, 340);
        Exclusive = true;
        Unresizable = true;
        CloseRequested += QueueFree;

        if (TargetRef == null || Catalog == null) { QueueFree(); return; }

        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        bg.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // Header
        vbox.AddChild(EditorTheme.MakeLabel($"Textura #{TargetRef.Index} — GRH {TargetRef.GrhIndex}",
            EditorTheme.TEXT_ACCENT, EditorTheme.FONT_MD));

        vbox.AddChild(EditorTheme.MakeHSeparator());

        // Name
        vbox.AddChild(EditorTheme.SectionLabel("Nombre"));
        _nameEdit = new LineEdit { Text = TargetRef.Name };
        _nameEdit.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _nameEdit.CustomMinimumSize = new Vector2(0, 26);
        vbox.AddChild(_nameEdit);

        // Category
        vbox.AddChild(EditorTheme.SectionLabel("Categoría"));
        var catRow = new HBoxContainer();
        catRow.AddThemeConstantOverride("separation", 4);
        _categoryOption = new OptionButton();
        _categoryOption.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _categoryOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        int selectedIdx = 0;
        for (int i = 0; i < Catalog.CategoryOrder.Count; i++)
        {
            _categoryOption.AddItem(Catalog.CategoryOrder[i]);
            if (Catalog.CategoryOrder[i] == TargetRef.Category)
                selectedIdx = i;
        }
        _categoryOption.Selected = selectedIdx;
        catRow.AddChild(_categoryOption);

        var addCatBtn = EditorTheme.MakeButton("+", OnAddCategory);
        addCatBtn.CustomMinimumSize = new Vector2(28, 0);
        catRow.AddChild(addCatBtn);
        vbox.AddChild(catRow);

        // Layer
        vbox.AddChild(EditorTheme.SectionLabel("Capa"));
        _layerSpin = EditorTheme.MakeSpinBox(0, 4, 1, TargetRef.Layer);
        vbox.AddChild(_layerSpin);

        // Width / Height
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 8);

        var wCol = new VBoxContainer();
        wCol.AddThemeConstantOverride("separation", 2);
        wCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        wCol.AddChild(EditorTheme.SectionLabel("Ancho (tiles)"));
        _widthSpin = EditorTheme.MakeSpinBox(0, 32, 1, TargetRef.TileWidth);
        wCol.AddChild(_widthSpin);
        sizeRow.AddChild(wCol);

        var hCol = new VBoxContainer();
        hCol.AddThemeConstantOverride("separation", 2);
        hCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hCol.AddChild(EditorTheme.SectionLabel("Alto (tiles)"));
        _heightSpin = EditorTheme.MakeSpinBox(0, 32, 1, TargetRef.TileHeight);
        hCol.AddChild(_heightSpin);
        sizeRow.AddChild(hCol);

        vbox.AddChild(sizeRow);

        // Spacer
        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(spacer);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.Alignment = BoxContainer.AlignmentMode.End;

        var cancelBtn = EditorTheme.MakeButton("Cancelar", () => QueueFree());
        btnRow.AddChild(cancelBtn);

        var saveBtn = EditorTheme.PrimaryButton("Guardar", OnSave);
        btnRow.AddChild(saveBtn);

        vbox.AddChild(btnRow);
    }

    private void OnAddCategory()
    {
        var dialog = new AcceptDialog { Title = "Nueva Categoría", Size = new Vector2I(250, 120) };
        var edit = new LineEdit { PlaceholderText = "Nombre de categoría" };
        edit.CustomMinimumSize = new Vector2(200, 28);
        dialog.AddChild(edit);
        dialog.Confirmed += () =>
        {
            string name = edit.Text.Trim();
            if (name.Length > 0 && Catalog != null && _categoryOption != null)
            {
                Catalog.AddCategory(name);
                _categoryOption.AddItem(name);
                _categoryOption.Selected = _categoryOption.ItemCount - 1;
            }
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void OnSave()
    {
        if (TargetRef == null || Catalog == null) { QueueFree(); return; }

        string newName = _nameEdit?.Text.Trim() ?? TargetRef.Name;
        string newCategory = _categoryOption != null
            ? _categoryOption.GetItemText(_categoryOption.Selected)
            : TargetRef.Category;
        int newLayer = (int)(_layerSpin?.Value ?? TargetRef.Layer);
        int newWidth = (int)(_widthSpin?.Value ?? TargetRef.TileWidth);
        int newHeight = (int)(_heightSpin?.Value ?? TargetRef.TileHeight);

        string oldCategory = TargetRef.Category;

        // If category changed, move between category lists
        if (oldCategory != newCategory)
        {
            if (Catalog.Categories.TryGetValue(oldCategory, out var oldList))
            {
                oldList.Remove(TargetRef);
                // Remove empty category
                if (oldList.Count == 0)
                {
                    Catalog.Categories.Remove(oldCategory);
                    Catalog.CategoryOrder.Remove(oldCategory);
                }
            }

            if (!Catalog.Categories.ContainsKey(newCategory))
                Catalog.AddCategory(newCategory);
            Catalog.Categories[newCategory].Add(TargetRef);
        }

        // Update fields
        TargetRef.Name = newName;
        TargetRef.Category = newCategory;
        TargetRef.Layer = newLayer;
        TargetRef.TileWidth = newWidth;
        TargetRef.TileHeight = newHeight;

        // Rebuild index ordering + save
        Catalog.RebuildAllRefsFromCategories();
        if (IndicesPath.Length > 0)
            Catalog.SaveToFile(IndicesPath);

        EmitSignal(SignalName.Saved);
        QueueFree();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

public partial class ItemSearchPanel : RpgBaseForm
{
    private GameData? _data;
    private AoTcpClient? _tcp;
    private ObjInfo[] _objects = Array.Empty<ObjInfo>();
    private LineEdit? _searchInput;
    private OptionButton? _typeFilter;
    private ItemList? _results;
    private ItemPreview? _preview;
    private Label? _statusLabel;
    private Label? _detailLabel;
    private readonly List<int> _filtered = new();

    public ItemSearchPanel() : base("Buscar Items", new Vector2(620, 460), "v2") { }

    public void Init(GameData data, AoTcpClient? tcp)
    {
        _data = data;
        _tcp = tcp;
        _objects = ObjectLoader.CountObjects(data.Objects) > 0 ? data.Objects : Array.Empty<ObjInfo>();
        _preview?.Init(data);
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(root);

        var searchRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        root.AddChild(searchRow);

        _searchInput = RpgTheme.CreateRpgInput("nombre, ID o tipo...", 230);
        _searchInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _searchInput.TextSubmitted += _ => SearchAndFocusFirst();
        searchRow.AddChild(_searchInput);

        _typeFilter = new OptionButton();
        _typeFilter.CustomMinimumSize = new Vector2(145, 30);
        _typeFilter.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _typeFilter.AddItem("Todos", 0);
        _typeFilter.AddItem("Armas", 2);
        _typeFilter.AddItem("Armaduras", 3);
        _typeFilter.AddItem("Escudos", 16);
        _typeFilter.AddItem("Cascos", 17);
        _typeFilter.AddItem("Mochilas", 37);
        _typeFilter.AddItem("Pociones", 11);
        _typeFilter.AddItem("Hechizos", 12);
        _typeFilter.ItemSelected += _ => Search();
        searchRow.AddChild(_typeFilter);

        var actionRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        actionRow.Alignment = BoxContainer.AlignmentMode.End;
        root.AddChild(actionRow);

        var searchBtn = RpgTheme.CreateRpgButton("Buscar", false, 12);
        searchBtn.CustomMinimumSize = new Vector2(100, 30);
        searchBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        searchBtn.Pressed += Search;
        actionRow.AddChild(searchBtn);

        var reloadBtn = RpgTheme.CreateRpgButton("Recargar", false, 12);
        reloadBtn.CustomMinimumSize = new Vector2(110, 30);
        reloadBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        reloadBtn.Pressed += () =>
        {
            EnsureCatalogLoaded(force: true);
            Search();
        };
        actionRow.AddChild(reloadBtn);

        _statusLabel = RpgTheme.CreateInfoLabel("", 10);
        root.AddChild(_statusLabel);

        var body = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        body.CustomMinimumSize = new Vector2(0, 210);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(body);

        _results = new ItemList
        {
            CustomMinimumSize = new Vector2(325, 210),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
        };
        _results.ItemSelected += OnResultSelected;
        _results.ItemActivated += _ => CreateSelectedItem();
        body.AddChild(_results);

        var side = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        side.CustomMinimumSize = new Vector2(190, 210);
        body.AddChild(side);

        _preview = new ItemPreview();
        _preview.CustomMinimumSize = new Vector2(180, 72);
        _preview.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _preview.Init(_data);
        side.AddChild(_preview);

        var detailScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(180, 78),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        side.AddChild(detailScroll);

        _detailLabel = RpgTheme.CreateInfoLabel("Escribi para buscar.", 11);
        _detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _detailLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        detailScroll.AddChild(_detailLabel);

        var itemActionRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        side.AddChild(itemActionRow);

        var createBtn = RpgTheme.CreateRpgButton("Crear", false, 12);
        createBtn.CustomMinimumSize = new Vector2(88, 28);
        createBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        createBtn.Pressed += CreateSelectedItem;
        itemActionRow.AddChild(createBtn);

        var copyBtn = RpgTheme.CreateRpgButton("Copiar ID", false, 13);
        copyBtn.CustomMinimumSize = new Vector2(94, 28);
        copyBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        copyBtn.Pressed += CopySelectedId;
        itemActionRow.AddChild(copyBtn);
    }

    public void OpenWithQuery(string query = "")
    {
        ShowForm();
        if (_searchInput != null)
        {
            _searchInput.Text = query;
            _searchInput.GrabFocus();
            _searchInput.CaretColumn = _searchInput.Text.Length;
        }
        EnsureCatalogLoaded();
        Search();
    }

    private void Search()
    {
        if (_results == null) return;
        EnsureCatalogLoaded();

        int catalogCount = CountCatalogItems();
        if (_statusLabel != null)
            _statusLabel.Text = catalogCount > 0
                ? $"Catalogo cargado: {catalogCount} items ({System.IO.Path.GetFileName(ObjectLoader.LastSourcePath)})"
                : "Catalogo vacio: no se encontro obj.dat o no se pudo leer.";

        if (catalogCount == 0)
        {
            _filtered.Clear();
            _results.Clear();
            _preview?.SetItem(0);
            if (_detailLabel != null)
                _detailLabel.Text = "No hay items cargados. Usa Recargar o revisa client/Data/INIT/obj.dat.";
            return;
        }

        string query = (_searchInput?.Text ?? "").Trim();
        string queryNorm = Normalize(query);
        int selectedType = (int)(_typeFilter?.GetSelectedId() ?? 0);
        bool queryIsId = int.TryParse(query, out int idQuery);

        _filtered.Clear();
        _results.Clear();

        for (int i = 1; i < _objects.Length; i++)
        {
            var obj = _objects[i];
            if (obj == null || obj.Index <= 0 || string.IsNullOrWhiteSpace(obj.Name)) continue;
            if (selectedType > 0 && obj.ObjType != selectedType) continue;
            if (queryNorm.Length > 0
                && !(queryIsId && obj.Index == idQuery)
                && !Normalize(obj.Name).Contains(queryNorm)
                && !Normalize(TypeName(obj.ObjType)).Contains(queryNorm))
                continue;

            _filtered.Add(obj.Index);
            _results.AddItem($"[{obj.Index}] {obj.Name}  ({TypeName(obj.ObjType)})");
            if (_filtered.Count >= 250) break;
        }

        if (_filtered.Count > 0)
        {
            _results.Select(0);
            ShowDetails(_filtered[0]);
        }
        else if (_detailLabel != null)
        {
            _preview?.SetItem(0);
            _detailLabel.Text = "Sin resultados.";
        }
    }

    private int CountCatalogItems()
    {
        return ObjectLoader.CountObjects(_objects);
    }

    private void SearchAndFocusFirst()
    {
        Search();
        if (_filtered.Count > 0)
            _results?.GrabFocus();
    }

    private void OnResultSelected(long selected)
    {
        int idx = (int)selected;
        if (idx >= 0 && idx < _filtered.Count)
            ShowDetails(_filtered[idx]);
    }

    private void ShowDetails(int objIndex)
    {
        if (_data == null || objIndex <= 0 || objIndex >= _objects.Length || _detailLabel == null) return;
        var obj = _objects[objIndex];
        _preview?.SetItem(obj.GrhIndex);
        _detailLabel.Text =
            $"ID: {obj.Index}\n" +
            $"Nombre: {obj.Name}\n" +
            $"Tipo: {obj.ObjType} - {TypeName(obj.ObjType)}\n" +
            $"GRH: {obj.GrhIndex}\n" +
            $"Valor: {obj.Value}\n" +
            $"Hit: {obj.MinHit}-{obj.MaxHit}\n" +
            $"Def: {obj.MinDef}-{obj.MaxDef}\n" +
            $"Mod: {obj.MinMod}-{obj.MaxMod}\n" +
            $"Hechizo: {obj.HechizoIndex}\n" +
            $"MochilaType: {obj.MochilaType}\n" +
            $"Newbie: {(obj.Newbie ? "si" : "no")}\n" +
            $"Agarrable: {(obj.Agarrable ? "si" : "no")}";
    }

    private int SelectedObjectIndex()
    {
        if (_results == null) return 0;
        var selected = _results.GetSelectedItems();
        if (selected.Length == 0) return 0;
        int idx = selected[0];
        return idx >= 0 && idx < _filtered.Count ? _filtered[idx] : 0;
    }

    private void CreateSelectedItem()
    {
        int objIndex = SelectedObjectIndex();
        if (objIndex <= 0) return;
        if (_tcp == null)
        {
            if (_statusLabel != null)
                _statusLabel.Text = "No hay conexion activa para crear items.";
            return;
        }

        _tcp.SendPacket(ClientPackets.WriteTalk($"/CI {objIndex}"));
        if (_statusLabel != null)
            _statusLabel.Text = $"Pedido enviado: /CI {objIndex}";
    }

    private void CopySelectedId()
    {
        int objIndex = SelectedObjectIndex();
        if (objIndex <= 0) return;
        DisplayServer.ClipboardSet(objIndex.ToString());
    }

    private void EnsureCatalogLoaded(bool force = false)
    {
        if (!force && ObjectLoader.CountObjects(_objects) > 0)
            return;

        if (!force && _data != null && ObjectLoader.CountObjects(_data.Objects) > 0)
        {
            _objects = _data.Objects;
            return;
        }

        try
        {
            _objects = ObjectLoader.LoadFromKnownLoosePaths();
            if (_data != null && ObjectLoader.CountObjects(_objects) > 0)
                _data.Objects = _objects;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ItemSearch] Failed to reload obj.dat: {ex.Message}");
            _objects = Array.Empty<ObjInfo>();
        }
    }

    private static string Normalize(string value)
    {
        string formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (char c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string TypeName(int objType) => objType switch
    {
        1 => "UseOnce",
        2 => "Arma",
        3 => "Armadura",
        6 => "Puerta",
        7 => "Contenedor",
        11 => "Pocion",
        12 => "Hechizo",
        16 => "Escudo",
        17 => "Casco",
        21 => "Barco",
        25 => "Aura",
        27 => "Yunque",
        37 => "Mochila",
        _ => $"Tipo {objType}",
    };

    private partial class ItemPreview : Control
    {
        private GameData? _data;
        private int _grhIndex;

        public void Init(GameData? data)
        {
            _data = data;
            QueueRedraw();
        }

        public void SetItem(int grhIndex)
        {
            _grhIndex = grhIndex;
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.035f, 0.03f, 0.8f), true);
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.45f, 0.38f, 0.25f, 0.75f), false, 1f);

            if (_data == null || _grhIndex <= 0)
                return;

            var grh = _data.ResolveGrh(_grhIndex);
            if (grh == null || grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0)
                return;

            var texture = _data.Textures?.GetTexture(grh.FileNum);
            if (texture == null)
                return;

            int texW = texture.GetWidth();
            int texH = texture.GetHeight();
            int sx = grh.SX;
            int sy = grh.SY;
            int sw = grh.PixelWidth;
            int sh = grh.PixelHeight;
            if (texW > 0) sx %= texW;
            if (texH > 0) sy %= texH;
            if (sx + sw > texW) sw = texW - sx;
            if (sy + sh > texH) sh = texH - sy;
            if (sw <= 0 || sh <= 0)
                return;

            float maxW = Math.Max(1f, Size.X - 18f);
            float maxH = Math.Max(1f, Size.Y - 18f);
            float scale = Math.Min(2f, Math.Min(maxW / sw, maxH / sh));
            var destSize = new Vector2(sw * scale, sh * scale);
            var dest = new Rect2((Size - destSize) / 2f, destSize);
            var src = new Rect2(sx, sy, sw, sh);
            DrawTextureRectRegion(texture, dest, src);
        }
    }
}

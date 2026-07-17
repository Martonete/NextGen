using Godot;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.UI;

public partial class WorldMapPanel : RpgBaseForm
{
    private const string MapPath = "Graficos/MapaMundo.png";
    private IResourceProvider? _resources;
    private TextureRect? _mapTexture;

    public WorldMapPanel() : base("Mapa del Mundo", new Vector2(760, 560), "v2") { }

    public void Init(IResourceProvider resources)
    {
        _resources = resources;
        LoadMapTexture();
    }

    protected override void BuildContent()
    {
        var frame = new PanelContainer();
        frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.025f, 0.02f, 0.95f),
            BorderColor = new Color(0.52f, 0.36f, 0.14f, 0.9f)
        };
        style.SetBorderWidthAll(1);
        style.SetContentMarginAll(4);
        frame.AddThemeStyleboxOverride("panel", style);
        ContentContainer.AddChild(frame);

        _mapTexture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        frame.AddChild(_mapTexture);
        LoadMapTexture();
    }

    public void Open()
    {
        LoadMapTexture();
        ShowForm();
    }

    private void LoadMapTexture()
    {
        if (_resources == null || _mapTexture == null) return;

        var image = _resources.ReadImage(MapPath);
        if (image == null) return;

        _mapTexture.Texture = ImageTexture.CreateFromImage(image);
    }
}

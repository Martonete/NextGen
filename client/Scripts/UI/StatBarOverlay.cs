using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.UI;

/// <summary>
/// Draws VB6-accurate stat bar fills using extracted bar images and value labels.
/// VB6 uses Image controls (HpSHP, MPShp, SPShp, AguaSP, COMIDASp, ExpBar) whose
/// Width is modified proportionally to current/max ratio. We replicate this by
/// drawing a clipped region of each bar image.
///
/// Self-contained compact stack: five rows (Hp/Mana/Sta/Ham/Agua) starting at local
/// (0,0), meant to live inside the "Estado" floating HUD window (see Main.FloatingHud.cs).
/// Unlike the old sidebar layout this no longer depends on ResolutionManager.SidebarX —
/// callers just position/size this control like any other widget.
/// </summary>
public partial class StatBarOverlay : Control
{
    private static int S(int v) => ResolutionManager.S(v);

    private const int TagW = 26, BarW = 78, BarH = 13, RowH = 16;
    private static Rect2 HpRect   => new(S(TagW), S(RowH * 0), S(BarW), S(BarH));
    private static Rect2 ManaRect => new(S(TagW), S(RowH * 1), S(BarW), S(BarH));
    private static Rect2 StaRect  => new(S(TagW), S(RowH * 2), S(BarW), S(BarH));
    private static Rect2 HamRect  => new(S(TagW), S(RowH * 3), S(BarW), S(BarH));
    private static Rect2 AguaRect => new(S(TagW), S(RowH * 4), S(BarW), S(BarH));

    /// <summary>Tight bounding size of the drawn content (5 rows) — callers position/size
    /// this control to exactly this, keyed to the current UIScale.</summary>
    public static Vector2 IntrinsicSize => new(S(TagW + BarW), S(RowH) * 5);

    private static readonly Color TextColor = new(1f, 1f, 1f);
    private static readonly Color TagColor = new(0.78f, 0.72f, 0.6f);

    // Bar image textures (extracted from VB6 frmMain.frx)
    private Texture2D? _hpTex;
    private Texture2D? _manaTex;
    private Texture2D? _staTex;
    private Texture2D? _aguaTex;
    private Texture2D? _hamTex;
    private Texture2D? _expTex;

    // Fallback colors if images fail to load
    private static readonly Color HpColor   = new(0.92f, 0.14f, 0.14f);
    private static readonly Color ManaColor = new(0f, 0.53f, 0.75f);
    private static readonly Color StaColor  = new(0.81f, 0.58f, 0.06f);
    private static readonly Color AguaColor = new(0f, 0.75f, 0.74f);
    private static readonly Color HamColor  = new(0f, 0.63f, 0.13f);
    private static readonly Color ExpColor  = new(0.37f, 0.6f, 0.18f);

    private int _minHp, _maxHp;
    private int _minMana, _maxMana;
    private int _minSta, _maxSta;
    private int _minAgua, _maxAgua;
    private int _minHam, _maxHam;
    private int _exp, _expNext;

    private Font? _font;
    private int FontSize => S(9); // VB6: Tahoma 6.75pt Bold (+1px), scaled

    /// <summary>Data path set by Main.cs before AddChild (so _Ready can find bar images).</summary>
    public string DataPath = "";

    /// <summary>Resource provider set by Main.cs before AddChild. Takes precedence over DataPath.</summary>
    public IResourceProvider? Resources;

    public override void _Ready()
    {
        var sysFont = new SystemFont();
        sysFont.FontNames = new string[] { "Tahoma" };
        sysFont.FontWeight = 700;
        _font = sysFont;

        // Load bar images from extracted VB6 resources (runtime file path, not res://)
        _hpTex   = LoadBarTexture("Graficos/Principal/bar_hp.jpg");
        _manaTex = LoadBarTexture("Graficos/Principal/bar_mana.jpg");
        _staTex  = LoadBarTexture("Graficos/Principal/bar_sta.jpg");
        _aguaTex = LoadBarTexture("Graficos/Principal/bar_agua.jpg");
        _hamTex  = LoadBarTexture("Graficos/Principal/bar_ham.jpg");
        _expTex  = LoadBarTexture("Graficos/Principal/bar_exp.jpg");
    }

    private Texture2D? LoadBarTexture(string relativePath)
    {
        Godot.Image? image;
        if (Resources != null)
        {
            image = Resources.ReadImage(relativePath);
        }
        else
        {
            string filePath = System.IO.Path.Combine(DataPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(filePath)) return null;
            image = Image.LoadFromFile(filePath);
        }

        if (image == null)
        {
            GD.Print($"[UI] Bar image failed to load: {relativePath} — using color fallback");
            return null;
        }

        var tex = ImageTexture.CreateFromImage(image);
        GD.Print($"[UI] Loaded bar image: {relativePath} ({tex.GetWidth()}x{tex.GetHeight()})");
        return tex;
    }

    public void SetStats(
        int minHp, int maxHp,
        int minMana, int maxMana,
        int minSta, int maxSta,
        int minAgua, int maxAgua,
        int minHam, int maxHam,
        int exp, int expNext)
    {
        bool changed = _minHp != minHp || _maxHp != maxHp
            || _minMana != minMana || _maxMana != maxMana
            || _minSta != minSta || _maxSta != maxSta
            || _minAgua != minAgua || _maxAgua != maxAgua
            || _minHam != minHam || _maxHam != maxHam
            || _exp != exp || _expNext != expNext;

        if (!changed) return;

        _minHp = minHp; _maxHp = maxHp;
        _minMana = minMana; _maxMana = maxMana;
        _minSta = minSta; _maxSta = maxSta;
        _minAgua = minAgua; _maxAgua = maxAgua;
        _minHam = minHam; _maxHam = maxHam;
        _exp = exp; _expNext = expNext;

        QueueRedraw();
    }

    public override void _Draw()
    {
        // Draw each bar fill — VB6: modify Width proportionally
        DrawBar(HpRect, _hpTex, HpColor, _minHp, _maxHp);
        DrawBar(ManaRect, _manaTex, ManaColor, _minMana, _maxMana);
        DrawBar(StaRect, _staTex, StaColor, _minSta, _maxSta);
        DrawBar(HamRect, _hamTex, HamColor, _minHam, _maxHam);
        DrawBar(AguaRect, _aguaTex, AguaColor, _minAgua, _maxAgua);
        // Experience is shown by the character sheet (P) now; drawing it here
        // too would leave a fill floating over the hidden sidebar frame.

        if (_font == null) return;

        // Short tag so each bar reads on its own — this widget no longer sits
        // next to sidebar icons that used to give that context.
        DrawBarTag(HpRect, "HP");
        DrawBarTag(ManaRect, "MP");
        DrawBarTag(StaRect, "STA");
        DrawBarTag(HamRect, "HAM");
        DrawBarTag(AguaRect, "SED");

        // Draw stat value text centered on each bar — VB6: "min/max" format
        DrawBarText(HpRect, $"{_minHp}/{_maxHp}");
        DrawBarText(ManaRect, $"{_minMana}/{_maxMana}");
        DrawBarText(StaRect, $"{_minSta}/{_maxSta}");
        // VB6: Agua/Ham show percentage
        DrawBarText(HamRect, $"{_minHam}%");
        DrawBarText(AguaRect, $"{_minAgua}%");
        // Exp bar is too thin (5px) for text — ExpLabel handles it
    }

    private void DrawBarTag(Rect2 barRect, string tag)
    {
        if (_font == null) return;
        float ascent = _font.GetAscent(FontSize);
        var pos = new Vector2(0, barRect.Position.Y + (barRect.Size.Y + ascent) / 2f - 1f);
        DrawString(_font, pos, tag, HorizontalAlignment.Left, S(TagW) - S(2), FontSize - 1, TagColor);
    }

    private void DrawBar(Rect2 rect, Texture2D? tex, Color fallbackColor, int min, int max)
    {
        if (max <= 0) return;
        // VB6: Width = (min / max) * maxWidth
        float ratio = Mathf.Clamp((float)min / max, 0f, 1f);
        float fillWidth = ratio * rect.Size.X;
        if (fillWidth < 1f) return;

        if (tex != null)
        {
            // Draw clipped portion of the bar image (left side, proportional to ratio)
            // srcRect: the left portion of the texture to show
            var srcRect = new Rect2(0, 0, ratio * tex.GetWidth(), tex.GetHeight());
            // dstRect: where on screen, scaled to bar dimensions
            var dstRect = new Rect2(rect.Position, new Vector2(fillWidth, rect.Size.Y));
            DrawTextureRectRegion(tex, dstRect, srcRect);
        }
        else
        {
            // Fallback: solid color rect
            DrawRect(new Rect2(rect.Position, new Vector2(fillWidth, rect.Size.Y)), fallbackColor);
        }
    }

    private void DrawBarText(Rect2 rect, string text)
    {
        if (_font == null) return;
        var textSize = _font.GetStringSize(text, HorizontalAlignment.Center, -1, FontSize);
        float textX = rect.Position.X + (rect.Size.X - textSize.X) / 2f;
        float ascent = _font.GetAscent(FontSize);
        float textY = rect.Position.Y + (rect.Size.Y + ascent) / 2f - 1f;
        var pos = new Vector2(textX, textY);
        DrawString(_font, pos, text, HorizontalAlignment.Left, -1, FontSize, TextColor);
    }
}

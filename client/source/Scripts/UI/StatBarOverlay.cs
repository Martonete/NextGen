using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Draws VB6-accurate stat bar fills using extracted bar images and value labels.
/// VB6 uses Image controls (HpSHP, MPShp, SPShp, AguaSP, COMIDASp, ExpBar) whose
/// Width is modified proportionally to current/max ratio. We replicate this by
/// drawing a clipped region of each bar image.
/// Bar X positions are relative to ResolutionManager.SidebarX (560 at 800x600).
/// </summary>
public partial class StatBarOverlay : Control
{
    // Bar positions computed from sidebar X, scaled by S(). Design-space offsets: 584=560+24, 565=560+5
    private static int S(int v) => ResolutionManager.S(v);
    private static Rect2 StaRect  => new(ResolutionManager.SidebarX + S(24), S(443), S(75), S(12));
    private static Rect2 ManaRect => new(ResolutionManager.SidebarX + S(24), S(467), S(75), S(12));
    private static Rect2 HpRect   => new(ResolutionManager.SidebarX + S(24), S(488), S(75), S(12));
    private static Rect2 HamRect  => new(ResolutionManager.SidebarX + S(24), S(511), S(75), S(12));
    private static Rect2 AguaRect => new(ResolutionManager.SidebarX + S(24), S(534), S(75), S(12));
    private static Rect2 ExpRect  => new(ResolutionManager.SidebarX + S(5), S(80), S(202), S(10));

    private static readonly Color TextColor = new(1f, 1f, 1f);

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

    public override void _Ready()
    {
        var sysFont = new SystemFont();
        sysFont.FontNames = new string[] { "Tahoma" };
        sysFont.FontWeight = 700;
        _font = sysFont;

        // Load bar images from extracted VB6 resources (runtime file path, not res://)
        string principalDir = System.IO.Path.Combine(DataPath, "Graficos", "Principal");
        _hpTex   = LoadBarTexture(System.IO.Path.Combine(principalDir, "bar_hp.jpg"));
        _manaTex = LoadBarTexture(System.IO.Path.Combine(principalDir, "bar_mana.jpg"));
        _staTex  = LoadBarTexture(System.IO.Path.Combine(principalDir, "bar_sta.jpg"));
        _aguaTex = LoadBarTexture(System.IO.Path.Combine(principalDir, "bar_agua.jpg"));
        _hamTex  = LoadBarTexture(System.IO.Path.Combine(principalDir, "bar_ham.jpg"));
        _expTex  = LoadBarTexture(System.IO.Path.Combine(principalDir, "bar_exp.jpg"));
    }

    private static Texture2D? LoadBarTexture(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return null;
        }

        var image = Image.LoadFromFile(filePath);
        if (image == null)
        {
            GD.Print($"[UI] Bar image failed to load: {filePath} — using color fallback");
            return null;
        }

        var tex = ImageTexture.CreateFromImage(image);
        GD.Print($"[UI] Loaded bar image: {filePath} ({tex.GetWidth()}x{tex.GetHeight()})");
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
        DrawBar(AguaRect, _aguaTex, AguaColor, _minAgua, _maxAgua);
        DrawBar(HamRect, _hamTex, HamColor, _minHam, _maxHam);
        DrawBar(ExpRect, _expTex, ExpColor, _exp, _expNext);

        if (_font == null) return;

        // Draw stat value text centered on each bar — VB6: "min/max" format
        DrawBarText(HpRect, $"{_minHp}/{_maxHp}");
        DrawBarText(ManaRect, $"{_minMana}/{_maxMana}");
        DrawBarText(StaRect, $"{_minSta}/{_maxSta}");
        // VB6: Agua/Ham show percentage
        DrawBarText(AguaRect, $"{_minAgua}%");
        DrawBarText(HamRect, $"{_minHam}%");
        // Exp bar is too thin (5px) for text — ExpLabel handles it
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

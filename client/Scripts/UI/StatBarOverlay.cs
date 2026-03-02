using Godot;

namespace TierrasSagradasAO.UI;

/// <summary>
/// Draws VB6-accurate stat bar fills and value labels directly over Principal.jpg.
/// The bar frames are baked into Principal.jpg — this only draws the colored fill rects
/// and the centered text labels ("min/max" or "min%").
/// </summary>
public partial class StatBarOverlay : Control
{
    // VB6 bar positions (twips÷15) — X, Y, MaxWidth, Height
    private static readonly Rect2 HpRect   = new(544, 423, 118, 12);
    private static readonly Rect2 ManaRect = new(544, 445, 118, 12);
    private static readonly Rect2 StaRect  = new(544, 467, 118, 12);
    private static readonly Rect2 AguaRect = new(544, 489, 118, 12);
    private static readonly Rect2 HamRect  = new(544, 511, 118, 12);
    private static readonly Rect2 ExpRect  = new(535, 85,  264, 5);

    // Fill colors matching VB6
    private static readonly Color HpColor   = new(1f, 0f, 0f);         // Red
    private static readonly Color ManaColor = new(0f, 0f, 1f);         // Blue
    private static readonly Color StaColor  = new(1f, 1f, 0f);         // Yellow
    private static readonly Color AguaColor = new(0f, 0.784f, 1f);     // Cyan (0,200,255)
    private static readonly Color HamColor  = new(0.784f, 0.5f, 0f);   // Brown/Orange (200,128,0)
    private static readonly Color ExpColor  = new(0f, 0.392f, 1f);     // Blue (0,100,255)
    private static readonly Color TextColor = new(1f, 1f, 1f);         // White text

    private int _minHp, _maxHp;
    private int _minMana, _maxMana;
    private int _minSta, _maxSta;
    private int _minAgua, _maxAgua;
    private int _minHam, _maxHam;
    private int _exp, _expNext;

    private Font? _font;
    private int _fontSize = 7; // VB6: Tahoma 6.75pt Bold

    public override void _Ready()
    {
        // VB6: HpBar/ManaBar/StaBar/AguaBar/ComidaBar all use Tahoma 6.75pt Bold
        var sysFont = new SystemFont();
        sysFont.FontNames = new string[] { "Tahoma" };
        sysFont.FontWeight = 700;
        _font = sysFont;
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
        // Draw each bar fill
        DrawBar(HpRect, HpColor, _minHp, _maxHp);
        DrawBar(ManaRect, ManaColor, _minMana, _maxMana);
        DrawBar(StaRect, StaColor, _minSta, _maxSta);
        DrawBar(AguaRect, AguaColor, _minAgua, _maxAgua);
        DrawBar(HamRect, HamColor, _minHam, _maxHam);
        DrawBar(ExpRect, ExpColor, _exp, _expNext);

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

    private void DrawBar(Rect2 rect, Color color, int min, int max)
    {
        if (max <= 0) return;
        // VB6: Width = (min / max) * maxWidth
        float ratio = Mathf.Clamp((float)min / max, 0f, 1f);
        float fillWidth = ratio * rect.Size.X;
        DrawRect(new Rect2(rect.Position, new Vector2(fillWidth, rect.Size.Y)), color);
    }

    private void DrawBarText(Rect2 rect, string text)
    {
        if (_font == null) return;
        var textSize = _font.GetStringSize(text, HorizontalAlignment.Center, -1, _fontSize);
        float textX = rect.Position.X + (rect.Size.X - textSize.X) / 2f;
        // Vertically center: baseline = top + (barHeight + ascent) / 2
        float ascent = _font.GetAscent(_fontSize);
        float textY = rect.Position.Y + (rect.Size.Y + ascent) / 2f;
        var pos = new Vector2(textX, textY);
        DrawString(_font, pos, text, HorizontalAlignment.Left, -1, _fontSize, TextColor);
    }
}

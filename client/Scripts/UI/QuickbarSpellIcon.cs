using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Small vector glyphs for spell slots, independent of the world GRH catalog.</summary>
public partial class QuickbarSpellIcon : Control
{
    private string _spell = "";
    public string Spell { set { if (_spell == value) return; _spell = value; QueueRedraw(); } }
    public override void _Draw()
    {
        string name = _spell.ToLowerInvariant();
        bool electric = name.Contains("descarga") || name.Contains("eléctr") || name.Contains("electr");
        bool fire = name.Contains("apocal") || name.Contains("fuego");
        bool green = name.Contains("inmov") || name.Contains("curar") || name.Contains("sanar");
        Color color = electric ? new Color("70d6ff") : fire ? new Color("ff784c") : green ? new Color("64d698") : new Color("b38bff");
        Vector2 center = new(17, 17);
        DrawCircle(center, 16, new Color(color, 0.12f));
        DrawCircle(center, 12, new Color(color, 0.18f));
        DrawArc(center, 13, 0, Mathf.Tau, 24, new Color(color, 0.65f), 1, true);
        if (electric)
            DrawColoredPolygon(new Vector2[] { new(20, 2), new(7, 20), new(16, 19), new(12, 33), new(27, 13), new(18, 14) }, color);
        else if (fire)
        {
            DrawColoredPolygon(new Vector2[] { new(17, 2), new(23, 13), new(29, 10), new(26, 27), new(17, 32), new(7, 25), new(6, 14), new(13, 19) }, color);
            DrawCircle(new Vector2(17, 24), 5, new Color("ffdb79"));
        }
        else if (green)
        {
            DrawArc(center, 9, 0.4f, 5.5f, 24, color, 3, true);
            DrawLine(new Vector2(10, 17), new Vector2(24, 17), color, 2, true);
            DrawLine(new Vector2(17, 10), new Vector2(17, 24), color, 2, true);
        }
        else
        {
            DrawPolyline(new Vector2[] { new(17, 3), new(28, 24), new(5, 24), new(17, 3) }, color, 2, true);
            DrawCircle(center, 4, Colors.White);
        }
    }
}

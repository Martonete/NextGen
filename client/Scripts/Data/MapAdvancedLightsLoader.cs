using System;
using System.Globalization;
using System.Text;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Loads the advanced light file authored by the World Editor
/// (<c>Maps/MapaN.aolight</c>) into the client's map-light list.
///
/// The editor stores rich per-light data (type, cone, flicker, pulse, shadows),
/// but the client's <see cref="LightSystem"/> bakes a static radial lightmap
/// that only understands position + radius + color. To keep that fast, static
/// path intact we map each advanced light down to what the client renders:
///   • X, Y                          → tile position
///   • Radius (tiles, rounded)       → Range
///   • R,G,B modulated by Energy     → color intensity
/// Energy scales the color brightness (clamped), so a high-energy light reads
/// brighter and a dim one softer — cheap variety with zero per-frame cost.
/// Cone / flicker / pulse / shadows are intentionally ignored for now.
/// </summary>
public static class MapAdvancedLightsLoader
{
    public static int Apply(IResourceProvider resources, GameState state, int mapNumber)
    {
        string relPath = $"Maps/Mapa{mapNumber}.aolight";
        if (!resources.Exists(relPath))
            return 0;

        string text = Encoding.UTF8.GetString(resources.ReadBytes(relPath));
        int loaded = 0;
        var ic = CultureInfo.InvariantCulture;

        foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[')
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            if (!key.Equals("Light", StringComparison.OrdinalIgnoreCase))
                continue; // ignore [ASSETn] template lines — those aren't placed lights

            // Value: x|y|type|r|g|b|energy|radius|dir|cone|flicker|pulse|shadows|name
            string[] p = line[(eq + 1)..].Trim().Split('|');
            if (p.Length < 2) continue;
            if (!int.TryParse(p[0], NumberStyles.Integer, ic, out int x)) continue;
            if (!int.TryParse(p[1], NumberStyles.Integer, ic, out int y)) continue;

            int r = p.Length >= 4 && int.TryParse(p[3], NumberStyles.Integer, ic, out int pr) ? pr : 255;
            int g = p.Length >= 5 && int.TryParse(p[4], NumberStyles.Integer, ic, out int pg) ? pg : 220;
            int b = p.Length >= 6 && int.TryParse(p[5], NumberStyles.Integer, ic, out int pb) ? pb : 180;
            float energy = p.Length >= 7 && float.TryParse(p[6], NumberStyles.Float, ic, out float pe) ? pe : 1.0f;
            float radius = p.Length >= 8 && float.TryParse(p[7], NumberStyles.Float, ic, out float prad) ? prad : 6.0f;

            // Energy modulates brightness. Clamp so a very high energy doesn't
            // just flatten everything to white; 1.0 = author's color as-is.
            float e = Mathf.Clamp(energy, 0.1f, 2.5f);
            byte mr = (byte)Mathf.Clamp((int)MathF.Round(r * e), 0, 255);
            byte mg = (byte)Mathf.Clamp((int)MathF.Round(g * e), 0, 255);
            byte mb = (byte)Mathf.Clamp((int)MathF.Round(b * e), 0, 255);

            int range = Math.Max(1, (int)MathF.Round(radius));

            state.MapLights.Add(new MapLight
            {
                X = x,
                Y = y,
                Range = range,
                R = mr,
                G = mg,
                B = mb,
                Active = true,
            });
            loaded++;
        }

        if (loaded > 0)
            GD.Print($"[MAPFX] Map {mapNumber}: loaded {loaded} advanced lights (.aolight)");

        return loaded;
    }
}

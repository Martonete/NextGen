using System;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Game;

public static class MapEffectsLoader
{
    private const string RelativePath = "INIT/MapEffects.ini";

    public static (int particles, int lights) Apply(IResourceProvider resources, GameState state, int mapNumber)
    {
        if (!resources.Exists(RelativePath))
            return (0, 0);

        string currentSection = "";
        int particles = 0;
        int lights = 0;
        string wantedSection = $"Mapa{mapNumber}";
        string text = Encoding.UTF8.GetString(resources.ReadBytes(RelativePath));

        foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1];
                continue;
            }

            if (!currentSection.Equals(wantedSection, StringComparison.OrdinalIgnoreCase))
                continue;

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            string key = line[..eqIdx].Trim();
            string value = line[(eqIdx + 1)..].Trim();
            string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (key.Equals("Particle", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 3)
                    continue;

                if (!int.TryParse(parts[0], out int defIndex) ||
                    !int.TryParse(parts[1], out int x) ||
                    !int.TryParse(parts[2], out int y))
                    continue;

                if (ParticleSystem.CreateMapStream(state, defIndex, x, y) != null)
                    particles++;
            }
            else if (key.Equals("Light", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 6)
                    continue;

                if (!int.TryParse(parts[0], out int x) ||
                    !int.TryParse(parts[1], out int y) ||
                    !int.TryParse(parts[2], out int range) ||
                    !byte.TryParse(parts[3], out byte r) ||
                    !byte.TryParse(parts[4], out byte g) ||
                    !byte.TryParse(parts[5], out byte b))
                    continue;

                state.MapLights.Add(new MapLight
                {
                    X = x,
                    Y = y,
                    Range = range,
                    R = r,
                    G = g,
                    B = b,
                    Active = true
                });
                lights++;
            }
        }

        if (particles > 0 || lights > 0)
            GD.Print($"[MAPFX] Map {mapNumber}: loaded {particles} particles, {lights} lights");

        return (particles, lights);
    }
}

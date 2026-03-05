using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace AOWorldEditor.Data;

public class ParticleStreamDef
{
    public string Name = "";
    public int NumParticles;
    public int[] GrhList = Array.Empty<int>();
    public int GrhCount;
    public float Angle;
    public float VecX1, VecY1, VecX2, VecY2;
    public float X1, Y1, X2, Y2;
    public float MoveX1, MoveY1, MoveX2, MoveY2;
    public float LifeMin, LifeMax;
    public float Friction;
    public float Gravity;
    public float GravStrength;
    public float BounceStrength;
    public float Speed;
    public bool Spin;
    public float SpinSpeedL, SpinSpeedH;
    public bool AlphaBlend;
    public bool XMove, YMove;
    public int LifeCounter;
    public byte ColR1, ColG1, ColB1;
    public byte ColR2, ColG2, ColB2;
    public byte ColR3, ColG3, ColB3;
    public byte ColR4, ColG4, ColB4;
}

public class EditorParticle
{
    public float X, Y;
    public float VelX, VelY;
    public float Life, MaxLife;
    public float Angle;
    public int GrhIndex;
    public float Alpha;
    public bool Alive;
    public byte ColR, ColG, ColB;
}

public class EditorParticleStream
{
    public int DefIndex;
    public int MapX, MapY;
    public EditorParticle[] Particles = Array.Empty<EditorParticle>();
    public float FrameCounter;
    public bool Active = true;
}

/// <summary>
/// Loads Particles.ini and simulates particle streams for the world editor.
/// Replicates the client ParticleSystem physics exactly.
/// </summary>
public class ParticleEngine
{
    private static readonly Random Rng = new();
    public ParticleStreamDef[] Defs = Array.Empty<ParticleStreamDef>();
    public List<EditorParticleStream> Streams = new();

    public void LoadDefinitions(string filePath)
    {
        if (!File.Exists(filePath))
        {
            GD.PrintErr($"[ParticleEngine] Not found: {filePath}");
            return;
        }

        var lines = File.ReadAllLines(filePath);
        int total = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Total=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(trimmed.AsSpan(6), out total);
                break;
            }
        }

        if (total <= 0) return;

        Defs = new ParticleStreamDef[total + 1];
        for (int i = 0; i <= total; i++)
            Defs[i] = new ParticleStreamDef();

        int currentSection = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                var sectionName = line[1..^1];
                if (sectionName.Equals("INIT", StringComparison.OrdinalIgnoreCase))
                { currentSection = 0; continue; }
                if (int.TryParse(sectionName, out int secNum) && secNum >= 1 && secNum <= total)
                    currentSection = secNum;
                else
                    currentSection = 0;
                continue;
            }

            if (currentSection < 1 || currentSection > total) continue;
            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = line[..eqIdx].Trim();
            var val = line[(eqIdx + 1)..].Trim();
            ParseField(Defs[currentSection], key, val);
        }

        GD.Print($"[ParticleEngine] Loaded {total} particle definitions");
    }

    /// <summary>
    /// Build particle streams for all tiles with ParticleGroup > 0.
    /// Call after loading a map.
    /// </summary>
    public void BuildStreamsFromMap(MapData map)
    {
        Streams.Clear();
        if (Defs.Length == 0) return;

        for (int y = 1; y <= map.Height; y++)
        {
            for (int x = 1; x <= map.Width; x++)
            {
                int pg = map.Tiles[x, y].ParticleGroup;
                if (pg <= 0 || pg >= Defs.Length) continue;

                var def = Defs[pg];
                if (def.NumParticles <= 0) continue;

                var stream = new EditorParticleStream
                {
                    DefIndex = pg,
                    MapX = x,
                    MapY = y,
                    Particles = new EditorParticle[def.NumParticles]
                };

                for (int i = 0; i < def.NumParticles; i++)
                    stream.Particles[i] = new EditorParticle();

                Streams.Add(stream);
            }
        }

        GD.Print($"[ParticleEngine] Created {Streams.Count} particle streams from map");
    }

    /// <summary>
    /// Simulate all active particle streams. Call from _Process(delta).
    /// </summary>
    public void Update(float deltaSeconds)
    {
        if (Defs.Length == 0) return;
        float deltaMs = deltaSeconds * 1000f;

        foreach (var stream in Streams)
        {
            if (!stream.Active) continue;
            if (stream.DefIndex < 1 || stream.DefIndex >= Defs.Length) continue;
            UpdateStream(stream, Defs[stream.DefIndex], deltaMs);
        }
    }

    private static void UpdateStream(EditorParticleStream stream, ParticleStreamDef def, float deltaMs)
    {
        const float EngineBaseSpeed = 0.0172f;
        stream.FrameCounter += deltaMs * EngineBaseSpeed;
        float speed = def.Speed > 0 ? def.Speed : 0.5f;
        bool doMove = stream.FrameCounter > speed;
        if (doMove)
            stream.FrameCounter = 0;

        float friction = def.Friction > 0 ? def.Friction : 1f;

        for (int i = 0; i < stream.Particles.Length; i++)
        {
            var p = stream.Particles[i];
            if (!doMove) continue;

            if (!p.Alive)
            {
                SpawnParticle(p, def);
                continue;
            }

            if (def.Gravity > 0)
            {
                p.VelY += def.GravStrength;
                if (p.Y > 0)
                    p.VelY = def.BounceStrength;
            }

            if (def.Spin)
                p.Angle += RandRange(def.SpinSpeedL, def.SpinSpeedH) / 100f;

            if (def.XMove) p.VelX = RandRange(def.MoveX1, def.MoveX2);
            if (def.YMove) p.VelY = RandRange(def.MoveY1, def.MoveY2);

            p.X += (int)(p.VelX / friction);
            p.Y += (int)(p.VelY / friction);

            p.Life -= 1;
            if (p.Life <= 0)
                SpawnParticle(p, def);
        }
    }

    private static void SpawnParticle(EditorParticle p, ParticleStreamDef def)
    {
        p.Alive = true;
        p.X = RandRange(def.X1, def.X2);
        p.Y = RandRange(def.Y1, def.Y2);
        p.VelX = RandRange(def.VecX1, def.VecX2);
        p.VelY = RandRange(def.VecY1, def.VecY2);
        p.Life = (int)RandRange(def.LifeMin, def.LifeMax);
        p.MaxLife = p.Life;
        p.Angle = def.Angle;
        p.Alpha = 1f;

        if (def.GrhList.Length > 0)
            p.GrhIndex = def.GrhList[Rng.Next(def.GrhList.Length)];

        // VB6 BGR quirk: INI R stored as Blue, INI B stored as Red
        p.ColR = (byte)((def.ColB1 + def.ColB2 + def.ColB3 + def.ColB4) / 4);
        p.ColG = (byte)((def.ColG1 + def.ColG2 + def.ColG3 + def.ColG4) / 4);
        p.ColB = (byte)((def.ColR1 + def.ColR2 + def.ColR3 + def.ColR4) / 4);
    }

    private static float RandRange(float min, float max)
    {
        if (min > max) (min, max) = (max, min);
        return min + (float)(Rng.NextDouble() * (max - min));
    }

    private static void ParseField(ParticleStreamDef def, string key, string val)
    {
        var style = System.Globalization.NumberStyles.Float;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        switch (key.ToLowerInvariant())
        {
            case "name": def.Name = val; break;
            case "numofparticles": int.TryParse(val, out def.NumParticles); break;
            case "angle": float.TryParse(val, style, culture, out def.Angle); break;
            case "x1": float.TryParse(val, style, culture, out def.X1); break;
            case "y1": float.TryParse(val, style, culture, out def.Y1); break;
            case "x2": float.TryParse(val, style, culture, out def.X2); break;
            case "y2": float.TryParse(val, style, culture, out def.Y2); break;
            case "vecx1": float.TryParse(val, style, culture, out def.VecX1); break;
            case "vecy1": float.TryParse(val, style, culture, out def.VecY1); break;
            case "vecx2": float.TryParse(val, style, culture, out def.VecX2); break;
            case "vecy2": float.TryParse(val, style, culture, out def.VecY2); break;
            case "life1": float.TryParse(val, style, culture, out def.LifeMin); break;
            case "life2": float.TryParse(val, style, culture, out def.LifeMax); break;
            case "friction": float.TryParse(val, style, culture, out def.Friction); break;
            case "gravity":
                if (val == "1") def.Gravity = 1f;
                else float.TryParse(val, style, culture, out def.Gravity);
                break;
            case "grav_strength": float.TryParse(val, style, culture, out def.GravStrength); break;
            case "bounce_strength": float.TryParse(val, style, culture, out def.BounceStrength); break;
            case "speed": float.TryParse(val, style, culture, out def.Speed); break;
            case "spin": def.Spin = val == "1"; break;
            case "spin_speedl": float.TryParse(val, style, culture, out def.SpinSpeedL); break;
            case "spin_speedh": float.TryParse(val, style, culture, out def.SpinSpeedH); break;
            case "alphablend": def.AlphaBlend = val == "1"; break;
            case "xmove": def.XMove = val == "1"; break;
            case "ymove": def.YMove = val == "1"; break;
            case "life_counter": int.TryParse(val, out def.LifeCounter); break;
            case "numgrhs": int.TryParse(val, out def.GrhCount); break;
            case "move_x1": float.TryParse(val, style, culture, out def.MoveX1); break;
            case "move_y1": float.TryParse(val, style, culture, out def.MoveY1); break;
            case "move_x2": float.TryParse(val, style, culture, out def.MoveX2); break;
            case "move_y2": float.TryParse(val, style, culture, out def.MoveY2); break;
            case "grh_list":
                var parts = val.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var list = new List<int>();
                foreach (var g in parts)
                    if (int.TryParse(g.Trim(), out int grhId) && grhId > 0)
                        list.Add(grhId);
                def.GrhList = list.ToArray();
                if (def.GrhCount == 0) def.GrhCount = def.GrhList.Length;
                break;
            case "colorset1": ParseColor(val, out def.ColR1, out def.ColG1, out def.ColB1); break;
            case "colorset2": ParseColor(val, out def.ColR2, out def.ColG2, out def.ColB2); break;
            case "colorset3": ParseColor(val, out def.ColR3, out def.ColG3, out def.ColB3); break;
            case "colorset4": ParseColor(val, out def.ColR4, out def.ColG4, out def.ColB4); break;
        }
    }

    private static void ParseColor(string val, out byte r, out byte g, out byte b)
    {
        r = g = b = 255;
        var parts = val.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            byte.TryParse(parts[0].Trim(), out r);
            byte.TryParse(parts[1].Trim(), out g);
            byte.TryParse(parts[2].Trim(), out b);
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    // Parsed but never consumed by the SIMULATION physics — legacy VB6 fields kept
    // for lossless round-trip. ResizeX/ResizeY ARE consumed by the draw path when
    // ScaleOverLife is on (see "extended motor" fields below) — they're reused as
    // the start/end scale factor instead of adding new INI keys for the same idea.
    public float Resize, ResizeX, ResizeY;

    // ── Extended motor fields (opt-in, "more than VB6") ────────────────────────
    // All default to false/1 = byte-identical to the classic VB6 behavior. Only
    // definitions that explicitly opt in change how they look; the 105 shipped
    // definitions are untouched unless someone edits them and flips these on.
    /// <summary>Fade alpha 1→0 over the particle's lifetime instead of snapping in/out.</summary>
    public bool FadeAlpha;
    /// <summary>Draw the particle rotated by its simulated spin angle (already computed, never drawn before).</summary>
    public bool RotateVisual;
    /// <summary>Scale the sprite from ResizeX to ResizeY over the particle's lifetime.</summary>
    public bool ScaleOverLife;
    /// <summary>Interpolate between the 4 ColorSets over lifetime instead of averaging them once at spawn.</summary>
    public bool ColorGradient;
    /// <summary>Perturb velocity with organic noise each tick (drifting smoke/mist instead of straight lines).</summary>
    public bool Turbulence;
    /// <summary>Noise perturbation magnitude (px/tick added to velocity).</summary>
    public float TurbulenceStrength = 10f;
    /// <summary>Pull (positive) or push (negative) particles toward/from a point each tick.</summary>
    public bool AttractToPoint;
    /// <summary>Attractor position, in the same space as X1/Y1 (px, relative to the stream origin).</summary>
    public float AttractX, AttractY;
    /// <summary>Force magnitude; negative = repulsion (explosion), positive = attraction (vortex/implosion).</summary>
    public float AttractStrength = 5f;

    /// <summary>Deep-enough copy for editing: GrhList gets its own array so mutating
    /// the clone's sprite list can't reach back into the source definition.</summary>
    public ParticleStreamDef Clone()
    {
        var copy = (ParticleStreamDef)MemberwiseClone();
        copy.GrhList = (int[])GrhList.Clone();
        return copy;
    }
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
    /// <summary>Per-particle advancing time offset for Turbulence noise sampling — keeps each particle's drift independent even when spawned at the same position.</summary>
    public float NoisePhase;
}

/// <summary>
/// Small deterministic value-noise helper for the Turbulence extended-motor
/// effect. Not a shader/texture noise (those are for rendering) — this is plain
/// C# so both the client and editor simulations get identical, cheap, coherent
/// drift without depending on a Godot Resource inside the hot particle loop.
/// </summary>
internal static class ParticleNoise
{
    /// <summary>Smooth pseudo-random value in [0,1], coherent across nearby (x,y,z).</summary>
    public static float Sample(float x, float y, float z)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);

        float c000 = Hash(xi, yi, zi), c100 = Hash(xi + 1, yi, zi);
        float c010 = Hash(xi, yi + 1, zi), c110 = Hash(xi + 1, yi + 1, zi);
        float c001 = Hash(xi, yi, zi + 1), c101 = Hash(xi + 1, yi, zi + 1);
        float c011 = Hash(xi, yi + 1, zi + 1), c111 = Hash(xi + 1, yi + 1, zi + 1);

        float x00 = Lerp(c000, c100, u), x10 = Lerp(c010, c110, u);
        float x01 = Lerp(c001, c101, u), x11 = Lerp(c011, c111, u);
        float y0 = Lerp(x00, x10, v), y1 = Lerp(x01, x11, v);
        return Lerp(y0, y1, w);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Hash(int x, int y, int z)
    {
        int h = x * 374761393 + y * 668265263 + z * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }
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

        var lines = File.ReadAllLines(filePath, Encoding.Latin1);
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
    /// Write all definitions back to Particles.ini, following the exact format
    /// LoadDefinitions expects: [INIT]/Total=N, then 1-indexed [n] sections.
    /// Index 0 (unused placeholder) is never written.
    /// </summary>
    public void SaveDefinitions(string filePath)
    {
        int total = Defs.Length - 1;
        if (total < 0) total = 0;

        var sb = new System.Text.StringBuilder();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine("[INIT]");
        sb.AppendLine($"Total={total}");

        for (int i = 1; i <= total; i++)
        {
            var d = Defs[i];
            sb.AppendLine($"[{i}]");
            sb.AppendLine($"Name={d.Name}");
            sb.AppendLine($"NumOfParticles={d.NumParticles}");
            sb.AppendLine($"X1={d.X1.ToString(culture)}");
            sb.AppendLine($"Y1={d.Y1.ToString(culture)}");
            sb.AppendLine($"X2={d.X2.ToString(culture)}");
            sb.AppendLine($"Y2={d.Y2.ToString(culture)}");
            sb.AppendLine($"Angle={d.Angle.ToString(culture)}");
            sb.AppendLine($"VecX1={d.VecX1.ToString(culture)}");
            sb.AppendLine($"VecX2={d.VecX2.ToString(culture)}");
            sb.AppendLine($"VecY1={d.VecY1.ToString(culture)}");
            sb.AppendLine($"VecY2={d.VecY2.ToString(culture)}");
            sb.AppendLine($"Life1={d.LifeMin.ToString(culture)}");
            sb.AppendLine($"Life2={d.LifeMax.ToString(culture)}");
            sb.AppendLine($"Friction={d.Friction.ToString(culture)}");
            sb.AppendLine($"Spin={(d.Spin ? 1 : 0)}");
            sb.AppendLine($"Spin_SpeedL={d.SpinSpeedL.ToString(culture)}");
            sb.AppendLine($"Spin_SpeedH={d.SpinSpeedH.ToString(culture)}");
            sb.AppendLine($"Grav_Strength={d.GravStrength.ToString(culture)}");
            sb.AppendLine($"Bounce_Strength={d.BounceStrength.ToString(culture)}");
            sb.AppendLine($"AlphaBlend={(d.AlphaBlend ? 1 : 0)}");
            sb.AppendLine($"Gravity={(d.Gravity > 0 ? 1 : 0)}");
            sb.AppendLine($"XMove={(d.XMove ? 1 : 0)}");
            sb.AppendLine($"YMove={(d.YMove ? 1 : 0)}");
            sb.AppendLine($"move_x1={d.MoveX1.ToString(culture)}");
            sb.AppendLine($"move_x2={d.MoveX2.ToString(culture)}");
            sb.AppendLine($"move_y1={d.MoveY1.ToString(culture)}");
            sb.AppendLine($"move_y2={d.MoveY2.ToString(culture)}");
            sb.AppendLine($"life_counter={d.LifeCounter}");
            sb.AppendLine($"Speed={d.Speed.ToString(culture)}");
            sb.AppendLine($"resize={d.Resize.ToString(culture)}");
            sb.AppendLine($"rx={d.ResizeX.ToString(culture)}");
            sb.AppendLine($"ry={d.ResizeY.ToString(culture)}");
            sb.AppendLine($"fade_alpha={(d.FadeAlpha ? 1 : 0)}");
            sb.AppendLine($"rotate_visual={(d.RotateVisual ? 1 : 0)}");
            sb.AppendLine($"scale_over_life={(d.ScaleOverLife ? 1 : 0)}");
            sb.AppendLine($"color_gradient={(d.ColorGradient ? 1 : 0)}");
            sb.AppendLine($"turbulence={(d.Turbulence ? 1 : 0)}");
            sb.AppendLine($"turbulence_strength={d.TurbulenceStrength.ToString(culture)}");
            sb.AppendLine($"attract_to_point={(d.AttractToPoint ? 1 : 0)}");
            sb.AppendLine($"attract_x={d.AttractX.ToString(culture)}");
            sb.AppendLine($"attract_y={d.AttractY.ToString(culture)}");
            sb.AppendLine($"attract_strength={d.AttractStrength.ToString(culture)}");
            sb.AppendLine($"NumGrhs={d.GrhList.Length}");
            sb.Append("Grh_List=");
            foreach (var grh in d.GrhList) sb.Append(grh).Append(',');
            sb.AppendLine();
            sb.AppendLine($"ColorSet1={d.ColR1},{d.ColG1},{d.ColB1}");
            sb.AppendLine($"ColorSet2={d.ColR2},{d.ColG2},{d.ColB2}");
            sb.AppendLine($"ColorSet3={d.ColR3},{d.ColG3},{d.ColB3}");
            sb.AppendLine($"ColorSet4={d.ColR4},{d.ColG4},{d.ColB4}");
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.Latin1);
        GD.Print($"[ParticleEngine] Saved {total} particle definitions to {filePath}");
    }

    /// <summary>
    /// Append a new definition (blank or pre-filled by a template/wizard) and
    /// return its new 1-based index. Grows Defs[] and bumps the implicit Total.
    /// </summary>
    public int AddDefinition(ParticleStreamDef def)
    {
        var grown = new ParticleStreamDef[Defs.Length + 1];
        Array.Copy(Defs, grown, Defs.Length);
        if (grown.Length == 1) grown[0] = new ParticleStreamDef(); // first-ever def: seed the unused index-0 placeholder
        int newIndex = grown.Length - 1;
        grown[newIndex] = def;
        Defs = grown;
        return newIndex;
    }

    /// <summary>Clone definition <paramref name="sourceIndex"/> as a new entry; returns its new index, or -1 if out of range.</summary>
    public int DuplicateDefinition(int sourceIndex)
    {
        if (sourceIndex < 1 || sourceIndex >= Defs.Length) return -1;
        var copy = Defs[sourceIndex].Clone();
        copy.Name = $"{copy.Name} (copia)";
        return AddDefinition(copy);
    }

    /// <summary>
    /// Remove a definition, shifting all later indices down by one. Streams
    /// referencing removed/shifted indices are rebuilt by the caller afterwards
    /// (BuildStreamsFromMap), since map tile ParticleGroup values are untouched
    /// here and would otherwise point at the wrong (shifted) definition.
    /// </summary>
    public void RemoveDefinition(int index)
    {
        if (index < 1 || index >= Defs.Length) return;
        var shrunk = new ParticleStreamDef[Defs.Length - 1];
        for (int i = 0, j = 0; i < Defs.Length; i++)
        {
            if (i == index) continue;
            shrunk[j++] = Defs[i];
        }
        Defs = shrunk;
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

    /// <summary>
    /// Public entry point for updating a single standalone stream (e.g. a preview panel).
    /// </summary>
    public static void UpdateSingleStream(EditorParticleStream stream, ParticleStreamDef def, float deltaMs)
        => UpdateStream(stream, def, deltaMs);

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
                if (p.Y > MathF.Max(def.Y1, def.Y2) + 32f)
                    p.VelY = def.BounceStrength;
            }

            if (def.Spin)
                p.Angle += RandRange(def.SpinSpeedL, def.SpinSpeedH) / 100f;

            if (def.XMove) p.VelX = RandRange(def.MoveX1, def.MoveX2);
            if (def.YMove) p.VelY = RandRange(def.MoveY1, def.MoveY2);

            // Extended motor: organic drift (smoke/mist) — perturbs velocity with
            // coherent noise sampled at the particle's own position + a slowly
            // advancing per-particle phase, so it drifts smoothly instead of
            // jittering randomly like XMove/YMove does.
            if (def.Turbulence)
            {
                p.NoisePhase += 0.05f;
                float nx = ParticleNoise.Sample(p.X * 0.05f, p.Y * 0.05f, p.NoisePhase) * 2f - 1f;
                float ny = ParticleNoise.Sample(p.X * 0.05f + 100f, p.Y * 0.05f + 100f, p.NoisePhase) * 2f - 1f;
                p.VelX += nx * def.TurbulenceStrength;
                p.VelY += ny * def.TurbulenceStrength;
            }

            // Extended motor: attraction (positive strength, vortex/implosion) or
            // repulsion (negative, radial explosion) toward a fixed point.
            if (def.AttractToPoint)
            {
                float dx = def.AttractX - p.X;
                float dy = def.AttractY - p.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 1f)
                {
                    p.VelX += dx / dist * def.AttractStrength;
                    p.VelY += dy / dist * def.AttractStrength;
                }
            }

            p.X += (int)(p.VelX / friction);
            p.Y += (int)(p.VelY / friction);

            // Extended motor: re-evaluate the gradient every tick so an aging
            // particle visibly shifts color. Classic (ColorGradient=false) particles
            // never re-enter this branch's ApplyColor work — their color stays the
            // one resolved at spawn, matching VB6 exactly.
            if (def.ColorGradient)
                ApplyColor(p, def);

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

        ApplyColor(p, def);
    }

    /// <summary>
    /// Particle color, with the VB6 BGR quirk (INI R stored as Blue, INI B stored
    /// as Red) always applied. Classic (ColorGradient=false, the VB6-identical
    /// default): average of the 4 ColorSet vertices, resolved once at spawn.
    /// Extended (ColorGradient=true): interpolate Set1→Set2→Set3→Set4 across the
    /// particle's lifetime (0=freshly spawned, 1=about to die) — evaluated fresh
    /// every call, so a moving particle visibly shifts color as it ages (e.g. fire
    /// yellow→orange→red→smoke). Shared by spawn and the live-recolor path.
    /// </summary>
    private static void ApplyColor(EditorParticle p, ParticleStreamDef def)
    {
        if (!def.ColorGradient)
        {
            p.ColR = (byte)((def.ColB1 + def.ColB2 + def.ColB3 + def.ColB4) / 4);
            p.ColG = (byte)((def.ColG1 + def.ColG2 + def.ColG3 + def.ColG4) / 4);
            p.ColB = (byte)((def.ColR1 + def.ColR2 + def.ColR3 + def.ColR4) / 4);
            return;
        }

        float t = p.MaxLife > 0 ? Math.Clamp(1f - p.Life / p.MaxLife, 0f, 1f) : 0f;
        LerpColorSets(def, t, out byte iniR, out byte iniG, out byte iniB);
        // Same BGR swap as the classic path: INI R→render B, INI B→render R.
        p.ColR = iniB; p.ColG = iniG; p.ColB = iniR;
    }

    /// <summary>Interpolate across the 4 raw INI ColorSets at t in [0,1] (Set1→Set2→Set3→Set4, 3 equal segments).</summary>
    private static void LerpColorSets(ParticleStreamDef def, float t, out byte r, out byte g, out byte b)
    {
        float seg = t * 3f;
        int i0 = Math.Clamp((int)seg, 0, 2);
        float f = Math.Clamp(seg - i0, 0f, 1f);

        (byte r, byte g, byte b) a = i0 switch
        {
            0 => (def.ColR1, def.ColG1, def.ColB1),
            1 => (def.ColR2, def.ColG2, def.ColB2),
            _ => (def.ColR3, def.ColG3, def.ColB3),
        };
        (byte r, byte g, byte b) c = i0 switch
        {
            0 => (def.ColR2, def.ColG2, def.ColB2),
            1 => (def.ColR3, def.ColG3, def.ColB3),
            _ => (def.ColR4, def.ColG4, def.ColB4),
        };

        r = (byte)Math.Clamp(a.r + (c.r - a.r) * f, 0, 255);
        g = (byte)Math.Clamp(a.g + (c.g - a.g) * f, 0, 255);
        b = (byte)Math.Clamp(a.b + (c.b - a.b) * f, 0, 255);
    }

    /// <summary>
    /// Recolor every currently-alive particle in a stream in place, so a color
    /// edit in the editor is visible immediately instead of waiting for each
    /// particle to die and respawn (their color is otherwise cached at spawn).
    /// </summary>
    public static void RecolorLiveParticles(EditorParticleStream stream, ParticleStreamDef def)
    {
        foreach (var p in stream.Particles)
            if (p.Alive) ApplyColor(p, def);
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
            // Legacy VB6 fields — resize/rx/ry unused by the CLASSIC simulation, kept
            // for lossless round-trip; ResizeX/ResizeY double as the extended motor's
            // scale-over-life start/end when ScaleOverLife is on (see below).
            case "resize": float.TryParse(val, style, culture, out def.Resize); break;
            case "rx": float.TryParse(val, style, culture, out def.ResizeX); break;
            case "ry": float.TryParse(val, style, culture, out def.ResizeY); break;
            // Extended motor fields — absent in every shipped def, so old files
            // parse to all-false/default and look byte-identical to before.
            case "fade_alpha": def.FadeAlpha = val == "1"; break;
            case "rotate_visual": def.RotateVisual = val == "1"; break;
            case "scale_over_life": def.ScaleOverLife = val == "1"; break;
            case "color_gradient": def.ColorGradient = val == "1"; break;
            case "turbulence": def.Turbulence = val == "1"; break;
            case "turbulence_strength": float.TryParse(val, style, culture, out def.TurbulenceStrength); break;
            case "attract_to_point": def.AttractToPoint = val == "1"; break;
            case "attract_x": float.TryParse(val, style, culture, out def.AttractX); break;
            case "attract_y": float.TryParse(val, style, culture, out def.AttractY); break;
            case "attract_strength": float.TryParse(val, style, culture, out def.AttractStrength); break;
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

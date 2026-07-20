using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Game;

/// <summary>
/// Loads particle definitions from Particles.ini and simulates active particle streams.
/// VB6 equivalent: Particulas.bas (General_Particle_Render, General_Particle_Create, etc.)
/// </summary>
public class ParticleSystem
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Load all particle definitions from Particles.ini using IResourceProvider.
    /// relativePath is relative to the Data directory (e.g. "INIT/Particles.ini").
    /// </summary>
    public void LoadDefinitions(IResourceProvider resources, string relativePath, GameState state)
    {
        if (!resources.Exists(relativePath))
        {
            GD.PrintErr($"[PARTICLE] Particles.ini not found: {relativePath}");
            return;
        }

        byte[] rawBytes = resources.ReadBytes(relativePath);
        string rawText = System.Text.Encoding.Latin1.GetString(rawBytes);
        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int total = 0;

        // First pass: find total
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Total=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(trimmed.AsSpan(6), out total);
                break;
            }
        }

        if (total <= 0)
        {
            GD.PrintErr("[PARTICLE] No particle definitions found");
            return;
        }

        // Allocate 1-indexed array
        state.ParticleDefs = new ParticleStreamDef[total + 1];
        for (int i = 0; i <= total; i++)
            state.ParticleDefs[i] = new ParticleStreamDef();

        // Second pass: parse sections
        int currentSection = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            // Section header [N] or [INIT]
            if (line[0] == '[' && line[^1] == ']')
            {
                var sectionName = line[1..^1];
                if (sectionName.Equals("INIT", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = 0;
                    continue;
                }
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
            var def = state.ParticleDefs[currentSection];

            ParseField(def, key, val);
        }

        GD.Print($"[PARTICLE] Loaded {total} particle definitions");
    }

    private static void ParseField(ParticleStreamDef def, string key, string val)
    {
        switch (key.ToLowerInvariant())
        {
            case "name": def.Name = val; break;
            case "numofparticles": int.TryParse(val, out def.NumParticles); break;
            case "angle": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Angle); break;
            case "x1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.X1); break;
            case "y1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Y1); break;
            case "x2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.X2); break;
            case "y2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Y2); break;
            case "vecx1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.VecX1); break;
            case "vecy1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.VecY1); break;
            case "vecx2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.VecX2); break;
            case "vecy2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.VecY2); break;
            case "life1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.LifeMin); break;
            case "life2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.LifeMax); break;
            case "friction": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Friction); break;
            case "gravity":
                if (val == "1") def.Gravity = 1f;
                else float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Gravity);
                break;
            case "grav_strength": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.GravStrength); break;
            case "bounce_strength": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.BounceStrength); break;
            case "speed": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Speed); break;
            case "spin": def.Spin = val == "1"; break;
            case "spin_speedl": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.SpinSpeedL); break;
            case "spin_speedh": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.SpinSpeedH); break;
            case "alphablend": def.AlphaBlend = val == "1"; break;
            case "xmove": def.XMove = val == "1"; break;
            case "ymove": def.YMove = val == "1"; break;
            case "life_counter": int.TryParse(val, out def.LifeCounter); break;
            case "numgrhs": int.TryParse(val, out def.GrhCount); break;
            case "move_x1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.MoveX1); break;
            case "move_y1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.MoveY1); break;
            case "move_x2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.MoveX2); break;
            case "move_y2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.MoveY2); break;
            case "grh_list":
                var grhParts = val.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var grhList = new List<int>();
                foreach (var g in grhParts)
                {
                    if (int.TryParse(g.Trim(), out int grhId) && grhId > 0)
                        grhList.Add(grhId);
                }
                def.GrhList = grhList.ToArray();
                if (def.GrhCount == 0) def.GrhCount = def.GrhList.Length;
                break;
            case "colorset1": ParseColor(val, out def.ColR1, out def.ColG1, out def.ColB1); break;
            case "colorset2": ParseColor(val, out def.ColR2, out def.ColG2, out def.ColB2); break;
            case "colorset3": ParseColor(val, out def.ColR3, out def.ColG3, out def.ColB3); break;
            case "colorset4": ParseColor(val, out def.ColR4, out def.ColG4, out def.ColB4); break;
            // Legacy VB6 fields — resize/rx/ry unused by the classic simulation, kept
            // for round-trip; ResizeX/ResizeY double as the extended motor's
            // scale-over-life start/end when ScaleOverLife is on.
            case "resize": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Resize); break;
            case "rx": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.ResizeX); break;
            case "ry": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.ResizeY); break;
            // Extended motor fields — absent in every shipped def, so old files
            // parse to all-false and look byte-identical to before.
            case "fade_alpha": def.FadeAlpha = val == "1"; break;
            case "rotate_visual": def.RotateVisual = val == "1"; break;
            case "scale_over_life": def.ScaleOverLife = val == "1"; break;
            case "color_gradient": def.ColorGradient = val == "1"; break;
            case "turbulence": def.Turbulence = val == "1"; break;
            case "turbulence_strength": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.TurbulenceStrength); break;
            case "attract_to_point": def.AttractToPoint = val == "1"; break;
            case "attract_x": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.AttractX); break;
            case "attract_y": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.AttractY); break;
            case "attract_strength": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.AttractStrength); break;
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

    /// <summary>
    /// Create a map-attached particle stream at the given tile position.
    /// </summary>
    public static ParticleStream? CreateMapStream(GameState state, int defIndex, int mapX, int mapY)
    {
        if (defIndex < 1 || defIndex >= state.ParticleDefs.Length)
        {
            GD.PrintErr($"[PARTICLE] CreateMapStream: invalid defIndex {defIndex} (max {state.ParticleDefs.Length - 1})");
            return null;
        }

        var def = state.ParticleDefs[defIndex];
        var stream = new ParticleStream
        {
            DefIndex = defIndex,
            MapX = mapX,
            MapY = mapY,
            CharIndex = -1,
            Active = true,
            LifeCountdown = def.LifeCounter == 0 ? -1 : def.LifeCounter,
            Particles = new Particle[def.NumParticles]
        };

        for (int i = 0; i < def.NumParticles; i++)
            stream.Particles[i] = new Particle();

        state.MapParticles.Add(stream);
        return stream;
    }

    /// <summary>
    /// Create a character-attached particle stream.
    /// </summary>
    public static ParticleStream? CreateCharStream(GameState state, int defIndex, int charIndex)
    {
        if (defIndex < 1 || defIndex >= state.ParticleDefs.Length)
        {
            GD.PrintErr($"[PARTICLE] CreateCharStream: invalid defIndex {defIndex} (max {state.ParticleDefs.Length - 1})");
            return null;
        }

        var def = state.ParticleDefs[defIndex];
        var stream = new ParticleStream
        {
            DefIndex = defIndex,
            CharIndex = charIndex,
            Active = true,
            LifeCountdown = def.LifeCounter == 0 ? -1 : def.LifeCounter,
            Particles = new Particle[def.NumParticles]
        };

        for (int i = 0; i < def.NumParticles; i++)
            stream.Particles[i] = new Particle();

        state.MapParticles.Add(stream);
        return stream;
    }

    /// <summary>
    /// Update all active particle streams: spawn new particles, simulate physics.
    /// Called every frame from Main._Process.
    /// </summary>
    public void Update(float deltaSeconds, GameState state)
    {
        float deltaMs = deltaSeconds * 1000f;
        if (state.ParticleDefs.Length == 0) return;

        for (int s = state.MapParticles.Count - 1; s >= 0; s--)
        {
            var stream = state.MapParticles[s];
            if (!stream.Active)
            {
                state.MapParticles.RemoveAt(s);
                continue;
            }

            if (stream.DefIndex < 1 || stream.DefIndex >= state.ParticleDefs.Length)
            {
                state.MapParticles.RemoveAt(s);
                continue;
            }

            var def = state.ParticleDefs[stream.DefIndex];
            UpdateStream(stream, def, deltaMs);
        }
    }

    private static void UpdateStream(ParticleStream stream, ParticleStreamDef def, float deltaMs)
    {
        // VB6: frame_counter += deltaMs * 0.0172 (EngineBaseSpeed).
        // When frame_counter > Speed → advance physics, reset counter.
        // This yields ~29ms per tick for Speed=0.5, matching VB6 exactly.
        const float EngineBaseSpeed = 0.0172f;
        stream.FrameCounter += deltaMs * EngineBaseSpeed;
        float speed = def.Speed > 0 ? def.Speed : 0.5f;
        bool doMove = stream.FrameCounter > speed;
        if (doMove)
            stream.FrameCounter = 0;

        // Stream lifetime countdown (VB6: life_counter per-tick decrement)
        if (doMove && stream.LifeCountdown > 0)
        {
            stream.LifeCountdown--;
            if (stream.LifeCountdown <= 0)
            {
                stream.Active = false;
                return;
            }
        }

        // VB6 friction: integer division `vector_x \ friction` (truncates toward zero)
        float friction = def.Friction > 0 ? def.Friction : 1f;

        for (int i = 0; i < stream.Particles.Length; i++)
        {
            var p = stream.Particles[i];

            if (!doMove)
            {
                // VB6: no_move = True → only draw, skip physics
                continue;
            }

            // === Advance physics (VB6: always runs, respawns inline) ===

            if (!p.Alive)
            {
                // First-frame spawn for dead particles
                SpawnParticle(p, def);
                continue;
            }

            // VB6: gravity first, then bounce
            if (def.Gravity > 0)
            {
                p.VelY += def.GravStrength;
                if (p.Y > MathF.Max(def.Y1, def.Y2) + 32f)
                {
                    // VB6: bounce — set velocity to bounce_strength
                    p.VelY = def.BounceStrength;
                }
            }

            // VB6: spin (degrees, /100)
            if (def.Spin)
                p.Angle += RandRange(def.SpinSpeedL, def.SpinSpeedH) / 100f;

            // VB6: XMove/YMove REPLACE velocity (not additive drift)
            if (def.XMove)
                p.VelX = RandRange(def.MoveX1, def.MoveX2);
            if (def.YMove)
                p.VelY = RandRange(def.MoveY1, def.MoveY2);

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

            // VB6: position += velocity / friction (integer division)
            p.X += (int)(p.VelX / friction);
            p.Y += (int)(p.VelY / friction);

            // Extended motor: re-evaluate the gradient every tick so an aging
            // particle visibly shifts color. Classic (ColorGradient=false)
            // particles never touch this — color stays the one from spawn,
            // matching VB6 exactly.
            if (def.ColorGradient)
                ApplyColor(p, def);

            // VB6: decrement alive_counter by 1, respawn immediately when dead (no gap frame)
            p.Life -= 1;
            if (p.Life <= 0)
            {
                SpawnParticle(p, def);
            }
            // VB6 does NOT fade alpha — particles snap in/out by default. Extended
            // motor's FadeAlpha (opt-in) is applied at DRAW time from Life/MaxLife,
            // not here — see WorldRenderer's particle collection.
        }
    }

    private static void SpawnParticle(Particle p, ParticleStreamDef def)
    {
        p.Alive = true;

        // VB6: RandomNumber(X1, X2) for spawn position
        p.X = RandRange(def.X1, def.X2);
        p.Y = RandRange(def.Y1, def.Y2);

        // VB6: RandomNumber(vecx1, vecx2) for initial velocity
        p.VelX = RandRange(def.VecX1, def.VecX2);
        p.VelY = RandRange(def.VecY1, def.VecY2);

        // VB6: alive_counter = RandomNumber(life1, life2) — integer steps
        p.Life = (int)RandRange(def.LifeMin, def.LifeMax);
        p.MaxLife = p.Life;

        // VB6: angle from def (not random)
        p.Angle = def.Angle;
        p.SpinSpeed = 0; // VB6 recalculates spin per-frame via RandomNumber
        p.Alpha = 1f;

        // Choose random GRH from list
        if (def.GrhList.Length > 0)
            p.GrhIndex = def.GrhList[Rng.Next(def.GrhList.Length)];

        ApplyColor(p, def);
    }

    /// <summary>
    /// Particle color, with the VB6 BGR quirk (RGB() produces 0x00BBGGRR, but D3D
    /// vertex color interprets it as ARGB = 0xAARRGGBB, effectively swapping R↔B —
    /// the INI values were authored with this swap in mind) always applied.
    ///
    /// Classic (ColorGradient=false, the VB6-identical default): average of the 4
    /// ColorSet vertices, resolved once at spawn — Godot only supports a single
    /// modulate color, so this replicates VB6's 4-vertex quad gradient as a flat mix.
    ///
    /// Extended (ColorGradient=true): interpolate Set1→Set2→Set3→Set4 across the
    /// particle's lifetime instead, so it visibly shifts color as it ages (fire
    /// yellow→orange→red→smoke). Called every tick from UpdateStream when active.
    /// </summary>
    private static void ApplyColor(Particle p, ParticleStreamDef def)
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

    private static float RandRange(float min, float max)
    {
        if (min > max) (min, max) = (max, min);
        return min + (float)(Rng.NextDouble() * (max - min));
    }
}

/// <summary>
/// Small deterministic value-noise helper for the Turbulence extended-motor
/// effect. Not a shader/texture noise (those are for rendering) — this is plain
/// C# so both the client and editor simulations get identical, cheap, coherent
/// drift without depending on a Godot Resource inside the hot particle loop.
/// Mirrors tools/world-editor/Scripts/Data/ParticleEngine.cs's ParticleNoise —
/// keep both in sync.
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

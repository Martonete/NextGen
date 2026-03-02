using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Game;

/// <summary>
/// Loads particle definitions from Particles.ini and simulates active particle streams.
/// VB6 equivalent: Particulas.bas (General_Particle_Render, General_Particle_Create, etc.)
/// </summary>
public class ParticleSystem
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Load all particle definitions from Particles.ini into state.ParticleDefs.
    /// Format: [INIT] Total=N, then [1]..[N] with particle properties.
    /// </summary>
    public void LoadDefinitions(string filePath, GameState state)
    {
        if (!File.Exists(filePath))
        {
            GD.PrintErr($"[PARTICLE] Particles.ini not found: {filePath}");
            return;
        }

        var lines = File.ReadAllLines(filePath);
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
    public static ParticleStream CreateMapStream(GameState state, int defIndex, int mapX, int mapY)
    {
        if (defIndex < 1 || defIndex >= state.ParticleDefs.Length)
            return null!;

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
    public static ParticleStream CreateCharStream(GameState state, int defIndex, int charIndex)
    {
        if (defIndex < 1 || defIndex >= state.ParticleDefs.Length)
            return null!;

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
                if (p.Y > 0)
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

            // VB6: position += velocity / friction (integer division)
            p.X += (int)(p.VelX / friction);
            p.Y += (int)(p.VelY / friction);

            // VB6: decrement alive_counter by 1, respawn immediately when dead (no gap frame)
            p.Life -= 1;
            if (p.Life <= 0)
            {
                SpawnParticle(p, def);
            }
            // VB6 does NOT fade alpha — particles snap in/out. Alpha stays 1f (set at spawn).
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

        // VB6 quirk: particle colors use RGB() which produces 0x00BBGGRR (BGR order),
        // but D3D vertex color interprets it as ARGB = 0xAARRGGBB.
        // This effectively swaps R↔B. The INI color values were authored with this
        // swap in mind, so we must replicate it: store INI's R as Blue, INI's B as Red.
        //
        // VB6 uses all 4 ColorSets as the 4 vertex colors of the quad (gradient).
        // We average the 4 sets since Godot only supports a single modulate color.
        p.ColR = (byte)((def.ColB1 + def.ColB2 + def.ColB3 + def.ColB4) / 4); // INI Blue → render Red
        p.ColG = (byte)((def.ColG1 + def.ColG2 + def.ColG3 + def.ColG4) / 4);
        p.ColB = (byte)((def.ColR1 + def.ColR2 + def.ColR3 + def.ColR4) / 4); // INI Red → render Blue
    }

    private static float RandRange(float min, float max)
    {
        if (min > max) (min, max) = (max, min);
        return min + (float)(Rng.NextDouble() * (max - min));
    }
}

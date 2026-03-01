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
            case "move_x1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.X1); break;
            case "move_y1": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Y1); break;
            case "move_x2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.X2); break;
            case "move_y2": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def.Y2); break;
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
        float speed = def.Speed > 0 ? def.Speed : 0.5f;
        float spawnInterval = 1000f * speed / Math.Max(1, def.NumParticles);

        // Spawn timer
        stream.SpawnTimer += deltaMs;
        int toSpawn = (int)(stream.SpawnTimer / spawnInterval);
        if (toSpawn > 0)
            stream.SpawnTimer -= toSpawn * spawnInterval;

        // Spawn new particles
        for (int i = 0; i < stream.Particles.Length && toSpawn > 0; i++)
        {
            if (!stream.Particles[i].Alive)
            {
                SpawnParticle(stream.Particles[i], def);
                toSpawn--;
            }
        }

        // Simulate
        float friction = def.Friction > 0 ? 1f - (def.Friction * 0.01f) : 1f;
        friction = Math.Clamp(friction, 0f, 1f);

        for (int i = 0; i < stream.Particles.Length; i++)
        {
            var p = stream.Particles[i];
            if (!p.Alive) continue;

            // Apply velocity
            p.X += p.VelX * deltaMs * 0.01f;
            p.Y += p.VelY * deltaMs * 0.01f;

            // Apply friction
            p.VelX *= friction;
            p.VelY *= friction;

            // Apply gravity
            if (def.Gravity > 0)
                p.VelY += def.GravStrength * deltaMs * 0.01f;

            // Apply spin
            if (def.Spin)
                p.Angle += p.SpinSpeed * deltaMs * 0.001f;

            // Bounce (VB6: if Y > 0 and BounceStrength != 0)
            if (p.Y > 0 && def.BounceStrength != 0)
            {
                p.Y = 0;
                p.VelY = def.BounceStrength;
            }

            // Decrease life
            p.Life -= deltaMs * 0.1f;

            // Alpha fade based on remaining life
            p.Alpha = p.MaxLife > 0 ? Math.Clamp(p.Life / p.MaxLife, 0f, 1f) : 0f;

            // Dead check
            if (p.Life <= 0)
                p.Alive = false;
        }
    }

    private static void SpawnParticle(Particle p, ParticleStreamDef def)
    {
        p.Alive = true;

        // Random position within spawn offset bounds
        p.X = RandRange(def.X1, def.X2);
        p.Y = RandRange(def.Y1, def.Y2);

        // Random velocity within bounds
        p.VelX = RandRange(def.VecX1, def.VecX2);
        p.VelY = RandRange(def.VecY1, def.VecY2);

        // Random lifetime
        p.Life = RandRange(def.LifeMin, def.LifeMax);
        p.MaxLife = p.Life;

        p.Angle = 0;
        p.SpinSpeed = def.Spin ? RandRange(def.SpinSpeedL, def.SpinSpeedH) : 0;
        p.Alpha = 1f;

        // Choose random GRH from list
        if (def.GrhList.Length > 0)
            p.GrhIndex = def.GrhList[Rng.Next(def.GrhList.Length)];

        // Choose random color from 4 color sets
        int colorSet = Rng.Next(4);
        switch (colorSet)
        {
            case 0: p.ColR = def.ColR1; p.ColG = def.ColG1; p.ColB = def.ColB1; break;
            case 1: p.ColR = def.ColR2; p.ColG = def.ColG2; p.ColB = def.ColB2; break;
            case 2: p.ColR = def.ColR3; p.ColG = def.ColG3; p.ColB = def.ColB3; break;
            case 3: p.ColR = def.ColR4; p.ColG = def.ColG4; p.ColB = def.ColB4; break;
        }
    }

    private static float RandRange(float min, float max)
    {
        if (min > max) (min, max) = (max, min);
        return min + (float)(Rng.NextDouble() * (max - min));
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Lightweight CPU particle weather effect shared by WalkModePanel and MapViewport.
/// Each panel owns one instance, calls Update(delta) in _Process, and Draw(canvas) in _Draw.
/// </summary>
public class WeatherFx
{
    public bool Lluvia;
    public bool Nieve;
    public bool Niebla;

    // Shader-based fog parameters
    public int FogDensity;
    public int FogR = 128;
    public int FogG = 140;
    public int FogB = 160;
    public int FogSpeedX = 5;
    public int FogSpeedY = 2;
    /// <summary>World-space offset for the fog noise UV, in pixels.
    /// The renderer normalizes and passes this to the shader so the
    /// noise pattern stays anchored to the world when the camera/character moves.</summary>
    public Vector2 FogWorldOffsetPx;

    private ColorRect? _fogRect;

    private struct Particle
    {
        public float X, Y, VelX, VelY, Alpha, Life;
    }

    private const int MaxParticles = 80;
    private readonly List<Particle> _particles = new(MaxParticles);
    private readonly Random _rng = new();

    /// <summary>
    /// Call once from _Ready of the owning Control. Loads the fog shader and creates a
    /// full-rect ColorRect child for the shader overlay. Safe to call if shader is missing.
    /// </summary>
    public void AttachTo(Control parent)
    {
        var shader = GD.Load<Shader>("res://Shaders/fog_overlay.gdshader");
        if (shader == null) return;

        var noise = new NoiseTexture2D();
        var fnl = new FastNoiseLite();
        fnl.Seed = 42;
        noise.Noise = fnl;
        noise.Width = 256;
        noise.Height = 256;
        noise.Seamless = true;

        var mat = new ShaderMaterial();
        mat.Shader = shader;
        mat.SetShaderParameter("noise_texture", noise);

        _fogRect = new ColorRect();
        _fogRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _fogRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        _fogRect.Material = mat;
        _fogRect.Visible = false;
        parent.AddChild(_fogRect);
    }

    public void Update(float delta, Vector2 panelSize)
    {
        // Update shader-based fog rect visibility and parameters
        if (_fogRect != null)
        {
            _fogRect.Visible = Niebla && FogDensity > 0;
            if (_fogRect.Visible && _fogRect.Material is ShaderMaterial sm)
            {
                sm.SetShaderParameter("density", FogDensity / 255f);
                sm.SetShaderParameter("fog_color", new Color(FogR / 255f, FogG / 255f, FogB / 255f, 1f));
                sm.SetShaderParameter("speed", new Vector2(FogSpeedX / 100f, FogSpeedY / 100f));
                // Normalize world offset: one full noise repeat every 512 world px.
                // Negate so as the camera pans right (CameraOffset.x decreases),
                // the pattern slides left — i.e., it stays anchored to the world.
                sm.SetShaderParameter("world_offset", FogWorldOffsetPx / 512f);
            }
        }

        if (!Lluvia && !Nieve)
        {
            _particles.Clear();
            return;
        }

        // Spawn new particles to maintain pool up to MaxParticles
        while (_particles.Count < MaxParticles)
        {
            _particles.Add(SpawnParticle(panelSize));
        }

        // Simulate existing particles
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.X += p.VelX * delta;
            p.Y += p.VelY * delta;
            p.Life -= delta;

            bool offScreen = p.X < -10 || p.X > panelSize.X + 10
                          || p.Y > panelSize.Y + 10 || p.Life <= 0;
            if (offScreen)
            {
                _particles[i] = SpawnParticle(panelSize);
            }
            else
            {
                _particles[i] = p;
            }
        }
    }

    public void Draw(Control canvas, Vector2 panelSize)
    {
        // Niebla CPU fallback: only draw gray rect when shader overlay is off (FogDensity == 0)
        if (Niebla && FogDensity == 0)
        {
            canvas.DrawRect(new Rect2(Vector2.Zero, panelSize),
                new Color(0.5f, 0.55f, 0.6f, 0.35f));
        }

        if (!Lluvia && !Nieve) return;

        foreach (var p in _particles)
        {
            var col = Lluvia
                ? new Color(0.4f, 0.6f, 1.0f, p.Alpha * 0.7f)
                : new Color(1f, 1f, 1f, p.Alpha * 0.85f);

            if (Lluvia)
            {
                // Rain: diagonal line, 2px
                var from = new Vector2(p.X, p.Y);
                var to = new Vector2(p.X + p.VelX * 0.06f, p.Y + p.VelY * 0.06f);
                canvas.DrawLine(from, to, col, 2f);
            }
            else
            {
                // Snow: small circle, 2px radius
                canvas.DrawCircle(new Vector2(p.X, p.Y), 2f, col);
            }
        }
    }

    private Particle SpawnParticle(Vector2 panelSize)
    {
        if (Lluvia)
        {
            return new Particle
            {
                X = (float)(_rng.NextDouble() * (panelSize.X + 100)) - 50f,
                Y = (float)(_rng.NextDouble() * -panelSize.Y), // start above panel
                VelX = -60f + (float)(_rng.NextDouble() * 20),
                VelY = 400f + (float)(_rng.NextDouble() * 150),
                Alpha = 0.5f + (float)(_rng.NextDouble() * 0.5f),
                Life = 3f,
            };
        }
        else // Nieve
        {
            return new Particle
            {
                X = (float)(_rng.NextDouble() * panelSize.X),
                Y = (float)(_rng.NextDouble() * -panelSize.Y),
                VelX = -20f + (float)(_rng.NextDouble() * 40),
                VelY = 40f + (float)(_rng.NextDouble() * 60),
                Alpha = 0.6f + (float)(_rng.NextDouble() * 0.4f),
                Life = 8f,
            };
        }
    }
}

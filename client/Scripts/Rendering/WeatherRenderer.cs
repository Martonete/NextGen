using System;
using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Renders weather effects: rain particles (diagonal), blue tint overlay,
/// and periodic lightning flashes. Attached as a child of the game world.
/// VB6: rain effect was diagonal drops + thunder flash.
/// </summary>
public partial class WeatherRenderer : Node2D
{
    private GameState? _state;
    private SoundManager? _soundManager;
    private IResourceProvider? _resources;

    // Rain particle system
    private const int MaxRainDrops = 300;
    private const float RainSpeed = 420f;        // pixels/sec vertical
    private const float RainWindSpeed = 120f;    // pixels/sec horizontal (diagonal)
    private const float RainDropLength = 12f;    // length of each rain line
    private const float RainDropWidth = 1.2f;

    // Snow particle system
    private const int MaxSnowFlakes = 150;
    private const float SnowSpeed = 50f;         // pixels/sec vertical (slow drift)
    private const float SnowSway = 20f;          // max horizontal sway (pixels/sec)
    private const float SnowAlpha = 0.6f;
    private const float SnowRadius = 2.5f;
    private readonly float[] _snowX = new float[MaxSnowFlakes];
    private readonly float[] _snowY = new float[MaxSnowFlakes];
    private readonly float[] _snowSway = new float[MaxSnowFlakes]; // per-flake sway velocity
    private bool _snowInitialized;
    private static int ViewW => ResolutionManager.ViewportW;
    private static int ViewH => ResolutionManager.ViewportH;
    // Spawn margin: drops spawn outside viewport so they enter from top/left
    private const float SpawnMarginX = 160f;

    private readonly float[] _dropX = new float[MaxRainDrops];
    private readonly float[] _dropY = new float[MaxRainDrops];
    private readonly float[] _dropAlpha = new float[MaxRainDrops];
    private bool _initialized;

    // Lightning
    private float _lightningTimer;
    private float _lightningFlashAlpha;
    private const float LightningIntervalMin = 10f;  // seconds
    private const float LightningIntervalMax = 20f;
    private const float LightningFlashDuration = 0.15f;
    private float _lightningFlashTimer;
    private readonly Random _rng = new();

    // Rain tint overlay
    private const float RainTintAlpha = 0.08f;
    private static readonly Color RainTintColor = new(0.3f, 0.4f, 0.7f, RainTintAlpha);

    // Rain drop base color (RGB only — alpha is applied per-drop based on _dropAlpha[i])
    private static readonly Color RainDropBaseColor = new(0.7f, 0.75f, 0.9f, 1f);

    // Opt 6: Pre-allocated point buffers for batched rain rendering.
    // Drops are bucketed into 5 alpha tiers; each tier emits one DrawMultiline call
    // instead of one DrawLine per drop — reducing 300 draw calls to at most 5.
    private const int RainAlphaBuckets = 5;
    private readonly Vector2[][] _rainBucketPoints = new Vector2[RainAlphaBuckets][];
    private readonly int[] _rainBucketCount = new int[RainAlphaBuckets];

    // Rain sound
    private AudioStreamPlayer? _rainSoundPlayer;
    private bool _rainSoundPlaying;
    private const float RainSoundDb = -18f;

    // Fade in/out
    private float _rainIntensity; // 0 = no rain, 1 = full rain
    private const float FadeInSpeed = 1.5f;  // per second
    private const float FadeOutSpeed = 2.0f;

    // Fog shader overlay
    private ColorRect? _fogShaderRect;

    public void Init(GameState state, SoundManager? soundManager, IResourceProvider? resources = null)
    {
        _state = state;
        _soundManager = soundManager;
        _resources = resources;
        _lightningTimer = (float)(_rng.NextDouble() * (LightningIntervalMax - LightningIntervalMin) + LightningIntervalMin);

        // Opt 6: allocate per-bucket point buffers (2 points per drop)
        for (int b = 0; b < RainAlphaBuckets; b++)
            _rainBucketPoints[b] = new Vector2[(MaxRainDrops / RainAlphaBuckets + 2) * 2];

        // Create rain sound player (looping)
        _rainSoundPlayer = new AudioStreamPlayer();
        _rainSoundPlayer.Bus = "Master";
        _rainSoundPlayer.VolumeDb = RainSoundDb;
        AddChild(_rainSoundPlayer);

        // Try to load rain sound
        TryLoadRainSound();

        // Set up animated fog shader overlay
        var fogShader = GD.Load<Shader>("res://Shaders/fog_overlay.gdshader");
        if (fogShader != null)
        {
            var noise = new FastNoiseLite { Seed = 42 };
            var noiseTexture = new NoiseTexture2D
            {
                Noise = noise,
                Seamless = true,
                Width = 512,
                Height = 512
            };
            var material = new ShaderMaterial { Shader = fogShader };
            material.SetShaderParameter("noise_texture", noiseTexture);

            _fogShaderRect = new ColorRect
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            _fogShaderRect.Material = material;

            // Put the fog rect inside a CanvasLayer so it renders on top of the world,
            // but size it explicitly to the viewport since anchors don't propagate from
            // CanvasLayer to Control children. Track viewport size changes.
            var canvasLayer = new CanvasLayer { Layer = 15 };
            AddChild(canvasLayer);
            canvasLayer.AddChild(_fogShaderRect);

            var vp = GetViewport();
            if (vp != null)
            {
                var vpSize = vp.GetVisibleRect().Size;
                _fogShaderRect.Position = Vector2.Zero;
                _fogShaderRect.Size = vpSize;
                vp.SizeChanged += () =>
                {
                    if (_fogShaderRect != null)
                        _fogShaderRect.Size = GetViewport().GetVisibleRect().Size;
                };
            }
        }
    }

    private void TryLoadRainSound()
    {
        if (_rainSoundPlayer == null) return;

        // Look for rain sound file in Data/Sounds/WAV/
        string[] candidates = { "lluviaout.wav", "lluvia.wav", "rain.wav" };

        if (_resources != null)
        {
            foreach (string candidate in candidates)
            {
                string relativePath = $"Sounds/WAV/{candidate}";
                if (_resources.Exists(relativePath))
                {
                    try
                    {
                        byte[] raw = _resources.ReadBytes(relativePath);
                        var stream = ParseWavLooping(raw);
                        if (stream != null)
                        {
                            _rainSoundPlayer.Stream = stream;
                            GD.Print($"[WEATHER] Rain sound loaded: {relativePath}");
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        GD.Print($"[WEATHER] Failed to load rain sound {relativePath}: {e.Message}");
                    }
                }
            }
        }
        else
        {
            // Fallback: try common filesystem paths
            string[] basePaths = {
                System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Data"),
                "Data"
            };

            foreach (string basePath in basePaths)
            {
                foreach (string candidate in candidates)
                {
                    string fullPath = System.IO.Path.Combine(basePath, "Sounds", "WAV", candidate);
                    if (System.IO.File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] raw = System.IO.File.ReadAllBytes(fullPath);
                            var stream = ParseWavLooping(raw);
                            if (stream != null)
                            {
                                _rainSoundPlayer.Stream = stream;
                                GD.Print($"[WEATHER] Rain sound loaded: {fullPath}");
                                return;
                            }
                        }
                        catch (Exception e)
                        {
                            GD.Print($"[WEATHER] Failed to load rain sound {fullPath}: {e.Message}");
                        }
                    }
                }
            }
        }

        GD.Print("[WEATHER] No rain sound file found (optional)");
    }

    /// <summary>
    /// Parse a WAV and set loop mode to Forward for continuous rain.
    /// </summary>
    private static AudioStreamWav? ParseWavLooping(byte[] raw)
    {
        if (raw.Length < 44) return null;
        if (raw[0] != 'R' || raw[1] != 'I' || raw[2] != 'F' || raw[3] != 'F') return null;
        if (raw[8] != 'W' || raw[9] != 'A' || raw[10] != 'V' || raw[11] != 'E') return null;

        int channels = 1, sampleRate = 22050, bitsPerSample = 16, audioFormat = 1;
        byte[]? pcmData = null;

        int pos = 12;
        while (pos + 8 <= raw.Length)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int chunkSize = BitConverter.ToInt32(raw, pos + 4);
            if (chunkSize < 0) break;
            int chunkDataStart = pos + 8;

            if (chunkId == "fmt " && chunkSize >= 16 && chunkDataStart + 16 <= raw.Length)
            {
                audioFormat = BitConverter.ToInt16(raw, chunkDataStart);
                channels = BitConverter.ToInt16(raw, chunkDataStart + 2);
                sampleRate = BitConverter.ToInt32(raw, chunkDataStart + 4);
                bitsPerSample = BitConverter.ToInt16(raw, chunkDataStart + 14);
            }
            else if (chunkId == "data")
            {
                int dataLen = Math.Min(chunkSize, raw.Length - chunkDataStart);
                if (dataLen > 0)
                {
                    pcmData = new byte[dataLen];
                    Array.Copy(raw, chunkDataStart, pcmData, 0, dataLen);
                }
            }

            pos = chunkDataStart + chunkSize;
            if (pos % 2 != 0) pos++;
        }

        if (audioFormat != 1 || pcmData == null || pcmData.Length == 0) return null;

        if (bitsPerSample == 8)
        {
            for (int i = 0; i < pcmData.Length; i++)
                pcmData[i] = (byte)(pcmData[i] - 128);
        }

        var wav = new AudioStreamWav();
        wav.Data = pcmData;
        wav.Format = bitsPerSample == 8
            ? AudioStreamWav.FormatEnum.Format8Bits
            : AudioStreamWav.FormatEnum.Format16Bits;
        wav.Stereo = channels >= 2;
        wav.MixRate = sampleRate;
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopEnd = pcmData.Length / (bitsPerSample / 8) / (channels >= 2 ? 2 : 1);
        return wav;
    }

    public override void _Process(double delta)
    {
        if (_state == null) return;

        float dt = (float)delta;
        bool raining = _state.Raining || _state.ZoneLluvia;

        // Fade intensity
        if (raining && _rainIntensity < 1f)
        {
            _rainIntensity = Math.Min(1f, _rainIntensity + FadeInSpeed * dt);
        }
        else if (!raining && _rainIntensity > 0f)
        {
            _rainIntensity = Math.Max(0f, _rainIntensity - FadeOutSpeed * dt);
        }

        // Update snow flakes
        if (_state.ZoneNieve)
        {
            if (!_snowInitialized)
            {
                for (int i = 0; i < MaxSnowFlakes; i++)
                {
                    _snowX[i] = (float)(_rng.NextDouble() * ViewW);
                    _snowY[i] = (float)(_rng.NextDouble() * ViewH);
                    _snowSway[i] = (float)((_rng.NextDouble() - 0.5) * 2.0 * SnowSway);
                }
                _snowInitialized = true;
            }

            for (int i = 0; i < MaxSnowFlakes; i++)
            {
                _snowX[i] += _snowSway[i] * dt;
                _snowY[i] += SnowSpeed * dt;

                if (_snowY[i] > ViewH + SnowRadius * 2 || _snowX[i] < -10 || _snowX[i] > ViewW + 10)
                {
                    _snowX[i] = (float)(_rng.NextDouble() * ViewW);
                    _snowY[i] = -SnowRadius * 2;
                    _snowSway[i] = (float)((_rng.NextDouble() - 0.5) * 2.0 * SnowSway);
                }
            }
        }

        // Rain sound control
        if (_rainSoundPlayer?.Stream != null)
        {
            if (_rainIntensity > 0.05f && !_rainSoundPlaying)
            {
                _rainSoundPlayer.Play();
                _rainSoundPlaying = true;
            }
            else if (_rainIntensity <= 0.05f && _rainSoundPlaying)
            {
                _rainSoundPlayer.Stop();
                _rainSoundPlaying = false;
            }

            if (_rainSoundPlaying)
            {
                // Fade volume with intensity
                float vol = RainSoundDb + Mathf.LinearToDb(Math.Max(0.01f, _rainIntensity));
                _rainSoundPlayer.VolumeDb = vol;
            }
        }

        bool hasWeather = _rainIntensity > 0f || _state.ZoneNieve || _state.ZoneNiebla;
        if (!hasWeather)
        {
            _lightningFlashAlpha = 0f;
            Visible = false;
            return;
        }

        Visible = true;

        // Initialize drop positions if needed
        if (!_initialized)
        {
            for (int i = 0; i < MaxRainDrops; i++)
            {
                _dropX[i] = (float)(_rng.NextDouble() * (ViewW + SpawnMarginX)) - SpawnMarginX * 0.5f;
                _dropY[i] = (float)(_rng.NextDouble() * ViewH);
                _dropAlpha[i] = 0.3f + (float)_rng.NextDouble() * 0.5f;
            }
            _initialized = true;
        }

        // Update rain drops
        for (int i = 0; i < MaxRainDrops; i++)
        {
            _dropX[i] += RainWindSpeed * dt;
            _dropY[i] += RainSpeed * dt;

            // Reset drops that go off screen
            if (_dropY[i] > ViewH + RainDropLength || _dropX[i] > ViewW + 20)
            {
                _dropX[i] = (float)(_rng.NextDouble() * (ViewW + SpawnMarginX)) - SpawnMarginX;
                _dropY[i] = -RainDropLength - (float)(_rng.NextDouble() * 60f);
                _dropAlpha[i] = 0.3f + (float)_rng.NextDouble() * 0.5f;
            }
        }

        // Lightning timer
        if (raining)
        {
            _lightningTimer -= dt;
            if (_lightningTimer <= 0f)
            {
                // Trigger flash
                _lightningFlashAlpha = 0.6f;
                _lightningFlashTimer = LightningFlashDuration;
                _lightningTimer = (float)(_rng.NextDouble() * (LightningIntervalMax - LightningIntervalMin) + LightningIntervalMin);
            }
        }

        // Decay flash
        if (_lightningFlashTimer > 0f)
        {
            _lightningFlashTimer -= dt;
            _lightningFlashAlpha = Math.Max(0f, _lightningFlashTimer / LightningFlashDuration) * 0.6f;
        }
        else
        {
            _lightningFlashAlpha = 0f;
        }

        // Update fog shader overlay
        if (_fogShaderRect != null)
        {
            bool showFog = _state.ZoneNiebla && _state.ZoneFogDensity > 0;
            _fogShaderRect.Visible = showFog;
            if (showFog && _fogShaderRect.Material is ShaderMaterial sm)
            {
                sm.SetShaderParameter("density", _state.ZoneFogDensity / 255f);
                sm.SetShaderParameter("fog_color", new Color(_state.ZoneFogR / 255f, _state.ZoneFogG / 255f, _state.ZoneFogB / 255f, 1f));
                sm.SetShaderParameter("speed", new Vector2(_state.ZoneFogSpeedX / 100f, _state.ZoneFogSpeedY / 100f));
                // Fog is screen-locked: no world_offset uniform. Character motion
                // must not shift the noise pattern. The only movement is the
                // `speed * TIME` drift from the shader, configurable per zone.
            }
        }

        // Redraw when any effect is active
        if (_rainIntensity > 0f || _lightningFlashAlpha > 0f || _state.ZoneNieve || _state.ZoneNiebla)
            QueueRedraw();
    }

    private void DrawSnow()
    {
        var snowColor = new Color(1f, 1f, 1f, SnowAlpha);
        for (int i = 0; i < MaxSnowFlakes; i++)
            DrawCircle(new Vector2(_snowX[i], _snowY[i]), SnowRadius, snowColor);
    }

    private void DrawNiebla()
    {
        DrawRect(new Rect2(0, 0, ViewW, ViewH), new Color(0.5f, 0.55f, 0.6f, 0.35f));
    }

    public override void _Draw()
    {
        if (_state == null) return;

        // Snow and niebla are independent of rain intensity
        if (_state.ZoneNieve) DrawSnow();
        // CPU fog rect: only when niebla is active but no shader fog density (backwards compat)
        if (_state.ZoneNiebla && _state.ZoneFogDensity == 0) DrawNiebla();

        if (_rainIntensity <= 0f) return;

        float alpha = _rainIntensity;

        // Blue-ish tint overlay
        var tint = new Color(RainTintColor.R, RainTintColor.G, RainTintColor.B, RainTintAlpha * alpha);
        DrawRect(new Rect2(0, 0, ViewW, ViewH), tint);

        // Opt 6: Batch rain drops into alpha buckets and use DrawMultiline —
        // reduces 300 DrawLine calls to at most RainAlphaBuckets calls per frame.
        // Alpha range per drop: _dropAlpha[i] in [0.3, 0.8], multiplied by global alpha.
        // Buckets span [0, 1] in equal steps; bucket index = clamp(floor(a * buckets), 0, buckets-1).
        {
            float ddx = RainWindSpeed / RainSpeed * RainDropLength;
            Array.Clear(_rainBucketCount, 0, RainAlphaBuckets);

            for (int i = 0; i < MaxRainDrops; i++)
            {
                float a = _dropAlpha[i] * alpha;
                int bucket = Math.Clamp((int)(a * RainAlphaBuckets), 0, RainAlphaBuckets - 1);
                var pts = _rainBucketPoints[bucket];
                int idx = _rainBucketCount[bucket] * 2;
                // Grow bucket array if needed (safety — bucket size is pre-allocated for average)
                if (idx + 1 >= pts.Length)
                {
                    Array.Resize(ref _rainBucketPoints[bucket], pts.Length * 2);
                    pts = _rainBucketPoints[bucket];
                }
                float x = _dropX[i];
                float y = _dropY[i];
                pts[idx]     = new Vector2(x, y);
                pts[idx + 1] = new Vector2(x + ddx, y + RainDropLength);
                _rainBucketCount[bucket]++;
            }

            // Emit one DrawMultiline per non-empty bucket (at most RainAlphaBuckets calls)
            for (int b = 0; b < RainAlphaBuckets; b++)
            {
                int count = _rainBucketCount[b];
                if (count == 0) continue;
                // Representative alpha = midpoint of bucket range
                float bucketAlpha = ((b + 0.5f) / RainAlphaBuckets);
                var bColor = new Color(RainDropBaseColor.R, RainDropBaseColor.G, RainDropBaseColor.B, bucketAlpha);
                // DrawMultiline expects a flat array of paired points
                var pts = _rainBucketPoints[b];
                // Slice to only the used portion — Godot's DrawMultiline takes ReadOnlySpan/Array
                DrawMultiline(pts[..(count * 2)], bColor, RainDropWidth);
            }
        }

        // Lightning flash overlay (white)
        if (_lightningFlashAlpha > 0.01f)
        {
            DrawRect(new Rect2(0, 0, ViewW, ViewH), new Color(1f, 1f, 1f, _lightningFlashAlpha));
        }
    }
}

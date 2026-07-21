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

    // Fog shader overlay — world-space mask-based fog.
    // A single ColorRect covers the whole map. The shader samples a fog mask
    // (1 pixel per tile) to decide where to draw fog. Layer 2/3/4 occlude.
    // The player's world position is passed for the smoke-break fade.
    private Node2D? _fogWorldLayer;
    private ColorRect? _fogShaderRect;
    private Image? _fogMaskImage;
    private ImageTexture? _fogMaskTexture;
    private int _fogMaskW, _fogMaskH;
    private bool _fogMaskDirty = true;
    private int _fogMaskZoneX1, _fogMaskZoneY1, _fogMaskZoneX2, _fogMaskZoneY2;

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

        // Set up world-space fog shader overlay. The fog is a ColorRect positioned
        // at the zone's world rectangle (X1..X2 × Y1..Y2 tile coords × 32 px).
        // Its parent Node2D gets Position = camera offset each frame (mirroring
        // WorldRenderer's transform), so the rect stays glued to the tiles.
        var fogShader = GD.Load<Shader>("res://Shaders/fog_overlay.gdshader");
        if (fogShader != null)
        {
            var noise = new FastNoiseLite { Seed = 42 };
            var noiseTexture = new NoiseTexture2D
            {
                Noise = noise,
                Seamless = true,
                Width = 512,
                Height = 512,
            };
            var material = new ShaderMaterial { Shader = fogShader };
            material.SetShaderParameter("noise_texture", noiseTexture);

            _fogWorldLayer = new Node2D { Name = "FogWorldLayer", ZIndex = 10 };
            AddChild(_fogWorldLayer);

            _fogShaderRect = new ColorRect
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = material,
                Visible = false,
            };
            _fogWorldLayer.AddChild(_fogShaderRect);
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

        // Update world-space mask-based fog shader overlay.
        // One ColorRect covers the whole map. The mask texture says which
        // tiles have fog (zone bounds minus layer-occluded tiles). The shader
        // also reads player_world_pos to dissolve fog locally around the
        // character (smoke-breaking effect).
        if (_fogShaderRect != null && _fogWorldLayer != null && _state.MapData != null)
        {
            bool hasZoneBounds = _state.CurrentZoneX2 > 0 && _state.CurrentZoneY2 > 0;
            bool showFog = _state.ZoneNiebla && _state.ZoneFogDensity > 0 && hasZoneBounds;
            _fogShaderRect.Visible = showFog;
            if (showFog)
            {
                // Match WorldRenderer camera transform so the fog rect lines up
                // with the drawn tiles. World pixel 0 → (1 - uX + hX) * TS + offX.
                var cam = WorldRenderer.CurrentCamera;
                const float TS = 32f;
                float hX = ResolutionManager.HalfTilesX;
                float hY = ResolutionManager.HalfTilesY;
                _fogWorldLayer.Position = new Vector2(
                    (1 - cam.UserX + hX) * TS + cam.PixelOffsetX,
                    (1 - cam.UserY + hY) * TS + cam.PixelOffsetY);

                // Rect covers the entire map so the shader can sample the mask
                // at any tile inside the map bounds.
                int mapW = _state.MapData.Width;
                int mapH = _state.MapData.Height;
                float worldW = mapW * TS;
                float worldH = mapH * TS;
                _fogShaderRect.Position = Vector2.Zero;
                _fogShaderRect.Size = new Vector2(worldW, worldH);

                // Rebuild mask when zone bounds change OR map changed
                if (_fogMaskDirty
                    || _fogMaskW != mapW || _fogMaskH != mapH
                    || _fogMaskZoneX1 != _state.CurrentZoneX1
                    || _fogMaskZoneY1 != _state.CurrentZoneY1
                    || _fogMaskZoneX2 != _state.CurrentZoneX2
                    || _fogMaskZoneY2 != _state.CurrentZoneY2)
                {
                    RebuildFogMask(mapW, mapH);
                    _fogMaskDirty = false;
                }

                if (_fogShaderRect.Material is ShaderMaterial sm)
                {
                    sm.SetShaderParameter("density", _state.ZoneFogDensity / 255f);
                    sm.SetShaderParameter("fog_color",
                        new Color(_state.ZoneFogR / 255f, _state.ZoneFogG / 255f, _state.ZoneFogB / 255f, 1f));
                    sm.SetShaderParameter("speed",
                        new Vector2(_state.ZoneFogSpeedX / 100f, _state.ZoneFogSpeedY / 100f));
                    if (_fogMaskTexture != null)
                        sm.SetShaderParameter("fog_mask", _fogMaskTexture);
                    sm.SetShaderParameter("map_tile_size", new Vector2(mapW, mapH));
                    sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
                    sm.SetShaderParameter("rect_world_size", new Vector2(worldW, worldH));
                    // Player's world position (tile center). Fog dissolves inside the break radius.
                    float playerWorldX = (_state.UserPosX - 0.5f) * TS;
                    float playerWorldY = (_state.UserPosY - 0.5f) * TS;
                    sm.SetShaderParameter("player_world_pos", new Vector2(playerWorldX, playerWorldY));
                    sm.SetShaderParameter("player_break_radius", 144f);
                    sm.SetShaderParameter("free_smoke", _state.MapData.FogFreeSmoke ? 1.0f : 0.0f);
                }
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

    /// <summary>Called from outside (e.g., map change, zone change) to force
    /// the fog mask to rebuild on the next _Process tick.</summary>
    public void MarkFogMaskDirty() => _fogMaskDirty = true;

    /// <summary>Build the R8 per-tile fog mask for the current map + zone.
    /// 1 = tile has fog (inside zone rect, no layer 2/3/4 content), 0 otherwise.</summary>
    private void RebuildFogMask(int mapW, int mapH)
    {
        if (_state == null || _state.MapData == null) return;

        if (_fogMaskImage == null || _fogMaskImage.GetWidth() != mapW || _fogMaskImage.GetHeight() != mapH)
        {
            _fogMaskImage = Image.CreateEmpty(mapW, mapH, false, Image.Format.R8);
            _fogMaskTexture = null;
        }
        _fogMaskImage.Fill(Colors.Black);

        int zx1 = Math.Max(1, (int)_state.CurrentZoneX1);
        int zy1 = Math.Max(1, (int)_state.CurrentZoneY1);
        int zx2 = Math.Min(mapW, (int)_state.CurrentZoneX2);
        int zy2 = Math.Min(mapH, (int)_state.CurrentZoneY2);

        for (int y = zy1; y <= zy2; y++)
        {
            for (int x = zx1; x <= zx2; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                // Skip tiles with upper-layer content — objects/trees/roofs
                // break the fog, so it flows around them cleanly.
                if (tile.Layer2 != 0 || tile.Layer3 != 0 || tile.Layer4 != 0)
                    continue;
                _fogMaskImage.SetPixel(x - 1, y - 1, Colors.White);
            }
        }

        if (_fogMaskTexture == null)
            _fogMaskTexture = ImageTexture.CreateFromImage(_fogMaskImage);
        else
            _fogMaskTexture.Update(_fogMaskImage);

        _fogMaskW = mapW;
        _fogMaskH = mapH;
        _fogMaskZoneX1 = _state.CurrentZoneX1;
        _fogMaskZoneY1 = _state.CurrentZoneY1;
        _fogMaskZoneX2 = _state.CurrentZoneX2;
        _fogMaskZoneY2 = _state.CurrentZoneY2;
    }

    public override void _Draw()
    {
        if (_state == null) return;

        // Niebla is rendered by the world-space fog ColorRect (see Init) —
        // not by _Draw. The old full-screen CPU rect was removed because fog
        // must be bounded to the zone's world rect, not the entire viewport.
        if (_state.ZoneNieve) DrawSnow();

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

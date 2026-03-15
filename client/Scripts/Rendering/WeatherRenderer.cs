using System;
using Godot;
using ArgentumNextgen.Game;

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

    // Rain particle system
    private const int MaxRainDrops = 300;
    private const float RainSpeed = 420f;        // pixels/sec vertical
    private const float RainWindSpeed = 120f;    // pixels/sec horizontal (diagonal)
    private const float RainDropLength = 12f;    // length of each rain line
    private const float RainDropWidth = 1.2f;
    // Dynamic viewport dimensions from ResolutionManager
    private static int ViewW => ResolutionManager.ViewportPixelW;
    private static int ViewH => ResolutionManager.ViewportPixelH;
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

    // Rain sound
    private AudioStreamPlayer? _rainSoundPlayer;
    private bool _rainSoundPlaying;
    private const float RainSoundDb = -18f;

    // Fade in/out
    private float _rainIntensity; // 0 = no rain, 1 = full rain
    private const float FadeInSpeed = 1.5f;  // per second
    private const float FadeOutSpeed = 2.0f;

    public void Init(GameState state, SoundManager? soundManager)
    {
        _state = state;
        _soundManager = soundManager;
        _lightningTimer = (float)(_rng.NextDouble() * (LightningIntervalMax - LightningIntervalMin) + LightningIntervalMin);

        // Create rain sound player (looping)
        _rainSoundPlayer = new AudioStreamPlayer();
        _rainSoundPlayer.Bus = "Master";
        _rainSoundPlayer.VolumeDb = RainSoundDb;
        AddChild(_rainSoundPlayer);

        // Try to load rain sound
        TryLoadRainSound();
    }

    private void TryLoadRainSound()
    {
        if (_rainSoundPlayer == null) return;

        // Look for rain sound file in Data/Sounds/WAV/
        string[] candidates = { "lluviaout.wav", "lluvia.wav", "rain.wav" };

        // Walk up to find Data/ folder
        var parent = GetParent();
        while (parent != null)
        {
            if (parent is Node2D)
                parent = parent.GetParent();
            else
                break;
        }

        // Try common paths
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

            if (chunkId == "fmt " && chunkSize >= 16)
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
        bool raining = _state.Raining;

        // Fade intensity
        if (raining && _rainIntensity < 1f)
        {
            _rainIntensity = Math.Min(1f, _rainIntensity + FadeInSpeed * dt);
        }
        else if (!raining && _rainIntensity > 0f)
        {
            _rainIntensity = Math.Max(0f, _rainIntensity - FadeOutSpeed * dt);
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

        if (_rainIntensity <= 0f)
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

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_rainIntensity <= 0f) return;

        float alpha = _rainIntensity;

        // Blue-ish tint overlay
        var tint = new Color(RainTintColor.R, RainTintColor.G, RainTintColor.B, RainTintAlpha * alpha);
        DrawRect(new Rect2(0, 0, ViewW, ViewH), tint);

        // Rain drops (diagonal lines)
        var dropColor = new Color(0.7f, 0.75f, 0.9f, 1f);
        for (int i = 0; i < MaxRainDrops; i++)
        {
            float a = _dropAlpha[i] * alpha;
            var color = new Color(dropColor.R, dropColor.G, dropColor.B, a);
            float x = _dropX[i];
            float y = _dropY[i];
            // Diagonal line: top-left to bottom-right
            float dx = RainWindSpeed / RainSpeed * RainDropLength;
            DrawLine(
                new Vector2(x, y),
                new Vector2(x + dx, y + RainDropLength),
                color,
                RainDropWidth,
                true
            );
        }

        // Lightning flash overlay (white)
        if (_lightningFlashAlpha > 0.01f)
        {
            DrawRect(new Rect2(0, 0, ViewW, ViewH), new Color(1f, 1f, 1f, _lightningFlashAlpha));
        }
    }
}

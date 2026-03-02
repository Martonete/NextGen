using Godot;
using System.Collections.Generic;

namespace TierrasSagradasAO.Game;

/// <summary>
/// Manages sound effects (WAV/MP3) and music playback.
/// VB6: Audio.PlayWave, Audio.PlayMIDI — sounds from Data/Sounds/WAV/, music from Data/Sounds/MIDI/ (or MP3 fallback).
/// </summary>
public partial class SoundManager : Node
{
    private const int MaxConcurrentSounds = 8;
    private const float SfxHeadroomDb = -8.0f;     // headroom so stacked SFX don't clip
    private const float MusicHeadroomDb = -6.0f;    // music headroom
    private const float DefaultSfxVolume = SfxHeadroomDb;
    private const float DefaultMusicVolume = MusicHeadroomDb;

    private readonly List<AudioStreamPlayer> _sfxPlayers = new();
    private AudioStreamPlayer? _musicPlayer;
    private readonly Dictionary<int, AudioStream?> _sfxCache = new();
    private readonly Dictionary<int, AudioStream?> _musCache = new();

    private string _dataPath = "";
    private int _currentMusicId;
    private bool _soundEnabled = true;
    private bool _musicEnabled = true;

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => _soundEnabled = value;
    }

    public bool MusicEnabled
    {
        get => _musicEnabled;
        set
        {
            _musicEnabled = value;
            if (!value) StopMusic();
        }
    }

    /// <summary>
    /// Set music volume from 0-100 percentage. Maps to dB scale with headroom.
    /// 100% = MusicHeadroomDb (not 0 dB) to prevent clipping.
    /// </summary>
    public void SetMusicVolume(int percent)
    {
        float db = percent <= 0 ? -80f : Mathf.LinearToDb(percent / 100f) + MusicHeadroomDb;
        if (_musicPlayer != null) _musicPlayer.VolumeDb = db;
    }

    /// <summary>
    /// Set SFX volume from 0-100 percentage. Maps to dB scale with headroom.
    /// 100% = SfxHeadroomDb (not 0 dB) — AO WAVs are already normalized near 0 dBFS
    /// and multiple concurrent sounds stack on the Master bus.
    /// </summary>
    public void SetSfxVolume(int percent)
    {
        float db = percent <= 0 ? -80f : Mathf.LinearToDb(percent / 100f) + SfxHeadroomDb;
        foreach (var p in _sfxPlayers) p.VolumeDb = db;
    }

    public void Init(string dataPath)
    {
        _dataPath = dataPath;

        // Create SFX player pool
        for (int i = 0; i < MaxConcurrentSounds; i++)
        {
            var player = new AudioStreamPlayer();
            player.Bus = "Master";
            player.VolumeDb = DefaultSfxVolume;
            AddChild(player);
            _sfxPlayers.Add(player);
        }

        // Create music player
        _musicPlayer = new AudioStreamPlayer();
        _musicPlayer.Bus = "Master";
        _musicPlayer.VolumeDb = DefaultMusicVolume;
        AddChild(_musicPlayer);

        // Test load sound 2 (common attack sound) to verify system works
        var test = LoadWav(2);
        GD.Print($"[SND] Init done. Test load sound 2: {(test != null ? "OK" : "FAIL")}");
    }

    public void PlaySound(int soundId)
    {
        if (!_soundEnabled || soundId <= 0) return;

        if (!_sfxCache.TryGetValue(soundId, out var stream))
        {
            stream = LoadWav(soundId) ?? LoadMp3(soundId);
            _sfxCache[soundId] = stream;
            if (stream == null)
                GD.Print($"[SND] Could not load sound {soundId}");
        }

        if (stream == null) return;

        // Find a free player (not currently playing)
        AudioStreamPlayer? player = null;
        foreach (var p in _sfxPlayers)
        {
            if (!p.Playing)
            {
                player = p;
                break;
            }
        }

        // If all busy, steal the first one (oldest sound)
        player ??= _sfxPlayers[0];

        player.Stream = stream;
        player.Play();
    }

    public void PlayMusic(int musicId)
    {
        if (musicId <= 0)
        {
            StopMusic();
            return;
        }

        if (musicId == _currentMusicId && _musicPlayer != null && _musicPlayer.Playing)
            return;

        _currentMusicId = musicId;
        if (!_musicEnabled) return;

        if (!_musCache.TryGetValue(musicId, out var stream))
        {
            // Music: try MP3 first (Godot can't play MIDI), then WAV
            stream = LoadMp3(musicId) ?? LoadWav(musicId);
            _musCache[musicId] = stream;
        }

        if (stream == null)
        {
            GD.Print($"[SND] No music file for ID {musicId}");
            return;
        }

        if (_musicPlayer != null)
        {
            _musicPlayer.Stream = stream;
            _musicPlayer.Play();
        }
    }

    public void StopMusic()
    {
        _currentMusicId = 0;
        _musicPlayer?.Stop();
    }

    /// <summary>
    /// Load a WAV file. Tries ResourceLoader first (editor), then raw file read (exported build).
    /// </summary>
    private AudioStream? LoadWav(int id)
    {
        // Method 1: Godot ResourceLoader (works when Godot has imported the file)
        string resPath = $"res://Data/Sounds/WAV/{id}.wav";
        if (ResourceLoader.Exists(resPath))
        {
            try
            {
                var s = ResourceLoader.Load<AudioStream>(resPath);
                if (s != null) return s;
            }
            catch { /* fall through */ }
        }

        // Method 2: Read raw file bytes and create AudioStreamWav manually
        string filePath = System.IO.Path.Combine(_dataPath, "Sounds", "WAV", $"{id}.wav");
        if (!System.IO.File.Exists(filePath)) return null;

        try
        {
            byte[] raw = System.IO.File.ReadAllBytes(filePath);
            return ParseWav(raw);
        }
        catch (System.Exception e)
        {
            GD.Print($"[SND] WAV parse failed for {id}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Load an MP3 file. Tries ResourceLoader first, then raw file read.
    /// </summary>
    private AudioStream? LoadMp3(int id)
    {
        string resPath = $"res://Data/Sounds/MP3/{id}.mp3";
        if (ResourceLoader.Exists(resPath))
        {
            try
            {
                var s = ResourceLoader.Load<AudioStream>(resPath);
                if (s != null) return s;
            }
            catch { /* fall through */ }
        }

        string filePath = System.IO.Path.Combine(_dataPath, "Sounds", "MP3", $"{id}.mp3");
        if (!System.IO.File.Exists(filePath)) return null;

        try
        {
            byte[] raw = System.IO.File.ReadAllBytes(filePath);
            var mp3 = new AudioStreamMP3();
            mp3.Data = raw;
            return mp3;
        }
        catch (System.Exception e)
        {
            GD.Print($"[SND] MP3 load failed for {id}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse a WAV file from raw bytes into an AudioStreamWav.
    /// Handles standard PCM WAV (8/16 bit, mono/stereo).
    /// </summary>
    private static AudioStreamWav? ParseWav(byte[] raw)
    {
        // Minimal WAV parser: RIFF header → fmt chunk → data chunk
        if (raw.Length < 44) return null;
        if (raw[0] != 'R' || raw[1] != 'I' || raw[2] != 'F' || raw[3] != 'F') return null;
        if (raw[8] != 'W' || raw[9] != 'A' || raw[10] != 'V' || raw[11] != 'E') return null;

        int channels = 1;
        int sampleRate = 22050;
        int bitsPerSample = 16;
        byte[]? pcmData = null;

        int pos = 12;
        while (pos + 8 <= raw.Length)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int chunkSize = System.BitConverter.ToInt32(raw, pos + 4);
            int chunkDataStart = pos + 8;

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                int audioFormat = System.BitConverter.ToInt16(raw, chunkDataStart);
                if (audioFormat != 1) return null; // Only PCM supported
                channels = System.BitConverter.ToInt16(raw, chunkDataStart + 2);
                sampleRate = System.BitConverter.ToInt32(raw, chunkDataStart + 4);
                bitsPerSample = System.BitConverter.ToInt16(raw, chunkDataStart + 14);
            }
            else if (chunkId == "data")
            {
                int dataLen = System.Math.Min(chunkSize, raw.Length - chunkDataStart);
                pcmData = new byte[dataLen];
                System.Array.Copy(raw, chunkDataStart, pcmData, 0, dataLen);
            }

            pos = chunkDataStart + chunkSize;
            // Chunks are word-aligned
            if (pos % 2 != 0) pos++;
        }

        if (pcmData == null || pcmData.Length == 0) return null;

        var wav = new AudioStreamWav();
        wav.Data = pcmData;
        wav.Format = bitsPerSample == 8
            ? AudioStreamWav.FormatEnum.Format8Bits
            : AudioStreamWav.FormatEnum.Format16Bits;
        wav.Stereo = channels >= 2;
        wav.MixRate = sampleRate;
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;

        return wav;
    }
}

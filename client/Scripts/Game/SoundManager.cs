using Godot;
using System.Collections.Generic;

namespace ArgentumNextgen.Game;

/// <summary>
/// Manages sound effects (WAV/MP3) and music playback.
/// VB6: Audio.PlayWave, Audio.PlayMIDI — sounds from Data/Sounds/WAV/, music from Data/Sounds/MIDI/ (or MP3 fallback).
/// </summary>
public partial class SoundManager : Node
{
    private const int MaxConcurrentSounds = 8;
    private const float SfxHeadroomDb = -10.0f;     // headroom so stacked SFX don't clip
    private const float MusicHeadroomDb = -14.0f;   // music headroom (AO MP3s are hot)
    private const float DefaultMusicVolume = MusicHeadroomDb;

    // VB6 spatial audio constants (clsAudio.cls)
    private const float MaxDistanceToSource = 150f;  // VB6: MAX_DISTANCE_TO_SOURCE
    private const float SilentDb = -80f;             // effectively silent in Godot

    // VB6 sound IDs — well-known event sounds
    public const int SND_CLICK = 1;       // UI click
    public const int SND_SWING = 2;       // melee swing
    public const int SND_LEVEL = 5;       // level up fanfare
    public const int SND_IMPACTO = 10;    // hit impact
    public const int SND_DEATH = 11;      // player death
    public const int SND_REVIVE = 41;     // resurrection/revive

    private readonly List<AudioStreamPlayer> _sfxPlayers = new();
    private AudioStreamPlayer? _musicPlayer;
    private readonly Dictionary<int, AudioStream?> _sfxCache = new();
    private readonly Dictionary<int, AudioStream?> _musCache = new();

    private string _dataPath = "";
    private int _currentMusicId;
    private bool _soundEnabled = true;
    private bool _musicEnabled = true;
    private float _sfxVolumeDb = SfxHeadroomDb; // current SFX base volume (user setting)

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
        _sfxVolumeDb = percent <= 0 ? -80f : Mathf.LinearToDb(percent / 100f) + SfxHeadroomDb;
    }

    public void Init(string dataPath)
    {
        _dataPath = dataPath;

        // Create SFX player pool
        for (int i = 0; i < MaxConcurrentSounds; i++)
        {
            var player = new AudioStreamPlayer();
            player.Bus = "Master";
            player.VolumeDb = _sfxVolumeDb;
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

    /// <summary>
    /// Stop all currently playing SFX. Called on map change to prevent stale sounds.
    /// </summary>
    public void StopAllSfx()
    {
        foreach (var p in _sfxPlayers)
            if (p.Playing) p.Stop();
    }

    public void PlaySound(int soundId)
    {
        PlaySoundInternal(soundId, _sfxVolumeDb);
    }

    /// <summary>
    /// Play a named sound file (e.g. "click.wav") — VB6 uses named constants for UI sounds.
    /// </summary>
    public void PlayNamedSound(string fileName)
    {
        if (!_soundEnabled || string.IsNullOrEmpty(fileName)) return;

        // Use negative hash as cache key to avoid collision with numeric IDs
        int cacheKey = -Math.Abs(fileName.GetHashCode());

        if (!_sfxCache.TryGetValue(cacheKey, out var stream))
        {
            string filePath = System.IO.Path.Combine(_dataPath, "Sounds", "WAV", fileName);
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    byte[] raw = System.IO.File.ReadAllBytes(filePath);
                    stream = ParseWav(raw);
                }
                catch { stream = null; }
            }
            _sfxCache[cacheKey] = stream;
        }

        if (stream == null) return;

        AudioStreamPlayer? player = null;
        foreach (var p in _sfxPlayers)
            if (!p.Playing) { player = p; break; }
        player ??= _sfxPlayers[0];

        player.VolumeDb = _sfxVolumeDb;
        player.Stream = stream;
        player.Play();
    }

    /// <summary>
    /// Play a sound with VB6-style spatial audio (distance attenuation).
    /// VB6: clsAudio.PlayWave → Update3DSound with Euclidean distance.
    /// srcX/srcY=0 means no spatial (UI/self sounds) — plays at full volume.
    /// </summary>
    public void PlaySoundAt(int soundId, int srcX, int srcY, int listenerX, int listenerY)
    {
        // No spatial info → play at full volume (matches VB6: srcX=0,srcY=0 skips 3D)
        if (srcX == 0 && srcY == 0)
        {
            PlaySound(soundId);
            return;
        }

        float dx = srcX - listenerX;
        float dy = srcY - listenerY;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        // Beyond max distance — don't play at all
        if (distance > MaxDistanceToSource) return;

        // VB6 formula: volume = SndVolume + (dist/MAX_DIST) * (SILENT - SndVolume)
        // Linear interpolation in dB space from full volume to silent
        float attenuation = distance / MaxDistanceToSource;
        float volumeDb = _sfxVolumeDb + attenuation * (SilentDb - _sfxVolumeDb);

        PlaySoundInternal(soundId, volumeDb);
    }

    // Track which sound ID each player is currently playing (for dedup)
    private readonly int[] _sfxPlayerSoundId = new int[MaxConcurrentSounds];

    private void PlaySoundInternal(int soundId, float volumeDb)
    {
        if (!_soundEnabled || soundId <= 0) return;

        if (!_sfxCache.TryGetValue(soundId, out var stream))
        {
            stream = LoadWav(soundId) ?? LoadWavAsMp3(soundId) ?? LoadMp3(soundId);
            _sfxCache[soundId] = stream;
            if (stream == null)
                GD.Print($"[SND] Could not load sound {soundId}");
        }

        if (stream == null) return;

        // If this sound ID is already playing, restart it instead of stacking
        for (int i = 0; i < _sfxPlayers.Count; i++)
        {
            if (_sfxPlayers[i].Playing && _sfxPlayerSoundId[i] == soundId)
            {
                _sfxPlayers[i].VolumeDb = volumeDb;
                _sfxPlayers[i].Seek(0);
                return;
            }
        }

        // Find a free player (not currently playing)
        int idx = -1;
        for (int i = 0; i < _sfxPlayers.Count; i++)
        {
            if (!_sfxPlayers[i].Playing) { idx = i; break; }
        }

        // If all busy, steal the first one (oldest sound)
        if (idx < 0) idx = 0;

        _sfxPlayers[idx].VolumeDb = volumeDb;
        _sfxPlayers[idx].Stream = stream;
        _sfxPlayers[idx].Play();
        _sfxPlayerSoundId[idx] = soundId;
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
    /// Load a WAV file from the external Data/Sounds/WAV/ folder.
    /// </summary>
    private AudioStream? LoadWav(int id)
    {
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
    /// Load an MP3 file from the external Data/Sounds/MP3/ folder.
    /// </summary>
    private AudioStream? LoadMp3(int id)
    {
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
    /// Handles PCM WAV (8/16 bit, mono/stereo).
    /// Non-PCM formats (ADPCM, MP3-in-WAV codec 0x55) return null
    /// so the caller can try loading as MP3 instead.
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
        int audioFormat = 1;
        byte[]? pcmData = null;

        int pos = 12;
        while (pos + 8 <= raw.Length)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int chunkSize = System.BitConverter.ToInt32(raw, pos + 4);
            if (chunkSize < 0) break; // corrupt
            int chunkDataStart = pos + 8;

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                audioFormat = System.BitConverter.ToInt16(raw, chunkDataStart);
                channels = System.BitConverter.ToInt16(raw, chunkDataStart + 2);
                sampleRate = System.BitConverter.ToInt32(raw, chunkDataStart + 4);
                bitsPerSample = System.BitConverter.ToInt16(raw, chunkDataStart + 14);
            }
            else if (chunkId == "data")
            {
                int dataLen = System.Math.Min(chunkSize, raw.Length - chunkDataStart);
                if (dataLen > 0)
                {
                    pcmData = new byte[dataLen];
                    System.Array.Copy(raw, chunkDataStart, pcmData, 0, dataLen);
                }
            }

            pos = chunkDataStart + chunkSize;
            // Chunks are word-aligned
            if (pos % 2 != 0) pos++;
        }

        // Only handle PCM (format 1). Non-PCM (ADPCM=2/17, MP3-in-WAV=0x55) not supported.
        if (audioFormat != 1) return null;

        if (pcmData == null || pcmData.Length == 0) return null;

        // WAV 8-bit PCM uses UNSIGNED samples (0-255, center=128).
        // Godot AudioStreamWav Format8Bits expects SIGNED (-128 to 127, center=0).
        // Without this conversion, 8-bit sounds are massively distorted.
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
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;

        return wav;
    }

    /// <summary>
    /// Try to load a WAV file that might actually contain MP3 data (codec 0x55).
    /// Falls back to loading the raw file as MP3.
    /// </summary>
    private AudioStream? LoadWavAsMp3(int id)
    {
        string filePath = System.IO.Path.Combine(_dataPath, "Sounds", "WAV", $"{id}.wav");
        if (!System.IO.File.Exists(filePath)) return null;

        try
        {
            byte[] raw = System.IO.File.ReadAllBytes(filePath);
            // Check for MP3-in-WAV: RIFF header with format 0x55
            if (raw.Length > 20 && raw[0] == 'R' && raw[1] == 'I')
            {
                int fmt = FindFmtFormat(raw);
                if (fmt == 0x55 || fmt == 0x50) // MPEG Layer 3 or MPEG
                {
                    // Extract the data chunk and feed as MP3
                    byte[]? dataChunk = ExtractDataChunk(raw);
                    if (dataChunk != null && dataChunk.Length > 0)
                    {
                        var mp3 = new AudioStreamMP3();
                        mp3.Data = dataChunk;
                        return mp3;
                    }
                }
            }
        }
        catch { /* fall through */ }
        return null;
    }

    private static int FindFmtFormat(byte[] raw)
    {
        int pos = 12;
        while (pos + 8 <= raw.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int size = System.BitConverter.ToInt32(raw, pos + 4);
            if (size < 0) break;
            if (id == "fmt " && size >= 2)
                return System.BitConverter.ToInt16(raw, pos + 8);
            pos = pos + 8 + size;
            if (pos % 2 != 0) pos++;
        }
        return 0;
    }

    private static byte[]? ExtractDataChunk(byte[] raw)
    {
        int pos = 12;
        while (pos + 8 <= raw.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int size = System.BitConverter.ToInt32(raw, pos + 4);
            if (size < 0) break;
            if (id == "data")
            {
                int dataLen = System.Math.Min(size, raw.Length - pos - 8);
                if (dataLen <= 0) return null;
                byte[] data = new byte[dataLen];
                System.Array.Copy(raw, pos + 8, data, 0, dataLen);
                return data;
            }
            pos = pos + 8 + size;
            if (pos % 2 != 0) pos++;
        }
        return null;
    }
}

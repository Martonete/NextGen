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
    private const float DefaultSfxVolume = 0.0f;   // dB (0 = full volume)
    private const float DefaultMusicVolume = -6.0f; // dB (slightly quieter)

    private readonly List<AudioStreamPlayer> _sfxPlayers = new();
    private AudioStreamPlayer? _musicPlayer;
    private readonly Dictionary<string, AudioStream> _cache = new();

    private string _wavPath = "";
    private string _mp3Path = "";
    private string _midiPath = "";
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

    public void Init(string dataPath)
    {
        _wavPath = System.IO.Path.Combine(dataPath, "Sounds", "WAV");
        _mp3Path = System.IO.Path.Combine(dataPath, "Sounds", "MP3");
        _midiPath = System.IO.Path.Combine(dataPath, "Sounds", "MIDI");

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

        GD.Print($"[SND] SoundManager initialized — WAV: {_wavPath}, pool: {MaxConcurrentSounds}");
    }

    /// <summary>
    /// Play a sound effect by ID. VB6: Audio.PlayWave(soundId).
    /// Tries WAV first, then MP3 fallback.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (!_soundEnabled || soundId <= 0) return;

        GD.Print($"[SND] PlaySound({soundId})");
        var stream = LoadSoundStream(soundId);
        if (stream == null)
        {
            GD.Print($"[SND] PlaySound({soundId}) — stream is null, skipping");
            return;
        }

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
        GD.Print($"[SND] Playing sound {soundId} — stream: {stream.GetType().Name}");
    }

    /// <summary>
    /// Play music by ID. VB6: Audio.PlayMIDI(musicId).
    /// MIDI files can't be played in Godot — tries MP3 fallback with same ID.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        GD.Print($"[SND] PlayMusic({musicId})");

        if (musicId <= 0)
        {
            StopMusic();
            return;
        }

        if (musicId == _currentMusicId && _musicPlayer != null && _musicPlayer.Playing)
            return;

        _currentMusicId = musicId;
        if (!_musicEnabled) return;

        var stream = LoadMusicStream(musicId);
        if (stream == null)
        {
            GD.Print($"[SND] No music file for ID {musicId}");
            return;
        }

        if (_musicPlayer != null)
        {
            _musicPlayer.Stream = stream;
            _musicPlayer.Play();
            GD.Print($"[SND] Playing music {musicId} — stream: {stream.GetType().Name}");
        }
    }

    public void StopMusic()
    {
        _currentMusicId = 0;
        _musicPlayer?.Stop();
    }

    private AudioStream? LoadSoundStream(int soundId)
    {
        string cacheKey = $"sfx:{soundId}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var stream = TryLoadAudio(soundId, isSfx: true);

        if (stream != null)
            _cache[cacheKey] = stream;
        else
            GD.Print($"[SND] Could not load sound {soundId}");

        return stream;
    }

    private AudioStream? LoadMusicStream(int musicId)
    {
        string cacheKey = $"mus:{musicId}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Music: try MP3 first, then WAV
        var stream = TryLoadAudio(musicId, isSfx: false);

        if (stream != null)
            _cache[cacheKey] = stream;

        return stream;
    }

    /// <summary>
    /// Try loading audio by ID. For SFX: WAV first, MP3 fallback.
    /// For music: MP3 first, WAV fallback.
    /// Handles both editor (res://) and exported (filesystem) builds.
    /// </summary>
    private AudioStream? TryLoadAudio(int id, bool isSfx)
    {
        string wavRes = $"res://Data/Sounds/WAV/{id}.wav";
        string mp3Res = $"res://Data/Sounds/MP3/{id}.mp3";

        string first = isSfx ? wavRes : mp3Res;
        string second = isSfx ? mp3Res : wavRes;

        // Try via ResourceLoader (works in editor + properly exported PCKs)
        var stream = TryResourceLoad(first) ?? TryResourceLoad(second);
        if (stream != null) return stream;

        // Fallback: load from filesystem directly (exported builds without PCK)
        string firstPath = isSfx
            ? System.IO.Path.Combine(_wavPath, $"{id}.wav")
            : System.IO.Path.Combine(_mp3Path, $"{id}.mp3");
        string secondPath = isSfx
            ? System.IO.Path.Combine(_mp3Path, $"{id}.mp3")
            : System.IO.Path.Combine(_wavPath, $"{id}.wav");

        stream = TryLoadFromFile(firstPath) ?? TryLoadFromFile(secondPath);
        return stream;
    }

    private static AudioStream? TryResourceLoad(string resPath)
    {
        if (!ResourceLoader.Exists(resPath)) return null;
        try
        {
            return ResourceLoader.Load<AudioStream>(resPath);
        }
        catch (System.Exception e)
        {
            GD.Print($"[SND] ResourceLoader failed for {resPath}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Load audio directly from disk — for exported builds where files
    /// are alongside the executable instead of packed in a PCK.
    /// </summary>
    private static AudioStream? TryLoadFromFile(string absPath)
    {
        if (!Godot.FileAccess.FileExists(absPath)) return null;

        try
        {
            if (absPath.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase))
            {
                var file = Godot.FileAccess.Open(absPath, Godot.FileAccess.ModeFlags.Read);
                if (file == null) return null;
                var bytes = file.GetBuffer((long)file.GetLength());
                file.Close();
                var mp3 = new AudioStreamMP3();
                mp3.Data = bytes;
                return mp3;
            }
            else if (absPath.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
            {
                // For WAV, try loading via ResourceLoader with absolute path
                var globalized = ProjectSettings.LocalizePath(absPath);
                if (ResourceLoader.Exists(globalized))
                    return ResourceLoader.Load<AudioStream>(globalized);
            }
        }
        catch (System.Exception e)
        {
            GD.Print($"[SND] File load failed for {absPath}: {e.Message}");
        }

        return null;
    }
}

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

        var stream = LoadSoundStream(soundId);
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

    /// <summary>
    /// Play music by ID. VB6: Audio.PlayMIDI(musicId).
    /// MIDI files can't be played in Godot — tries MP3 fallback with same ID.
    /// </summary>
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
        }
    }

    public void StopMusic()
    {
        _currentMusicId = 0;
        _musicPlayer?.Stop();
    }

    private AudioStream? LoadSoundStream(int soundId)
    {
        // Try WAV via Godot resource loader (handles .import files)
        string wavRes = $"res://Data/Sounds/WAV/{soundId}.wav";
        string cacheKey = $"sfx:{soundId}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        AudioStream? stream = null;

        if (ResourceLoader.Exists(wavRes))
        {
            stream = ResourceLoader.Load<AudioStream>(wavRes);
        }

        // Fallback: MP3
        if (stream == null)
        {
            string mp3Res = $"res://Data/Sounds/MP3/{soundId}.mp3";
            if (ResourceLoader.Exists(mp3Res))
            {
                stream = ResourceLoader.Load<AudioStream>(mp3Res);
            }
        }

        if (stream != null)
        {
            _cache[cacheKey] = stream;
        }

        return stream;
    }

    private AudioStream? LoadMusicStream(int musicId)
    {
        string cacheKey = $"mus:{musicId}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        AudioStream? stream = null;

        // Try MP3 first (Godot can't play MIDI natively)
        string mp3Res = $"res://Data/Sounds/MP3/{musicId}.mp3";
        if (ResourceLoader.Exists(mp3Res))
        {
            stream = ResourceLoader.Load<AudioStream>(mp3Res);
        }

        // Try WAV fallback for music
        if (stream == null)
        {
            string wavRes = $"res://Data/Sounds/WAV/{musicId}.wav";
            if (ResourceLoader.Exists(wavRes))
            {
                stream = ResourceLoader.Load<AudioStream>(wavRes);
            }
        }

        if (stream != null)
        {
            _cache[cacheKey] = stream;
        }

        return stream;
    }
}

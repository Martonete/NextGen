using Godot;
using System;
using System.Collections.Generic;

namespace ArgentumNextgen.Game;

/// <summary>
/// Audio system replicating VB6 13.3 clsAudio.cls behavior exactly.
///
/// VB6 uses DirectSound with 30 buffers, distance-based volume attenuation,
/// stereo panning from source X position, and Doppler frequency shifting.
///
/// This implementation uses Godot AudioStreamPlayer2D for spatial SFX
/// (automatic distance attenuation + panning) and AudioStreamPlayer for
/// non-spatial sounds (UI clicks, self-actions) and music.
///
/// Key VB6 constants preserved:
///   NumSoundBuffers = 30
///   MAX_DISTANCE_TO_SOURCE = 150 tiles
///   DELTA_FQ = 75 (Doppler coefficient)
/// </summary>
public partial class SoundManager : Node
{
    // ── VB6 constants (clsAudio.cls) ─────────────────────────────────

    /// <summary>VB6: NumSoundBuffers = 30 concurrent sound slots.</summary>
    private const int NumSoundBuffers = 30;

    /// <summary>Practical hearing range in tiles (~2 screens). VB6 used 150 but
    /// linear attenuation made sounds inaudible well before that.</summary>
    private const float MaxDistanceToSource = 22f;

    /// <summary>VB6: DELTA_FQ — Doppler frequency variation coefficient.</summary>
    private const float DeltaFq = 75f;

    /// <summary>Headroom so stacked SFX don't clip the Master bus.</summary>
    private const float SfxHeadroomDb = -10.0f;

    /// <summary>Music headroom — AO MP3s are hot-mastered.</summary>
    private const float MusicHeadroomDb = -14.0f;

    /// <summary>Effectively silent in Godot dB scale.</summary>
    private const float SilentDb = -80f;

    /// <summary>
    /// Pixels per tile. AudioStreamPlayer2D works in pixel coordinates;
    /// VB6 spatial audio works in tile coordinates. We convert.
    /// </summary>
    private const float TileSize = 32f;

    /// <summary>MaxDistance in pixels for AudioStreamPlayer2D.</summary>
    private const float MaxDistancePx = MaxDistanceToSource * TileSize;

    // ── Well-known sound IDs ─────────────────────────────────────────

    public const int SND_CLICK = 1;
    public const int SND_SWING = 2;
    public const int SND_LEVEL = 5;
    public const int SND_IMPACTO = 10;
    public const int SND_DEATH = 11;
    public const int SND_REVIVE = 41;

    // ── SFX slot (mirrors VB6 SoundBuffer struct) ────────────────────

    private struct SfxSlot
    {
        public int SoundId;          // 0 = empty
        public bool Looping;
        public int SrcTileX;         // VB6: X (tile coords, 0 = non-spatial)
        public int SrcTileY;         // VB6: Y
        public float NormalPitchScale; // VB6: normalFq (base frequency ratio)
    }

    // ── Fields ────────────────────────────────────────────────────────

    // Spatial SFX players (AudioStreamPlayer2D) — 30 slots like VB6
    private readonly AudioStreamPlayer2D[] _sfxPlayers = new AudioStreamPlayer2D[NumSoundBuffers];
    private readonly SfxSlot[] _sfxSlots = new SfxSlot[NumSoundBuffers];

    // Non-spatial player for UI sounds (clicks, self-actions with srcX=srcY=0)
    private readonly List<AudioStreamPlayer> _uiPlayers = new();
    private const int NumUiPlayers = 4;

    // Music
    private AudioStreamPlayer? _musicPlayer;
    private int _currentMusicId;

    // Audio stream cache with FIFO eviction
    private const int MaxSfxCacheSize = 200;
    private readonly Dictionary<int, AudioStream?> _sfxCache = new();
    private readonly Queue<int> _sfxCacheOrder = new();
    private readonly Dictionary<int, AudioStream?> _musCache = new();

    // State
    private string _dataPath = "";
    private bool _soundEnabled = true;
    private bool _musicEnabled = true;
    private float _sfxVolumeDb = SfxHeadroomDb;
    private float _sfxVolumeLinear = 1f;

    // VB6: lastPosX, lastPosY — listener position in tile coords
    private int _listenerTileX;
    private int _listenerTileY;

    // AudioListener2D node — attached as child, positioned each frame
    private AudioListener2D? _listener;

    // ── Properties ───────────────────────────────────────────────────

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set
        {
            _soundEnabled = value;
            if (!value) StopAllSfx();
        }
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

    // ── Initialization ───────────────────────────────────────────────

    public void Init(string dataPath)
    {
        _dataPath = dataPath;

        // Create AudioListener2D so spatial sounds work relative to player
        _listener = new AudioListener2D();
        _listener.MakeCurrent();
        AddChild(_listener);

        // Create 30 spatial SFX players (VB6: DSBuffers[1..30])
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            var player = new AudioStreamPlayer2D();
            player.Bus = "Master";
            player.MaxDistance = MaxDistancePx;
            player.Attenuation = 1.0f; // Linear attenuation (VB6 uses linear)
            player.VolumeDb = _sfxVolumeDb;
            AddChild(player);
            _sfxPlayers[i] = player;
            _sfxSlots[i] = new SfxSlot();
        }

        // Non-spatial UI players (for sounds with srcX=srcY=0)
        for (int i = 0; i < NumUiPlayers; i++)
        {
            var player = new AudioStreamPlayer();
            player.Bus = "Master";
            player.VolumeDb = _sfxVolumeDb;
            AddChild(player);
            _uiPlayers.Add(player);
        }

        // Music player (non-spatial, always full volume)
        _musicPlayer = new AudioStreamPlayer();
        _musicPlayer.Bus = "Master";
        _musicPlayer.VolumeDb = MusicHeadroomDb;
        AddChild(_musicPlayer);

        var test = LoadWav(2);
        GD.Print($"[SND] Init done ({NumSoundBuffers} spatial + {NumUiPlayers} UI slots). Test sound 2: {(test != null ? "OK" : "FAIL")}");
    }

    // ── Volume ───────────────────────────────────────────────────────

    /// <summary>Set SFX volume 0-100%. VB6: SndVolume percentage mapping.</summary>
    public void SetSfxVolume(int percent)
    {
        _sfxVolumeLinear = Mathf.Clamp(percent / 100f, 0f, 1f);
        _sfxVolumeDb = percent <= 0 ? SilentDb : Mathf.LinearToDb(_sfxVolumeLinear) + SfxHeadroomDb;

        // Update all active spatial players
        for (int i = 0; i < NumSoundBuffers; i++)
            _sfxPlayers[i].VolumeDb = _sfxVolumeDb;

        // Update UI players
        foreach (var p in _uiPlayers)
            p.VolumeDb = _sfxVolumeDb;
    }

    /// <summary>Set music volume 0-100%. VB6: MusicVolume percentage.</summary>
    public void SetMusicVolume(int percent)
    {
        float db = percent <= 0 ? SilentDb : Mathf.LinearToDb(percent / 100f) + MusicHeadroomDb;
        if (_musicPlayer != null) _musicPlayer.VolumeDb = db;
    }

    // ── Listener position (VB6: MoveListener) ────────────────────────

    /// <summary>
    /// Update the listener position. Called every frame or on player movement.
    /// VB6: MoveListener(X, Y) — updates all active 3D sounds with delta.
    /// With AudioStreamPlayer2D, we move the AudioListener2D node instead.
    /// </summary>
    public void UpdateListenerPosition(int tileX, int tileY)
    {
        _listenerTileX = tileX;
        _listenerTileY = tileY;

        // Move the AudioListener2D to the player's pixel position
        if (_listener != null)
            _listener.GlobalPosition = new Vector2(tileX * TileSize, tileY * TileSize);
    }

    // ── Play SFX ─────────────────────────────────────────────────────

    /// <summary>
    /// Play a sound without spatial positioning (UI/self sounds).
    /// VB6: PlayWave with srcX=0, srcY=0 → no 3D effects.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (!_soundEnabled || soundId <= 0) return;

        var stream = GetOrLoadSfx(soundId);
        if (stream == null) return;

        // Dedup: if same sound already playing on a UI player, restart it
        foreach (var p in _uiPlayers)
        {
            if (p.Playing && p.Stream == stream)
            {
                p.Seek(0);
                return;
            }
        }

        // Find free UI player
        AudioStreamPlayer? player = null;
        foreach (var p in _uiPlayers)
        {
            if (!p.Playing) { player = p; break; }
        }
        player ??= _uiPlayers[0]; // Steal oldest

        player.VolumeDb = _sfxVolumeDb;
        player.Stream = stream;
        player.Play();
    }

    /// <summary>
    /// Play a named sound file (e.g. "click.wav").
    /// Used for UI button clicks.
    /// </summary>
    public void PlayNamedSound(string fileName)
    {
        if (!_soundEnabled || string.IsNullOrEmpty(fileName)) return;

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
            CacheStream(cacheKey, stream);
        }

        if (stream == null) return;

        AudioStreamPlayer? player = null;
        foreach (var p in _uiPlayers)
        {
            if (!p.Playing) { player = p; break; }
        }
        player ??= _uiPlayers[0];

        player.VolumeDb = _sfxVolumeDb;
        player.Stream = stream;
        player.Play();
    }

    /// <summary>
    /// Play a sound at a spatial position (tile coordinates).
    /// VB6: PlayWave(file, srcX, srcY) → Update3DSound for volume + pan + Doppler.
    ///
    /// srcX/srcY=0 means non-spatial (plays via PlaySound instead).
    /// AudioStreamPlayer2D handles distance attenuation and stereo panning
    /// automatically based on position relative to AudioListener2D.
    /// </summary>
    public void PlaySoundAt(int soundId, int srcTileX, int srcTileY, int listenerX, int listenerY)
    {
        // VB6: srcX=0 and srcY=0 → no 3D, play at full volume
        if (srcTileX == 0 && srcTileY == 0)
        {
            PlaySound(soundId);
            return;
        }

        if (!_soundEnabled || soundId <= 0) return;

        // Distance check (VB6: MAX_DISTANCE_TO_SOURCE)
        float dx = srcTileX - listenerX;
        float dy = srcTileY - listenerY;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        if (distance > MaxDistanceToSource) return;

        var stream = GetOrLoadSfx(soundId);
        if (stream == null) return;

        // Update listener position if changed
        if (listenerX != _listenerTileX || listenerY != _listenerTileY)
            UpdateListenerPosition(listenerX, listenerY);

        int slotIdx = AcquireSpatialSlot(soundId, false);

        var player = _sfxPlayers[slotIdx];
        player.Stream = stream;
        player.VolumeDb = _sfxVolumeDb;
        player.PitchScale = 1.0f;

        // Position the player at the source in pixel coordinates
        player.GlobalPosition = new Vector2(srcTileX * TileSize, srcTileY * TileSize);

        player.Play();

        _sfxSlots[slotIdx] = new SfxSlot
        {
            SoundId = soundId,
            Looping = false,
            SrcTileX = srcTileX,
            SrcTileY = srcTileY,
            NormalPitchScale = 1.0f
        };
    }

    // ── Stop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stop all SFX. VB6: StopWave(0) — stops all 30 buffers.
    /// Called on map change / warp to prevent stale sounds.
    /// </summary>
    public void StopAllSfx()
    {
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            if (_sfxPlayers[i].Playing) _sfxPlayers[i].Stop();
            _sfxSlots[i].SoundId = 0;
        }
        foreach (var p in _uiPlayers)
        {
            if (p.Playing) p.Stop();
        }
    }

    // ── Music ────────────────────────────────────────────────────────

    /// <summary>
    /// Play music by ID. VB6: PlayMIDI / MusicMP3Play.
    /// Tries MP3 first (Godot can't play MIDI), then WAV fallback.
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

        if (!_musCache.TryGetValue(musicId, out var stream))
        {
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

    /// <summary>VB6: StopMidi / MusicMP3Stop.</summary>
    public void StopMusic()
    {
        _currentMusicId = 0;
        _musicPlayer?.Stop();
    }

    // ── Slot acquisition (VB6: LoadWave buffer selection logic) ──────

    /// <summary>
    /// Find and acquire a spatial SFX slot, following VB6 priority exactly:
    /// 1. Same soundId already loaded and not playing → reuse
    /// 2. Empty slot (SoundId == 0)
    /// 3. Slot with finished (not playing) sound
    /// 4. First non-looping playing slot (steal it)
    /// 5. If all looping → steal slot 0
    /// </summary>
    private int AcquireSpatialSlot(int soundId, bool looping)
    {
        // 1. Dedup: same sound loaded and stopped → reuse
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            if (_sfxSlots[i].SoundId == soundId && !_sfxPlayers[i].Playing)
                return i;
        }

        // Also check: same sound already playing → restart (dedup)
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            if (_sfxSlots[i].SoundId == soundId && _sfxPlayers[i].Playing)
            {
                _sfxPlayers[i].Seek(0);
                return i;
            }
        }

        // 2. Empty slot
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            if (_sfxSlots[i].SoundId == 0) return i;
        }

        // 3. Stopped (finished playing)
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            if (!_sfxPlayers[i].Playing) return i;
        }

        // 4. Non-looping slot (steal it)
        for (int i = 0; i < NumSoundBuffers; i++)
        {
            if (!_sfxSlots[i].Looping)
            {
                _sfxPlayers[i].Stop();
                return i;
            }
        }

        // 5. All looping — steal slot 0 (VB6: i = 1, our 0-based)
        if (!looping) return -1; // VB6: ignore non-looping if all slots are looping
        _sfxPlayers[0].Stop();
        return 0;
    }

    // ── Audio stream cache ───────────────────────────────────────────

    private AudioStream? GetOrLoadSfx(int soundId)
    {
        if (_sfxCache.TryGetValue(soundId, out var stream))
            return stream;

        stream = LoadWav(soundId) ?? LoadWavAsMp3(soundId) ?? LoadMp3(soundId);
        CacheStream(soundId, stream);

        if (stream == null)
            GD.Print($"[SND] Could not load sound {soundId}");

        return stream;
    }

    private void CacheStream(int key, AudioStream? stream)
    {
        _sfxCache[key] = stream;
        _sfxCacheOrder.Enqueue(key);

        while (_sfxCacheOrder.Count > MaxSfxCacheSize)
        {
            int oldest = _sfxCacheOrder.Dequeue();
            _sfxCache.Remove(oldest);
        }
    }

    // ── File loaders ─────────────────────────────────────────────────

    private AudioStream? LoadWav(int id)
    {
        string filePath = System.IO.Path.Combine(_dataPath, "Sounds", "WAV", $"{id}.wav");
        if (!System.IO.File.Exists(filePath)) return null;

        try
        {
            byte[] raw = System.IO.File.ReadAllBytes(filePath);
            return ParseWav(raw);
        }
        catch (Exception e)
        {
            GD.Print($"[SND] WAV parse failed for {id}: {e.Message}");
            return null;
        }
    }

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
        catch (Exception e)
        {
            GD.Print($"[SND] MP3 load failed for {id}: {e.Message}");
            return null;
        }
    }

    private AudioStream? LoadWavAsMp3(int id)
    {
        string filePath = System.IO.Path.Combine(_dataPath, "Sounds", "WAV", $"{id}.wav");
        if (!System.IO.File.Exists(filePath)) return null;

        try
        {
            byte[] raw = System.IO.File.ReadAllBytes(filePath);
            if (raw.Length > 20 && raw[0] == 'R' && raw[1] == 'I')
            {
                int fmt = FindFmtFormat(raw);
                if (fmt == 0x55 || fmt == 0x50)
                {
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

    // ── WAV parser ───────────────────────────────────────────────────

    /// <summary>
    /// Parse PCM WAV (8/16 bit, mono/stereo) from raw bytes.
    /// Non-PCM (ADPCM, MP3-in-WAV codec 0x55) returns null.
    /// </summary>
    private static AudioStreamWav? ParseWav(byte[] raw)
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

        if (audioFormat != 1) return null; // PCM only
        if (pcmData == null || pcmData.Length == 0) return null;

        // WAV 8-bit uses unsigned (0-255, center=128); Godot expects signed (-128..127)
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

    private static int FindFmtFormat(byte[] raw)
    {
        int pos = 12;
        while (pos + 8 <= raw.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int size = BitConverter.ToInt32(raw, pos + 4);
            if (size < 0) break;
            if (id == "fmt " && size >= 2)
                return BitConverter.ToInt16(raw, pos + 8);
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
            int size = BitConverter.ToInt32(raw, pos + 4);
            if (size < 0) break;
            if (id == "data")
            {
                int dataLen = Math.Min(size, raw.Length - pos - 8);
                if (dataLen <= 0) return null;
                byte[] data = new byte[dataLen];
                Array.Copy(raw, pos + 8, data, 0, dataLen);
                return data;
            }
            pos = pos + 8 + size;
            if (pos % 2 != 0) pos++;
        }
        return null;
    }
}

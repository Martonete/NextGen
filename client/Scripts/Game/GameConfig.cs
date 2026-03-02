using System;
using System.IO;
using System.Text;
using Godot;

namespace TierrasSagradasAO.Game;

/// <summary>
/// User configuration — persisted to Data/INIT/Options.tsao (INI format).
/// Mirrors VB6 User_Config from frmOpcionesNew (46 fields).
/// A temporary copy is used while the options dialog is open to support Cancel.
/// </summary>
public class GameConfig
{
    // ── Audio ──────────────────────────────────────────────
    public bool MusicEnabled = true;        // VB6: Sonido_Musica
    public bool SfxEnabled = true;          // VB6: Sonido_Fx
    public int MusicVolume = 70;            // 0-100 (mapped to dB in SoundManager)
    public int SfxVolume = 100;             // 0-100

    // ── Rendering ─────────────────────────────────────────
    public bool ShowAuras = true;           // VB6: Video_Toggle_Aura
    public bool ShowParticles = true;       // VB6: Video_Toggle_Particulas
    public bool ShowShadows = true;         // VB6: Video_Toggle_Sombras
    public bool ShowNpcShadows = true;      // VB6: Video_Toggle_Sombras_NPC
    public bool ShowReflections = true;     // VB6: Video_Toggle_Reflejos
    public bool ShowDayNight = true;        // VB6: Alpha_Use_Dia_Noche
    public bool ShowNames = true;           // VB6: General_Show_Nicks
    public bool ShowLights = true;          // (not in VB6, but useful toggle)

    // ── Transparency ──────────────────────────────────────
    public bool UiTransparency;             // VB6: Alpha_Interfaz_Activar
    public int UiTransparencyLevel = 180;   // VB6: Alpha_Interfaz_Transparencia (40-255)
    public bool TreeRoofTransparency = true; // VB6: Alpha_Usar_Transparencias_Objetos
    public bool DeadCharTransparency = true; // VB6: Alpha_Usar_Transparencias_PJs

    // ── Performance ───────────────────────────────────────
    public int PerformanceLevel = 2;        // VB6: Performance_Level (0=Min,1=Low,2=Med,3=High,4=Max)
    public int FpsLimit = 65;               // VB6: General_Limit_FPS (0=unlimited, 18, 32, 65)

    // ── Interface ─────────────────────────────────────────
    public bool ShowMinimap = true;         // VB6: MiniMap_Activate
    public bool ShowMinimapPosition = true; // VB6: MiniMap_Show_Position
    public bool ShowDeathDialog = true;     // VB6: General_Mostrar_Cartel_Muerte
    public bool ReplaceEmoticons = true;    // VB6: General_Emoticons_Reeplace
    public int ScreenshotFormat;            // VB6: 0=JPG, 1=BMP

    // ── Console/Chat ──────────────────────────────────────
    public bool ShowGlobalChat = true;      // VB6: !Consola_Globales_DeActivate
    public bool ShowPrivateChat = true;     // VB6: !Consola_Privados_DeActivate
    public bool ShowBuffTimers = true;      // VB6: Consola_Show_Counters
    public bool ContactSignIn = true;       // VB6: Chat_Contact_SignsIn
    public bool ContactSignOut = true;      // VB6: Chat_Contact_SignsOut
    public bool ChatSoundAlert;             // VB6: Chat_Use_Sound_Alert

    // ── Mouse/Input ───────────────────────────────────────
    public bool MouseDoubleClick = true;    // VB6: MouseActions_DClick
    public bool MouseRightClick;            // VB6: MouseActions_RClick
    public bool MouseContextMenu = true;    // VB6: MouseActions_Activate

    /// <summary>
    /// Create a deep copy for temporary editing (Cancel support).
    /// </summary>
    public GameConfig Clone()
    {
        return (GameConfig)MemberwiseClone();
    }

    /// <summary>
    /// Copy all values from another config (apply temp → permanent).
    /// </summary>
    public void CopyFrom(GameConfig other)
    {
        MusicEnabled = other.MusicEnabled;
        SfxEnabled = other.SfxEnabled;
        MusicVolume = other.MusicVolume;
        SfxVolume = other.SfxVolume;

        ShowAuras = other.ShowAuras;
        ShowParticles = other.ShowParticles;
        ShowShadows = other.ShowShadows;
        ShowNpcShadows = other.ShowNpcShadows;
        ShowReflections = other.ShowReflections;
        ShowDayNight = other.ShowDayNight;
        ShowNames = other.ShowNames;
        ShowLights = other.ShowLights;

        UiTransparency = other.UiTransparency;
        UiTransparencyLevel = other.UiTransparencyLevel;
        TreeRoofTransparency = other.TreeRoofTransparency;
        DeadCharTransparency = other.DeadCharTransparency;

        PerformanceLevel = other.PerformanceLevel;
        FpsLimit = other.FpsLimit;

        ShowMinimap = other.ShowMinimap;
        ShowMinimapPosition = other.ShowMinimapPosition;
        ShowDeathDialog = other.ShowDeathDialog;
        ReplaceEmoticons = other.ReplaceEmoticons;
        ScreenshotFormat = other.ScreenshotFormat;

        ShowGlobalChat = other.ShowGlobalChat;
        ShowPrivateChat = other.ShowPrivateChat;
        ShowBuffTimers = other.ShowBuffTimers;
        ContactSignIn = other.ContactSignIn;
        ContactSignOut = other.ContactSignOut;
        ChatSoundAlert = other.ChatSoundAlert;

        MouseDoubleClick = other.MouseDoubleClick;
        MouseRightClick = other.MouseRightClick;
        MouseContextMenu = other.MouseContextMenu;
    }

    // ── Persistence ───────────────────────────────────────

    private static string FilePath(string dataPath) =>
        Path.Combine(dataPath, "INIT", "Options.tsao");

    /// <summary>
    /// Load configuration from INI file. Missing keys use defaults.
    /// </summary>
    public static GameConfig Load(string dataPath)
    {
        var cfg = new GameConfig();
        string path = FilePath(dataPath);

        if (!File.Exists(path))
        {
            GD.Print("[CFG] No Options.tsao found, using defaults.");
            return cfg;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            bool inSection = false;

            foreach (var line in lines)
            {
                string t = line.Trim();
                if (t.Equals("[Options]", StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (t.StartsWith("[")) { inSection = false; continue; }
                if (!inSection || t.Length == 0 || t[0] == ';') continue;

                int eq = t.IndexOf('=');
                if (eq < 0) continue;
                string key = t[..eq].Trim();
                string val = t[(eq + 1)..].Trim();

                switch (key)
                {
                    // Audio
                    case "MusicEnabled": cfg.MusicEnabled = val == "1"; break;
                    case "SfxEnabled": cfg.SfxEnabled = val == "1"; break;
                    case "MusicVolume": if (int.TryParse(val, out int mv)) cfg.MusicVolume = Math.Clamp(mv, 0, 100); break;
                    case "SfxVolume": if (int.TryParse(val, out int sv)) cfg.SfxVolume = Math.Clamp(sv, 0, 100); break;

                    // Rendering
                    case "ShowAuras": cfg.ShowAuras = val == "1"; break;
                    case "ShowParticles": cfg.ShowParticles = val == "1"; break;
                    case "ShowShadows": cfg.ShowShadows = val == "1"; break;
                    case "ShowNpcShadows": cfg.ShowNpcShadows = val == "1"; break;
                    case "ShowReflections": cfg.ShowReflections = val == "1"; break;
                    case "ShowDayNight": cfg.ShowDayNight = val == "1"; break;
                    case "ShowNames": cfg.ShowNames = val == "1"; break;
                    case "ShowLights": cfg.ShowLights = val == "1"; break;

                    // Transparency
                    case "UiTransparency": cfg.UiTransparency = val == "1"; break;
                    case "UiTransparencyLevel": if (int.TryParse(val, out int utl)) cfg.UiTransparencyLevel = Math.Clamp(utl, 40, 255); break;
                    case "TreeRoofTransparency": cfg.TreeRoofTransparency = val == "1"; break;
                    case "DeadCharTransparency": cfg.DeadCharTransparency = val == "1"; break;

                    // Performance
                    case "PerformanceLevel": if (int.TryParse(val, out int pl)) cfg.PerformanceLevel = Math.Clamp(pl, 0, 4); break;
                    case "FpsLimit": if (int.TryParse(val, out int fps)) cfg.FpsLimit = fps; break;

                    // Interface
                    case "ShowMinimap": cfg.ShowMinimap = val == "1"; break;
                    case "ShowMinimapPosition": cfg.ShowMinimapPosition = val == "1"; break;
                    case "ShowDeathDialog": cfg.ShowDeathDialog = val == "1"; break;
                    case "ReplaceEmoticons": cfg.ReplaceEmoticons = val == "1"; break;
                    case "ScreenshotFormat": if (int.TryParse(val, out int ssf)) cfg.ScreenshotFormat = ssf; break;

                    // Console/Chat
                    case "ShowGlobalChat": cfg.ShowGlobalChat = val == "1"; break;
                    case "ShowPrivateChat": cfg.ShowPrivateChat = val == "1"; break;
                    case "ShowBuffTimers": cfg.ShowBuffTimers = val == "1"; break;
                    case "ContactSignIn": cfg.ContactSignIn = val == "1"; break;
                    case "ContactSignOut": cfg.ContactSignOut = val == "1"; break;
                    case "ChatSoundAlert": cfg.ChatSoundAlert = val == "1"; break;

                    // Mouse/Input
                    case "MouseDoubleClick": cfg.MouseDoubleClick = val == "1"; break;
                    case "MouseRightClick": cfg.MouseRightClick = val == "1"; break;
                    case "MouseContextMenu": cfg.MouseContextMenu = val == "1"; break;
                }
            }

            GD.Print($"[CFG] Loaded options from {path}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CFG] Failed to load options: {ex.Message}");
        }

        return cfg;
    }

    /// <summary>
    /// Save configuration to INI file.
    /// </summary>
    public void Save(string dataPath)
    {
        string path = FilePath(dataPath);

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("[Options]");

            // Audio
            sb.AppendLine($"MusicEnabled={(MusicEnabled ? "1" : "0")}");
            sb.AppendLine($"SfxEnabled={(SfxEnabled ? "1" : "0")}");
            sb.AppendLine($"MusicVolume={MusicVolume}");
            sb.AppendLine($"SfxVolume={SfxVolume}");

            // Rendering
            sb.AppendLine($"ShowAuras={(ShowAuras ? "1" : "0")}");
            sb.AppendLine($"ShowParticles={(ShowParticles ? "1" : "0")}");
            sb.AppendLine($"ShowShadows={(ShowShadows ? "1" : "0")}");
            sb.AppendLine($"ShowNpcShadows={(ShowNpcShadows ? "1" : "0")}");
            sb.AppendLine($"ShowReflections={(ShowReflections ? "1" : "0")}");
            sb.AppendLine($"ShowDayNight={(ShowDayNight ? "1" : "0")}");
            sb.AppendLine($"ShowNames={(ShowNames ? "1" : "0")}");
            sb.AppendLine($"ShowLights={(ShowLights ? "1" : "0")}");

            // Transparency
            sb.AppendLine($"UiTransparency={(UiTransparency ? "1" : "0")}");
            sb.AppendLine($"UiTransparencyLevel={UiTransparencyLevel}");
            sb.AppendLine($"TreeRoofTransparency={(TreeRoofTransparency ? "1" : "0")}");
            sb.AppendLine($"DeadCharTransparency={(DeadCharTransparency ? "1" : "0")}");

            // Performance
            sb.AppendLine($"PerformanceLevel={PerformanceLevel}");
            sb.AppendLine($"FpsLimit={FpsLimit}");

            // Interface
            sb.AppendLine($"ShowMinimap={(ShowMinimap ? "1" : "0")}");
            sb.AppendLine($"ShowMinimapPosition={(ShowMinimapPosition ? "1" : "0")}");
            sb.AppendLine($"ShowDeathDialog={(ShowDeathDialog ? "1" : "0")}");
            sb.AppendLine($"ReplaceEmoticons={(ReplaceEmoticons ? "1" : "0")}");
            sb.AppendLine($"ScreenshotFormat={ScreenshotFormat}");

            // Console/Chat
            sb.AppendLine($"ShowGlobalChat={(ShowGlobalChat ? "1" : "0")}");
            sb.AppendLine($"ShowPrivateChat={(ShowPrivateChat ? "1" : "0")}");
            sb.AppendLine($"ShowBuffTimers={(ShowBuffTimers ? "1" : "0")}");
            sb.AppendLine($"ContactSignIn={(ContactSignIn ? "1" : "0")}");
            sb.AppendLine($"ContactSignOut={(ContactSignOut ? "1" : "0")}");
            sb.AppendLine($"ChatSoundAlert={(ChatSoundAlert ? "1" : "0")}");

            // Mouse/Input
            sb.AppendLine($"MouseDoubleClick={(MouseDoubleClick ? "1" : "0")}");
            sb.AppendLine($"MouseRightClick={(MouseRightClick ? "1" : "0")}");
            sb.AppendLine($"MouseContextMenu={(MouseContextMenu ? "1" : "0")}");

            File.WriteAllText(path, sb.ToString());
            GD.Print($"[CFG] Saved options to {path}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CFG] Failed to save options: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply a performance preset — VB6 auto-configures toggles based on level.
    /// 0=Minimo, 1=Bajo, 2=Medio, 3=Alto, 4=Maximo
    /// </summary>
    public void ApplyPerformancePreset(int level)
    {
        PerformanceLevel = Math.Clamp(level, 0, 4);

        switch (level)
        {
            case 0: // Minimo
                ShowAuras = false;
                ShowParticles = false;
                ShowShadows = false;
                ShowNpcShadows = false;
                ShowReflections = false;
                ShowDayNight = false;
                ShowLights = false;
                TreeRoofTransparency = false;
                DeadCharTransparency = false;
                break;
            case 1: // Bajo
                ShowAuras = false;
                ShowParticles = false;
                ShowShadows = true;
                ShowNpcShadows = false;
                ShowReflections = false;
                ShowDayNight = false;
                ShowLights = true;
                TreeRoofTransparency = true;
                DeadCharTransparency = false;
                break;
            case 2: // Medio (default)
                ShowAuras = true;
                ShowParticles = true;
                ShowShadows = true;
                ShowNpcShadows = false;
                ShowReflections = false;
                ShowDayNight = true;
                ShowLights = true;
                TreeRoofTransparency = true;
                DeadCharTransparency = true;
                break;
            case 3: // Alto
                ShowAuras = true;
                ShowParticles = true;
                ShowShadows = true;
                ShowNpcShadows = true;
                ShowReflections = true;
                ShowDayNight = true;
                ShowLights = true;
                TreeRoofTransparency = true;
                DeadCharTransparency = true;
                break;
            case 4: // Maximo
                ShowAuras = true;
                ShowParticles = true;
                ShowShadows = true;
                ShowNpcShadows = true;
                ShowReflections = true;
                ShowDayNight = true;
                ShowLights = true;
                TreeRoofTransparency = true;
                DeadCharTransparency = true;
                break;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Bindable game action — matches VB6 BindKeys indices (1-22).
/// </summary>
public enum GameAction
{
    Attack = 0,       // VB6 idx 1: Atacar (Ctrl)
    PickUp = 1,       // VB6 idx 2: Tomar objeto (A → G in WASD)
    Drop = 2,         // VB6 idx 3: Tirar objeto (T)
    UseItem = 3,      // VB6 idx 4: Usar objeto (U)
    EquipItem = 4,    // VB6 idx 5: Equipar objeto (E → Q in WASD)
    SafetyToggle = 5, // VB6 idx 6: Seguro PvP (S → F7 in WASD)
    ShowNames = 6,    // VB6 idx 7: Mostrar/Ocultar Nombres (N)
    ResSafety = 7,    // VB6 idx 8: Seguro Resurrección (D → F8 in WASD)
    Steal = 8,        // VB6 idx 9: Robar (R)
    RefreshPos = 9,   // VB6 idx 10: Actualizar posición (L)
    Hide = 10,        // VB6 idx 11: Ocultarse (O)
    CombatMode = 11,  // VB6 idx 12: Modo combate (C)
    Screenshot = 12,  // VB6 idx 13: Screenshot (F)
    MoveUp = 13,      // VB6 idx 14: Arriba (↑ / W)
    MoveDown = 14,    // VB6 idx 15: Abajo (↓ / S)
    MoveLeft = 15,    // VB6 idx 16: Izquierda (← / A)
    MoveRight = 16,   // VB6 idx 17: Derecha (→ / D)
    ToggleMusic = 18, // VB6 idx 19: Música (M → P in WASD)
    ShowMap = 19,     // VB6 idx 20: Mapa (currently unused)
    ItemSafety = 20,  // Unused — item drop safety removed
    Meditate = 21,    // VB6 idx 22: Meditar (F6)
}

/// <summary>
/// A single key binding: Godot Key + display name.
/// </summary>
public struct KeyBind
{
    public Key KeyCode;
    public string Name;

    public KeyBind(Key key, string name)
    {
        KeyCode = key;
        Name = name;
    }
}

/// <summary>
/// Manages all key bindings — 22 configurable actions.
/// Persisted to Data/INIT/Teclas.ao (INI format matching VB6).
/// </summary>
public class KeyBindings
{
    public const int ActionCount = 22;

    /// <summary>Current bindings (indexed by GameAction).</summary>
    public KeyBind[] Binds = new KeyBind[ActionCount];

    /// <summary>Display labels for each action (Spanish).</summary>
    public static readonly string[] ActionLabels =
    {
        "Atacar",                         // 0
        "Tomar objeto",                   // 1
        "Tirar objeto",                   // 2
        "Usar objeto",                    // 3
        "Equipar objeto",                 // 4
        "Seguro PvP",                     // 5
        "Mostrar/Ocultar nombres",        // 6
        "Seguro de resurrección",         // 7
        "Robar",                          // 8
        "Actualizar posición",            // 9
        "Ocultarse",                      // 10
        "Modo combate",                   // 11
        "Captura de pantalla",            // 12
        "Mover arriba",                   // 13
        "Mover abajo",                    // 14
        "Mover izquierda",               // 15
        "Mover derecha",                  // 16
        "(Libre)",                        // 17 — was Emoticons, removed
        "Música on/off",                  // 18
        "Mostrar mapa",                   // 19
        "(Libre)",                        // 20 — was Seguro de items, removed
        "Meditar",                        // 21
    };

    /// <summary>
    /// Reserved keys that cannot be bound (F1-F12 for macros, Numpad 0-8 for chat modes, etc.)
    /// </summary>
    private static readonly HashSet<Key> ReservedKeys = new()
    {
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5,
        Key.F9, Key.F10, Key.F11, Key.F12,
        Key.Enter, Key.KpEnter, Key.Escape, Key.Tab,
        Key.Kp0, Key.Kp1, Key.Kp2, Key.Kp3, Key.Kp4,
        Key.Kp5, Key.Kp6, Key.Kp7, Key.Kp8,
    };

    public KeyBindings()
    {
        SetDefaults();
    }

    /// <summary>
    /// VB6-accurate defaults (from clsCustomKeys.cls LoadDefaults).
    /// Arrow keys for movement, letter keys match original AO bindings.
    /// </summary>
    public void SetDefaults()
    {
        Binds[(int)GameAction.Attack]      = new KeyBind(Key.Ctrl, "Control");      // VB6: mKeyAttack = vbKeyControl
        Binds[(int)GameAction.PickUp]      = new KeyBind(Key.A, "A");               // VB6: mKeyGetObject = vbKeyA
        Binds[(int)GameAction.Drop]        = new KeyBind(Key.T, "T");               // VB6: mKeyDropObject = vbKeyT
        Binds[(int)GameAction.UseItem]     = new KeyBind(Key.U, "U");               // VB6: mKeyUseObject = vbKeyU
        Binds[(int)GameAction.EquipItem]   = new KeyBind(Key.E, "E");               // VB6: mKeyEquipObject = vbKeyE
        Binds[(int)GameAction.SafetyToggle]= new KeyBind(Key.S, "S");               // VB6: mKeySeg = vbKeyS — Seguro PvP
        Binds[(int)GameAction.ShowNames]   = new KeyBind(Key.N, "N");               // VB6: mKeyToggleNames = vbKeyN
        Binds[(int)GameAction.ResSafety]   = new KeyBind(Key.Minus, "-");           // Seguro Resurrección
        Binds[(int)GameAction.Steal]       = new KeyBind(Key.R, "R");               // VB6: mKeySteal = vbKeyR
        Binds[(int)GameAction.RefreshPos]  = new KeyBind(Key.L, "L");               // VB6: mKeyRequestRefresh = vbKeyL
        Binds[(int)GameAction.Hide]        = new KeyBind(Key.O, "O");               // VB6: mKeyHide = vbKeyO
        Binds[(int)GameAction.CombatMode]  = new KeyBind(Key.C, "C");               // (no VB6 equivalent, kept)
        Binds[(int)GameAction.Screenshot]  = new KeyBind(Key.F, "F");               // VB6: mKeyFoto = vbKeyF
        Binds[(int)GameAction.MoveUp]      = new KeyBind(Key.Up, "Arriba");         // VB6: mKeyUp = vbKeyUp
        Binds[(int)GameAction.MoveDown]    = new KeyBind(Key.Down, "Abajo");        // VB6: mKeyDown = vbKeyDown
        Binds[(int)GameAction.MoveLeft]    = new KeyBind(Key.Left, "Izquierda");    // VB6: mKeyLeft = vbKeyLeft
        Binds[(int)GameAction.MoveRight]   = new KeyBind(Key.Right, "Derecha");     // VB6: mKeyRight = vbKeyRight
        Binds[(int)GameAction.ToggleMusic] = new KeyBind(Key.M, "M");               // VB6: mKeyToggleMusic = vbKeyM
        Binds[(int)GameAction.ShowMap]     = new KeyBind(Key.F7, "F7");              // VB6: mKeyCastSpellMacro = vbKeyF7
        Binds[(int)GameAction.ItemSafety]  = new KeyBind(Key.None, "");               // Unused — item safety removed
        Binds[(int)GameAction.Meditate]    = new KeyBind(Key.F6, "F6");              // VB6: mKeyMeditate = vbKeyF6
    }

    /// <summary>
    /// Check if a key is already bound to another action.
    /// Returns the conflicting GameAction index, or -1 if free.
    /// </summary>
    public int FindConflict(Key key, int excludeIndex)
    {
        for (int i = 0; i < ActionCount; i++)
        {
            if (i == excludeIndex) continue;
            if (Binds[i].KeyCode == key) return i;
        }
        return -1;
    }

    /// <summary>
    /// Check if a key is reserved (cannot be bound).
    /// </summary>
    public static bool IsReserved(Key key) => ReservedKeys.Contains(key);

    /// <summary>
    /// Check if a specific action's key is currently pressed.
    /// </summary>
    public bool IsActionPressed(GameAction action) =>
        Input.IsKeyPressed(Binds[(int)action].KeyCode);

    /// <summary>
    /// Get the key bound to an action.
    /// </summary>
    public Key GetKey(GameAction action) => Binds[(int)action].KeyCode;

    /// <summary>
    /// Create a deep copy for temporary editing.
    /// </summary>
    public KeyBindings Clone()
    {
        var copy = new KeyBindings();
        for (int i = 0; i < ActionCount; i++)
            copy.Binds[i] = new KeyBind(Binds[i].KeyCode, Binds[i].Name);
        return copy;
    }

    /// <summary>
    /// Copy all bindings from another instance.
    /// </summary>
    public void CopyFrom(KeyBindings other)
    {
        for (int i = 0; i < ActionCount; i++)
            Binds[i] = new KeyBind(other.Binds[i].KeyCode, other.Binds[i].Name);
    }

    // ── Persistence ───────────────────────────────────────

    private static string FilePath(string dataPath) =>
        Path.Combine(dataPath, "INIT", "Teclas.ao");

    /// <summary>
    /// Convert a Godot Key to a display name.
    /// </summary>
    public static string KeyToName(Key key)
    {
        return key switch
        {
            Key.Ctrl => "Control",
            Key.Shift => "Shift",
            Key.Alt => "Alt",
            Key.Space => "Espacio",
            Key.Up => "Flecha Arriba",
            Key.Down => "Flecha Abajo",
            Key.Left => "Flecha Izquierda",
            Key.Right => "Flecha Derecha",
            Key.Enter => "Enter",
            Key.KpEnter => "Numpad Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Backspace => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Inicio",
            Key.End => "Fin",
            Key.Pageup => "Page Up",
            Key.Pagedown => "Page Down",
            Key.KpMultiply => "Numpad *",
            Key.KpDivide => "Numpad /",
            Key.KpAdd => "Numpad +",
            Key.KpSubtract => "Numpad -",
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            _ => key.ToString().Length == 1 ? key.ToString() : key.ToString()
        };
    }

    /// <summary>
    /// Load key bindings from Teclas.ao. Missing entries use defaults.
    /// Format: [TECLAS] section, each line: index=GodotKeyInt,DisplayName
    /// </summary>
    public static KeyBindings Load(string dataPath)
    {
        var kb = new KeyBindings(); // starts with defaults
        string path = FilePath(dataPath);

        if (!File.Exists(path))
        {
            GD.Print("[KEYS] No Teclas.ao found, using defaults.");
            return kb;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            bool inSection = false;

            foreach (var line in lines)
            {
                string t = line.Trim();
                if (t.Equals("[TECLAS]", StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (t.StartsWith("[")) { inSection = false; continue; }
                if (!inSection || t.Length == 0 || t[0] == ';') continue;

                int eq = t.IndexOf('=');
                if (eq < 0) continue;
                string idxStr = t[..eq].Trim();
                string val = t[(eq + 1)..].Trim();

                if (!int.TryParse(idxStr, out int idx) || idx < 0 || idx >= ActionCount)
                    continue;

                int comma = val.IndexOf(',');
                if (comma < 0) continue;

                string keyStr = val[..comma].Trim();
                string name = val[(comma + 1)..].Trim();

                if (int.TryParse(keyStr, out int keyInt))
                {
                    kb.Binds[idx] = new KeyBind((Key)keyInt, name);
                }
            }

            GD.Print($"[KEYS] Loaded key bindings from {path}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[KEYS] Failed to load key bindings: {ex.Message}");
        }

        return kb;
    }

    /// <summary>
    /// Save key bindings to Teclas.ao.
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

            // Write defaults section (read-only reference)
            sb.AppendLine("[DEFAULTS]");
            var defaults = new KeyBindings();
            for (int i = 0; i < ActionCount; i++)
                sb.AppendLine($"{i}={(int)defaults.Binds[i].KeyCode},{defaults.Binds[i].Name}");

            sb.AppendLine();

            // Write current bindings
            sb.AppendLine("[TECLAS]");
            for (int i = 0; i < ActionCount; i++)
                sb.AppendLine($"{i}={(int)Binds[i].KeyCode},{Binds[i].Name}");

            File.WriteAllText(path, sb.ToString());
            GD.Print($"[KEYS] Saved key bindings to {path}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[KEYS] Failed to save key bindings: {ex.Message}");
        }
    }
}

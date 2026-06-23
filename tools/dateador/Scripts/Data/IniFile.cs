#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AODateador.Data;

/// <summary>
/// VB6-compatible INI file parser/writer.
/// Handles ';' and '\'' comments, case-insensitive keys, Latin-1/UTF-8 encoding.
/// </summary>
public class IniFile
{
    // Ordered list of sections to preserve file order on save
    private readonly List<string> _sectionOrder = new();
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> SectionNames => _sectionOrder;

    public string? Get(string section, string key)
    {
        if (_sections.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var val))
            return val;
        return null;
    }

    public int GetInt(string section, string key, int def = 0)
    {
        var v = Get(section, key);
        return v != null && int.TryParse(v, out int r) ? r : def;
    }

    public long GetLong(string section, string key, long def = 0)
    {
        var v = Get(section, key);
        return v != null && long.TryParse(v, out long r) ? r : def;
    }

    public float GetFloat(string section, string key, float def = 0f)
    {
        var v = Get(section, key);
        return v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : def;
    }

    public bool GetBool(string section, string key, bool def = false)
    {
        var v = Get(section, key);
        if (v == null) return def;
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("si", StringComparison.OrdinalIgnoreCase);
    }

    public string GetString(string section, string key, string def = "")
        => Get(section, key) ?? def;

    public void Set(string section, string key, string value)
    {
        if (!_sections.TryGetValue(section, out var sec))
        {
            sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[section] = sec;
            _sectionOrder.Add(section);
        }
        sec[key] = value;
    }

    public void Set(string section, string key, int value) => Set(section, key, value.ToString());
    public void Set(string section, string key, long value) => Set(section, key, value.ToString());
    public void Set(string section, string key, bool value) => Set(section, key, value ? "1" : "0");
    public void Set(string section, string key, float value)
        => Set(section, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public IEnumerable<string> Keys(string section)
    {
        if (_sections.TryGetValue(section, out var sec))
            return sec.Keys;
        return Array.Empty<string>();
    }

    public bool HasSection(string section) => _sections.ContainsKey(section);

    public void RemoveSection(string section)
    {
        _sections.Remove(section);
        _sectionOrder.Remove(section);
    }

    // ── Load ──

    public static IniFile Load(string path)
    {
        var ini = new IniFile();
        if (!File.Exists(path)) return ini;

        // Try UTF-8 first, fallback to Latin-1
        string content;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            content = Encoding.Unicode.GetString(bytes); // UTF-16 LE
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            content = Encoding.BigEndianUnicode.GetString(bytes); // UTF-16 BE
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            content = Encoding.UTF8.GetString(bytes); // UTF-8 BOM
        else
            content = Encoding.Latin1.GetString(bytes); // Latin-1 fallback (VB6 default)

        ini.Parse(content);
        return ini;
    }

    private void Parse(string content)
    {
        string currentSection = "";
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r', ' ', '\t');

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Skip comment lines
            if (line.StartsWith(';') || line.StartsWith('\'')) continue;

            // Section header
            if (line.StartsWith('['))
            {
                int end = line.IndexOf(']');
                if (end > 1)
                {
                    currentSection = line.Substring(1, end - 1).Trim();
                    if (!_sections.ContainsKey(currentSection))
                    {
                        _sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _sectionOrder.Add(currentSection);
                    }
                }
                continue;
            }

            // Key=Value
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();

            // Strip inline comments (VB6: space + apostrophe)
            int commentIdx = val.IndexOf(" '", StringComparison.Ordinal);
            if (commentIdx >= 0) val = val.Substring(0, commentIdx).TrimEnd();
            commentIdx = val.IndexOf(" ;", StringComparison.Ordinal);
            if (commentIdx >= 0) val = val.Substring(0, commentIdx).TrimEnd();

            if (currentSection.Length == 0)
            {
                currentSection = "GLOBAL";
                if (!_sections.ContainsKey(currentSection))
                {
                    _sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _sectionOrder.Add(currentSection);
                }
            }
            _sections[currentSection][key] = val;
        }
    }

    // ── Save ──

    public void Save(string path)
    {
        var sb = new StringBuilder();
        foreach (var section in _sectionOrder)
        {
            sb.AppendLine($"[{section}]");
            if (_sections.TryGetValue(section, out var sec))
            {
                foreach (var (key, val) in sec)
                    sb.AppendLine($"{key}={val}");
            }
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), Encoding.Latin1);
    }
}

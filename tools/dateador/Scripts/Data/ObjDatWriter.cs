#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AODateador.Data;

/// <summary>
/// Non-destructive editor for obj.dat.
///
/// The file is hand-authored VB6-era INI: UTF-16LE, ~100 distinct keys of which
/// the game only reads a subset, a header comment block documenting every
/// ObjType, inline comments, and mixed line endings (most lines are CRLF, but
/// fields rewritten by tooling ended up LF-only).
///
/// So it is edited in place rather than regenerated: everything outside the
/// keys being changed is copied through byte for byte. Regenerating instead —
/// which is what the old <c>IniFile.Save</c> did — dropped the comments, forced
/// one encoding and one line ending, and silently deleted every key the model
/// did not know about.
///
/// Adapted from <c>tools/grhtool/ObjDat.cs</c>, extended to insert keys that are
/// missing and remove those reset to their default.
/// </summary>
public static class ObjDatWriter
{
    private static readonly Regex SectionRx =
        new(@"^\[OBJ(\d+)\]", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>A key to write, or remove when <see cref="Value"/> is null.</summary>
    public readonly struct FieldEdit
    {
        public readonly string Key;
        public readonly string? Value;

        public FieldEdit(string key, string? value) { Key = key; Value = value; }

        public static FieldEdit Set(string key, string value) => new(key, value);
        public static FieldEdit Remove(string key) => new(key, null);
    }

    /// <summary>
    /// Applies per-object edits, leaving every other byte untouched.
    /// </summary>
    /// <param name="text">Original file contents.</param>
    /// <param name="edits">Object number to the fields changed on it.</param>
    public static string Apply(string text, IReadOnlyDictionary<int, List<FieldEdit>> edits)
    {
        if (edits.Count == 0) return text;

        var sb = new StringBuilder(text.Length + 256);
        var matches = SectionRx.Matches(text);
        int cursor = 0;

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            // Everything before this section, verbatim.
            sb.Append(text, cursor, start - cursor);
            cursor = end;

            string body = text[start..end];
            if (int.TryParse(matches[i].Groups[1].Value, out int number)
                && edits.TryGetValue(number, out var fields))
            {
                foreach (var edit in fields)
                    body = edit.Value is null
                        ? RemoveField(body, edit.Key)
                        : SetField(body, edit.Key, edit.Value);
            }
            sb.Append(body);
        }

        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    /// <summary>
    /// Replaces the key's value, or appends the key when the section lacks it.
    /// Any inline comment on the line is preserved.
    /// </summary>
    private static string SetField(string body, string key, string value)
    {
        var rx = KeyRegex(key);
        var match = rx.Match(body);
        if (match.Success)
        {
            // Groups: 1 = "Key=", 2 = value, 3 = trailing comment (may be empty).
            return body.Remove(match.Index, match.Length)
                       .Insert(match.Index, match.Groups[1].Value + value + match.Groups[3].Value);
        }
        return AppendField(body, key, value);
    }

    private static string RemoveField(string body, string key)
    {
        var match = KeyRegex(key).Match(body);
        if (!match.Success) return body;

        // Take the line ending with it so no blank line is left behind.
        int end = match.Index + match.Length;
        if (end < body.Length && body[end] == '\r') end++;
        if (end < body.Length && body[end] == '\n') end++;
        return body.Remove(match.Index, end - match.Index);
    }

    /// <summary>
    /// Adds a key after the section's last one, matching the line ending its
    /// neighbours use — the file mixes CRLF and LF, and normalising would show
    /// up as a diff on every line of the section.
    /// </summary>
    private static string AppendField(string body, string key, string value)
    {
        var lines = Regex.Matches(body, @"^[^\r\n]*[A-Za-z][^\r\n]*=[^\r\n]*", RegexOptions.Multiline);
        string newline = body.Contains("\r\n") ? "\r\n" : "\n";

        if (lines.Count == 0)
        {
            // Header only: put the key straight after it.
            var header = Regex.Match(body, @"^\[OBJ\d+\][^\r\n]*(\r?\n)");
            if (!header.Success) return body;
            int at = header.Index + header.Length;
            return body.Insert(at, $"{key}={value}{header.Groups[1].Value}");
        }

        var last = lines[lines.Count - 1];
        int insertAt = last.Index + last.Length;

        // Adopt the line ending already used after the last field.
        if (insertAt < body.Length)
        {
            if (body[insertAt] == '\r' && insertAt + 1 < body.Length && body[insertAt + 1] == '\n')
            {
                newline = "\r\n";
                insertAt += 2;
            }
            else if (body[insertAt] == '\n')
            {
                newline = "\n";
                insertAt += 1;
            }
        }
        return body.Insert(insertAt, $"{key}={value}{newline}");
    }

    /// <summary>
    /// Matches one key line. Case-insensitive because the file is inconsistent —
    /// both <c>ObjType</c> and <c>Objtype</c> occur — and the trailing group
    /// captures inline comments such as <c>Respawn=1 ' Hace ReSpawn</c>.
    ///
    /// Anchored with a lookahead rather than <c>$</c>: the file is CRLF, and in
    /// multiline mode <c>$</c> sits before the <c>\r</c>, so a pattern ending in
    /// <c>$</c> never matches a line that has one.
    /// </summary>
    private static Regex KeyRegex(string key)
        // The value stops before any run of spaces preceding a comment, so the
        // separator stays in group 3 and survives a rewrite: `Respawn=1 ' Hace
        // ReSpawn` must not become `Respawn=0' Hace ReSpawn`.
        => new($@"^({Regex.Escape(key)}=)([^\r\n']*?)((?:[ \t]*'[^\r\n]*)?)(?=\r?\n|$)",
               RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>Reads a key from one section body, or null when absent.</summary>
    public static string? ReadField(string body, string key)
    {
        var match = KeyRegex(key).Match(body);
        return match.Success ? match.Groups[2].Value.Trim() : null;
    }

    /// <summary>Section bodies by object number, for reading current values.</summary>
    public static Dictionary<int, string> SplitSections(string text)
    {
        var result = new Dictionary<int, string>();
        var matches = SectionRx.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            if (int.TryParse(matches[i].Groups[1].Value, out int number))
                result[number] = text[start..end];
        }
        return result;
    }
}

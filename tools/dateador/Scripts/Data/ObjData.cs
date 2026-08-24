#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AODateador.Data;

/// <summary>
/// One object of obj.dat, held as its raw section text plus the values read
/// from it.
///
/// Keeping the section text is what makes editing safe: fields the tool does
/// not model — roughly forty keys inherited from VB6 that no parser reads —
/// stay in the text and are written back untouched.
/// </summary>
public sealed class ObjData
{
    public int Index;

    /// <summary>Verbatim `[OBJn]` section, including its header line.</summary>
    public string RawSection = "";

    /// <summary>Edits not yet written to disk, by key.</summary>
    private readonly Dictionary<string, string?> _pending = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDirty => _pending.Count > 0;

    public string Name => Get("Name") ?? "";
    public int ObjType => GetInt("ObjType");
    public int GrhIndex => GetInt("GrhIndex");

    /// <summary>Current value: a pending edit if any, otherwise the file's.</summary>
    public string? Get(string key)
    {
        if (_pending.TryGetValue(key, out var pending)) return pending;
        return ObjDatWriter.ReadField(RawSection, key);
    }

    public int GetInt(string key)
        => int.TryParse(Get(key), out int value) ? value : 0;

    public bool GetBool(string key) => GetInt(key) == 1;

    /// <summary>
    /// Stages an edit. Writing a value equal to what the file already holds
    /// clears the edit instead of recording a no-op, so an accidental focus
    /// change does not mark the object dirty.
    /// </summary>
    public void Set(string key, string? value)
    {
        string? onDisk = ObjDatWriter.ReadField(RawSection, key);
        if (value == onDisk) { _pending.Remove(key); return; }
        _pending[key] = value;
    }

    public void SetInt(string key, int value) => Set(key, value.ToString());
    public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

    /// <summary>Removes the key from the file on the next save.</summary>
    public void Clear(string key) => Set(key, null);

    public IReadOnlyList<ObjDatWriter.FieldEdit> PendingEdits()
        => _pending.Select(kv => new ObjDatWriter.FieldEdit(kv.Key, kv.Value)).ToList();

    public void MarkSaved(string newRawSection)
    {
        RawSection = newRawSection;
        _pending.Clear();
    }

    /// <summary>Class restrictions, read from CP1..CP16.</summary>
    public List<string> ProhibitedClasses()
    {
        var result = new List<string>();
        for (int i = 1; i <= 16; i++)
        {
            string? value = Get($"CP{i}");
            if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
        }
        return result;
    }

    /// <summary>
    /// Rewrites CP1..CP16 to hold exactly <paramref name="classes"/>, packed
    /// from CP1 with no gaps; the server stops reading at CP8.
    /// </summary>
    public void SetProhibitedClasses(IReadOnlyList<string> classes)
    {
        for (int i = 1; i <= 16; i++)
        {
            string key = $"CP{i}";
            if (i <= classes.Count) Set(key, classes[i - 1]);
            else if (ObjDatWriter.ReadField(RawSection, key) != null) Clear(key);
        }
    }
}

/// <summary>
/// All objects of obj.dat, with a save path that never regenerates the file.
/// </summary>
public sealed class ObjDatabase
{
    /// <summary>Objects by their `[OBJn]` number.</summary>
    public readonly Dictionary<int, ObjData> Objects = new();

    public string SourcePath { get; private set; } = "";

    /// <summary>Full file text, the base every edit is applied to.</summary>
    private string _text = "";

    /// <summary>
    /// Encoding detected on load and reused on save. obj.dat ships as UTF-16LE;
    /// writing it back as anything else corrupts every accented name.
    /// </summary>
    private Encoding _encoding = Encoding.Unicode;

    /// <summary>
    /// Whether the file began with a BOM. Recorded so it is written back:
    /// dropping it still parses, because every reader has a fallback
    /// heuristic, but it is a silent change to a file we promise not to alter
    /// beyond the fields being edited.
    /// </summary>
    private bool _hadBom;

    public IEnumerable<ObjData> All => Objects.Values.OrderBy(o => o.Index);

    public bool IsDirty => Objects.Values.Any(o => o.IsDirty);

    /// <summary>
    /// Loads obj.dat. Accepts either the file itself or the directory holding
    /// it, since the rest of the tool passes a dat directory around. The name
    /// is lower case on disk in server/dat but capitalised under resources/.
    /// </summary>
    public static ObjDatabase Load(string pathOrDir)
    {
        string path = pathOrDir;
        if (Directory.Exists(pathOrDir))
        {
            path = new[] { "obj.dat", "Obj.dat" }
                .Select(name => Path.Combine(pathOrDir, name))
                .FirstOrDefault(File.Exists)
                ?? Path.Combine(pathOrDir, "obj.dat");
        }

        var db = new ObjDatabase { SourcePath = path };
        byte[] bytes = File.ReadAllBytes(path);
        db._encoding = DetectEncoding(bytes);
        db._text = db._encoding.GetString(bytes);
        // A BOM decodes to U+FEFF. Strip it from the working text so offsets
        // line up, and remember to put it back on save.
        if (db._text.Length > 0 && db._text[0] == '﻿')
        {
            db._hadBom = true;
            db._text = db._text[1..];
        }

        foreach (var (number, body) in ObjDatWriter.SplitSections(db._text))
            db.Objects[number] = new ObjData { Index = number, RawSection = body };

        return db;
    }

    /// <summary>
    /// Applies every pending edit and writes the file, keeping a .bak.
    /// Returns how many objects changed.
    /// </summary>
    public int Save(IEnumerable<string>? alsoWriteTo = null)
    {
        var edits = new Dictionary<int, List<ObjDatWriter.FieldEdit>>();
        foreach (var obj in Objects.Values.Where(o => o.IsDirty))
            edits[obj.Index] = obj.PendingEdits().ToList();

        if (edits.Count == 0) return 0;

        _text = ObjDatWriter.Apply(_text, edits);

        WriteFile(SourcePath, _text, backup: true);
        foreach (string extra in alsoWriteTo ?? Enumerable.Empty<string>())
            WriteFile(extra, _text, backup: true);

        // Re-split so each object's raw text matches what is now on disk.
        var sections = ObjDatWriter.SplitSections(_text);
        foreach (var obj in Objects.Values)
            if (sections.TryGetValue(obj.Index, out var body)) obj.MarkSaved(body);

        return edits.Count;
    }

    private void WriteFile(string path, string text, bool backup)
    {
        if (backup && File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);

        // Write through a temp file so an interrupted save cannot truncate the
        // original.
        //
        // The BOM comes from an encoder configured to emit one, never from
        // prepending U+FEFF to the text: the text is re-read after each save,
        // so a manual prefix would stack up one BOM per save.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, BomEncoding());
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// The load encoding, set to emit a BOM only if the file had one.
    /// <c>Encoding.Unicode</c> writes one by default, so the no-BOM case needs
    /// an explicitly configured instance.
    /// </summary>
    private Encoding BomEncoding()
    {
        if (_encoding is UnicodeEncoding)
            return new UnicodeEncoding(bigEndian: _encoding.CodePage == 1201,
                                       byteOrderMark: _hadBom);
        if (_encoding is UTF8Encoding)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: _hadBom);
        return _encoding;
    }

    /// <summary>
    /// Detects the encoding from the BOM, falling back to a zero-byte
    /// heuristic. Mirrors <c>ObjectLoader.DecodeObjDat</c> in the client.
    /// </summary>
    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;

        // No BOM: UTF-16 text is half zero bytes, and which half says the order.
        int evenZeros = 0, oddZeros = 0, sample = Math.Min(bytes.Length, 512);
        for (int i = 0; i < sample; i++)
            if (bytes[i] == 0) { if (i % 2 == 0) evenZeros++; else oddZeros++; }

        if (oddZeros > sample / 8) return Encoding.Unicode;
        if (evenZeros > sample / 8) return Encoding.BigEndianUnicode;
        return Encoding.Latin1;
    }
}

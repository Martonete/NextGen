using System.Text;
using System.Text.RegularExpressions;

namespace GrhTool;

/// <summary>
/// Repoints obj.dat at the new catalogue.
///
/// The file is UTF-16LE INI text with one [OBJn] section per item. Three fields
/// reference art, and each points at a different table:
///   GrhIndex  - a static GRH, the inventory icon
///   Anim      - an index into Armas.dat or Escudos.dat, by ObjType
///   NumRopaje - an index into Personajes.ind
///
/// The old values cannot be translated through remap.json: they addressed the
/// legacy art, which was dropped rather than renumbered. They are reassigned
/// from the classified catalogue instead, matched by item type.
/// </summary>
public static class ObjDat
{
    // ObjType values that matter for art assignment, from the header comment
    // block in obj.dat itself.
    public const int TypeWeapon = 2;
    public const int TypeArmour = 3;
    public const int TypeShield = 16;
    public const int TypeHelmet = 17;

    public sealed class Item
    {
        public int Number;
        public string Name = "";
        public int ObjType;
        public int GrhIndex;
        public int Anim;
        public int NumRopaje;
        public bool HasAnim;
        public bool HasRopaje;
    }

    private static readonly Regex SectionRx = new(@"^\[OBJ(\d+)\]", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Reads the fields this tool rewrites; the rest of the file is left untouched.</summary>
    public static List<Item> Parse(string text)
    {
        var items = new List<Item>();
        var matches = SectionRx.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            string body = text[start..end];

            var item = new Item { Number = int.Parse(matches[i].Groups[1].Value) };
            item.Name = Field(body, "Name") ?? "";
            // Casing is inconsistent in the shipped file: both ObjType and Objtype occur.
            item.ObjType = ParseInt(Field(body, "ObjType") ?? Field(body, "Objtype"));
            item.GrhIndex = ParseInt(Field(body, "GrhIndex"));

            string? anim = Field(body, "Anim");
            item.HasAnim = anim is not null;
            item.Anim = ParseInt(anim);

            string? ropaje = Field(body, "NumRopaje");
            item.HasRopaje = ropaje is not null;
            item.NumRopaje = ParseInt(ropaje);

            items.Add(item);
        }
        return items;
    }

    private static string? Field(string body, string key)
    {
        var m = Regex.Match(body, $@"^{key}=(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static int ParseInt(string? s) => int.TryParse(s, out int v) ? v : 0;

    /// <summary>
    /// Rewrites the three art fields in place, preserving every other line and
    /// the file's UTF-16LE encoding. Editing the text rather than regenerating
    /// it keeps the hand-authored stats, comments and layout intact.
    /// </summary>
    public static string Rewrite(string text, IReadOnlyDictionary<int, (int grh, int anim, int ropaje)> assign)
    {
        var sb = new StringBuilder(text.Length);
        var matches = SectionRx.Matches(text);
        int cursor = 0;

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            sb.Append(text, cursor, start - cursor);
            string body = text[start..end];

            int number = int.Parse(matches[i].Groups[1].Value);
            if (assign.TryGetValue(number, out var a))
            {
                body = ReplaceField(body, "GrhIndex", a.grh);
                if (a.anim > 0) body = ReplaceField(body, "Anim", a.anim);
                if (a.ropaje > 0) body = ReplaceField(body, "NumRopaje", a.ropaje);
            }
            sb.Append(body);
            cursor = end;
        }
        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    private static string ReplaceField(string body, string key, int value)
        => Regex.Replace(body, $@"^({key}=).*$", $"${{1}}{value}",
                         RegexOptions.Multiline | RegexOptions.IgnoreCase);
}

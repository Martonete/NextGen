using System.Text;
using System.Text.RegularExpressions;
using AODateador.Data;
using E = AODateador.Data.ObjDatWriter.FieldEdit;

namespace AODateador.Test;

internal static class WriterTests
{
    /// <summary>Runs the suite and returns how many checks failed.</summary>
    public static int Run()
    {
        string path = @"c:\Users\marti\Documents\AORust\argentum-nextgen\server\dat\obj.dat";
        string text = File.ReadAllText(path, Encoding.Unicode);
        int fails = 0;

        void Check(string name, bool ok, string? detail = null)
        {
            Console.WriteLine($"  {(ok ? "OK  " : "FALLA")} {name}{(detail is null ? "" : "  -> " + detail)}");
            if (!ok) fails++;
        }

        string Apply(int obj, params E[] edits) =>
            ObjDatWriter.Apply(text, new Dictionary<int, List<E>> { [obj] = edits.ToList() });

        // 1. No edits: byte-identical.
        Check("sin ediciones: identico",
            ObjDatWriter.Apply(text, new Dictionary<int, List<E>>()) == text);

        // 2. Writing back the value already there is a no-op.
        var sections = ObjDatWriter.SplitSections(text);
        string current = ObjDatWriter.ReadField(sections[1], "GrhIndex")!;
        Check("lee un valor existente", current.Length > 0, $"GrhIndex={current}");
        Check("reescribir el mismo valor: identico", Apply(1, E.Set("GrhIndex", current)) == text);

        // 3. One field changed = one line differs.
        var changed = Apply(1, E.Set("GrhIndex", "999"));
        var a = text.Split('\n');
        var b = changed.Split('\n');
        int diff = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) diff++;
        Check("cambiar 1 campo: 1 linea distinta", diff == 1 && a.Length == b.Length, $"{diff} lineas");

        // 4. Mixed line endings survive: OBJ1's GrhIndex line is LF-only in the source.
        Check("conserva el LF suelto", changed.Contains("GrhIndex=999\nObjType=1"));

        // 5. Insert a key the section lacks.
        var added = Apply(1, E.Set("MaxHIT", "42"));
        Check("inserta clave nueva", ObjDatWriter.ReadField(ObjDatWriter.SplitSections(added)[1], "MaxHIT") == "42");
        Check("insercion no toca otras secciones",
            ObjDatWriter.SplitSections(added)[2] == ObjDatWriter.SplitSections(text)[2]);

        // 6. Remove a key, taking its line.
        var removed = Apply(1, E.Remove("Crucial"));
        var rsec = ObjDatWriter.SplitSections(removed)[1];
        Check("borra clave", ObjDatWriter.ReadField(rsec, "Crucial") is null);
        Check("borrar no deja linea vacia", !rsec.Contains("\r\n\r\n\r\n"));

        // 7. Insert then remove is a round-trip.
        var rt = ObjDatWriter.Apply(added, new Dictionary<int, List<E>> { [1] = new() { E.Remove("MaxHIT") } });
        Check("insertar+borrar: identico", rt == text, rt == text ? null : $"len {text.Length} -> {rt.Length}");

        // 8. Inline comments survive an edit to their own line.
        var respawn = sections.First(kv => kv.Value.Contains("Respawn=1"));
        var withComment = Apply(respawn.Key, E.Set("Respawn", "0"));
        Check("conserva comentario inline", withComment.Contains("Respawn=0 ' Hace ReSpawn"));

        // 9. OBJ2 spells it "Objtype"; editing "ObjType" must hit that line, not add one.
        var casing = Apply(2, E.Set("ObjType", "2"));
        int typeLines = Regex.Matches(ObjDatWriter.SplitSections(casing)[2],
            @"(?im)^Obj[Tt]ype=").Count;
        Check("casing inconsistente: una sola clave", typeLines == 1, $"{typeLines} lineas ObjType");
        Check("casing: sin cambios netos", casing == text);

        // 10. Accents intact.
        Check("acentos intactos", changed.Contains("Poción"));

        // 11. Every section survives a no-op pass over all of them.
        var allEdits = new Dictionary<int, List<E>>();
        foreach (var (num, body) in sections)
        {
            string? g = ObjDatWriter.ReadField(body, "GrhIndex");
            if (g != null) allEdits[num] = new() { E.Set("GrhIndex", g) };
        }
        var allSame = ObjDatWriter.Apply(text, allEdits);
        Check($"reescribir GrhIndex en las {allEdits.Count} secciones: identico", allSame == text,
            allSame == text ? null : $"len {text.Length} -> {allSame.Length}");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "TODO OK" : $"{fails} FALLAS");
        return fails;
    }
}
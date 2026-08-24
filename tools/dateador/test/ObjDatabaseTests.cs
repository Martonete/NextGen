using System.Text;
using AODateador.Data;

namespace AODateador.Test;

internal static class DatabaseTests
{
    /// <summary>Runs the suite and returns how many checks failed.</summary>
    public static int Run()
    {
        // End-to-end: load obj.dat through ObjDatabase, save without edits and with
        // one edit, and confirm the file is only changed where intended.

        string repo = @"c:\Users\marti\Documents\AORust\argentum-nextgen";
        string src = Path.Combine(repo, "server", "dat", "obj.dat");
        string work = Path.Combine(Path.GetTempPath(), "objdat-e2e");
        Directory.CreateDirectory(work);
        string copy = Path.Combine(work, "obj.dat");
        File.Copy(src, copy, overwrite: true);

        byte[] original = File.ReadAllBytes(copy);
        // Pristine copy kept aside, so text comparisons read both sides alike.
        File.Copy(src, Path.Combine(work, "original.bak"), overwrite: true);
        int fails = 0;
        void Check(string name, bool ok, string? detail = null)
        {
            Console.WriteLine($"  {(ok ? "OK  " : "FALLA")} {name}{(detail is null ? "" : "  -> " + detail)}");
            if (!ok) fails++;
        }

        // 1. Load, save with nothing dirty.
        var db = ObjDatabase.Load(copy);
        Check("carga los objetos", db.Objects.Count == 1637, $"{db.Objects.Count}");
        Check("no arranca sucio", !db.IsDirty);
        int saved = db.Save();
        Check("guardar sin cambios no escribe", saved == 0, $"{saved} objetos");
        Check("archivo intacto", File.ReadAllBytes(copy).SequenceEqual(original));

        // 2. Set a value to what it already is: still not dirty.
        var first = db.Objects[1];
        first.Set("GrhIndex", first.GrhIndex.ToString());
        Check("reescribir el mismo valor no ensucia", !db.IsDirty);

        // 3. Change one graphic and save.
        int before = first.GrhIndex;
        first.SetInt("GrhIndex", 12345);
        Check("marca sucio al cambiar", db.IsDirty);
        saved = db.Save();
        Check("guarda 1 objeto", saved == 1, $"{saved}");
        Check("deja respaldo .bak", File.Exists(copy + ".bak"));

        var reloaded = ObjDatabase.Load(copy);
        Check("el cambio persiste", reloaded.Objects[1].GrhIndex == 12345,
            $"{reloaded.Objects[1].GrhIndex}");
        Check("los demas objetos no cambiaron",
            reloaded.Objects[2].GrhIndex == db.Objects[2].GrhIndex);

        // 4. Exactly one line differs from the original. Compared here, while
        //    the edit is still applied — step 6 reverts it.
        // Both sides read the same way: Encoding.GetString keeps the decoded
        // U+FEFF while File.ReadAllText strips it, and comparing one against
        // the other reports a phantom difference on line 0.
        string a = File.ReadAllText(Path.Combine(work, "original.bak"), Encoding.Unicode);
        string b = File.ReadAllText(copy, Encoding.Unicode);
        var la = a.Split('\n'); var lb = b.Split('\n');
        int diff = la.Length == lb.Length
            ? Enumerable.Range(0, la.Length).Count(i => la[i] != lb[i]) : -1;
        var differing = Enumerable.Range(0, Math.Min(la.Length, lb.Length))
                                  .Where(i => la[i] != lb[i]).ToList();
        Check("una sola linea distinta", diff == 1,
            diff == 1 ? null
                      : $"{diff} lineas: " + string.Join(" | ", differing.Take(4)
                            .Select(i => $"L{i} [{la[i].TrimEnd()}]->[{lb[i].TrimEnd()}]")));

        // 5. Encoding and comments survived.
        Check("sigue UTF-16LE", File.ReadAllBytes(copy) is [0xFF, 0xFE, ..]);
        Check("conserva acentos", b.Contains("Poción") || b.Contains("Árbol"));
        Check("conserva comentarios de cabecera", b.Contains("Tipo de objetos"));
        Check("conserva campos no modelados", b.Contains("AntiLimpieza"));

        // 6. Restore and confirm we are back to byte-identical.
        reloaded.Objects[1].SetInt("GrhIndex", before);
        reloaded.Save();
        Check("revertir da identico", File.ReadAllBytes(copy).SequenceEqual(original));

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "TODO OK" : $"{fails} FALLAS");
        return fails;
    }
}
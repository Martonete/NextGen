using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrhTool;

/// <summary>
/// Catalogue tooling for Graficos.ind. Console-only (no Godot) so it can run in
/// CI and so its output can be diffed.
///
///   grhtool dump     &lt;ind&gt; &lt;out.json&gt;      volcado completo del indice
///   grhtool build    &lt;in.json&gt; &lt;out.ind&gt;   reconstruye el binario
///   grhtool verify   &lt;ind&gt; [--graficos d]  invariantes: refs colgadas, cobertura
///   grhtool classify &lt;ind&gt; &lt;out.json&gt;     clasifica laminas por geometria
/// </summary>
internal static class Program
{
    /// <summary>Sheets numbered at or above this belong to the new pixel-art catalogue.</summary>
    public const int NewArtSheetBase = 100000;

    /// <summary>
    /// Above this, a scan result is noise rather than sprites. The largest
    /// legitimate sheet in the indexed catalogue holds 141 regions.
    /// </summary>
    private const int MaxRegionsPerSheet = 200;

    // GrhEntry exposes plain fields, so IncludeFields is required or the dump
    // silently writes zeros. Defaults are written too: an SX of 0 is real data.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    private static int Main(string[] args)
    {
        if (args.Length < 1) { Usage(); return 1; }
        // The .dat files are Latin-1; .NET Core ships only Unicode by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return args[0] switch
            {
                "dump" => Dump(args),
                "build" => Build(args),
                "verify" => Verify(args),
                "classify" => ClassifyCmd(args),
                "index" => IndexCmd(args),
                "checkscan" => CheckScanCmd(args),
                "remap" => RemapCmd(args),
                "thin" => ThinCmd(args),
                "entities" => EntitiesCmd(args),
                "objdat" => ObjDatCmd(args),
                "npcs" => NpcsCmd(args),
                "fxs" => FxsCmd(args),
                "special" => SpecialCmd(args),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            uso:
              grhtool dump     <ind> <out.json>
              grhtool build    <in.json> <out.ind>
              grhtool verify   <ind> [--graficos <dir>]
              grhtool classify <ind> <out.json>
            """);
        return 1;
    }

    // ---- dump -------------------------------------------------------------

    private sealed class DumpFile
    {
        public int Version { get; set; }
        public int Count { get; set; }
        public string? Trailer { get; set; }
        public List<GrhEntry> Entries { get; set; } = new();
    }

    private static int Dump(string[] args)
    {
        if (args.Length < 3) return Usage();
        var idx = GrhIndex.Load(args[1]);
        var d = new DumpFile
        {
            Version = idx.Version,
            Count = idx.Count,
            Entries = idx.Entries,
            Trailer = idx.Trailer.Length > 0 ? Convert.ToBase64String(idx.Trailer) : null,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        File.WriteAllText(args[2], JsonSerializer.Serialize(d, Json));
        Console.WriteLine($"{idx.Entries.Count} entradas -> {args[2]}");
        Console.WriteLine($"  version={idx.Version} count={idx.Count} trailer={idx.Trailer.Length}B");
        return 0;
    }

    private static int Build(string[] args)
    {
        if (args.Length < 3) return Usage();
        var d = JsonSerializer.Deserialize<DumpFile>(File.ReadAllText(args[1]), Json)!;
        var idx = new GrhIndex
        {
            Version = d.Version,
            Count = d.Count,
            Entries = d.Entries,
            Trailer = d.Trailer is null ? Array.Empty<byte>() : Convert.FromBase64String(d.Trailer),
        };
        idx.Save(args[2]);
        Console.WriteLine($"{idx.Entries.Count} entradas -> {args[2]}");
        return 0;
    }

    // ---- verify -----------------------------------------------------------

    private static int Verify(string[] args)
    {
        if (args.Length < 2) return Usage();
        var idx = GrhIndex.Load(args[1]);
        string? gfxDir = null;
        for (int i = 2; i < args.Length - 1; i++)
            if (args[i] == "--graficos") gfxDir = args[i + 1];

        var byIndex = new Dictionary<int, GrhEntry>();
        var dupes = new List<int>();
        foreach (var e in idx.Entries)
            if (!byIndex.TryAdd(e.Index, e)) dupes.Add(e.Index);

        var statics = idx.Entries.Where(e => !e.IsAnimated).ToList();
        var anims = idx.Entries.Where(e => e.IsAnimated).ToList();

        Console.WriteLine($"entradas         : {idx.Entries.Count}  (estaticas {statics.Count}, animadas {anims.Count})");
        Console.WriteLine($"indice maximo    : {idx.Entries.Max(e => e.Index)}");
        Console.WriteLine($"duplicados       : {dupes.Count}");

        // Dangling frame references break animations silently at runtime.
        var dangling = new List<(int grh, int frame)>();
        foreach (var a in anims)
            foreach (int f in a.Frames)
                if (!byIndex.ContainsKey(f)) dangling.Add((a.Index, f));
        Console.WriteLine($"refs colgadas    : {dangling.Count}");
        foreach (var (g, f) in dangling.Take(10))
            Console.WriteLine($"    grh {g} -> frame {f} inexistente");

        // Hard ceilings that silently truncate rather than error.
        int overUShort = byIndex.Keys.Count(k => k > 65535);
        int overShort = byIndex.Keys.Count(k => k > 32767);
        Console.WriteLine($"GRH > 65535      : {overUShort}   (techo de Personajes/Cabezas/Cascos)");
        Console.WriteLine($"GRH > 32767      : {overShort}   (techo de Fxs.ind y capas .map legacy)");

        var sheets = statics.Select(s => s.FileNum).Distinct().ToHashSet();
        int newSheets = sheets.Count(s => s >= NewArtSheetBase);
        Console.WriteLine($"laminas          : {sheets.Count}  (arte nuevo {newSheets}, viejo {sheets.Count - newSheets})");

        if (gfxDir is not null)
        {
            var onDisk = Directory.EnumerateFiles(gfxDir, "*.png")
                .Select(p => int.TryParse(Path.GetFileNameWithoutExtension(p), out int n) ? n : -1)
                .Where(n => n >= NewArtSheetBase).ToHashSet();
            var missing = onDisk.Where(s => !sheets.Contains(s)).OrderBy(s => s).ToList();
            var ghost = sheets.Where(s => s >= NewArtSheetBase && !onDisk.Contains(s)).OrderBy(s => s).ToList();
            Console.WriteLine();
            Console.WriteLine($"cobertura arte nuevo : {onDisk.Count - missing.Count}/{onDisk.Count} laminas indexadas");
            Console.WriteLine($"  sin indexar        : {missing.Count}");
            Console.WriteLine($"  indexadas sin PNG  : {ghost.Count}");
            if (missing.Count > 0)
                Console.WriteLine("  primeras: " + string.Join(" ", missing.Take(15)));
        }

        return dangling.Count > 0 || dupes.Count > 0 ? 2 : 0;
    }

    /// <summary>
    /// Picks a region-splitting strategy from the sheet's number block. Measured
    /// against the indexed catalogue with `checkscan`: band scanning reproduces
    /// almost nothing on its own (3/40), because tiled terrain has no empty gaps
    /// to cut on and bodies are a ragged 6/6/5/5 grid. Choosing by category
    /// instead lifts that to 72/120, and to nearly all of the classes that matter.
    /// </summary>
    private static (List<SheetScanner.Region>, string) ScanByClass(string png, int sheet)
    {
        using (var probe = new System.Drawing.Bitmap(png))
        {
            // A body sheet: 150x180 of 25x45 cells, rows of 6/6/5/5.
            if (probe.Width == 150 && probe.Height == 180)
                return (SheetScanner.BodyGrid(), "cuerpo6655");

            // Head, helmet and weapon strips: one pose per facing, evenly spaced.
            int block = sheet / 1000;
            if (block is 102 or 118 && probe.Width % 4 == 0)
                return (SheetScanner.UniformGrid(png, 1, 4), "grilla1x4");

            // Terrain: cut on the 32px grid, keeping only cells with pixels.
            if (probe.Width % 32 == 0 && probe.Height % 32 == 0)
            {
                var grid = SheetScanner.Grid(png, 32);
                if (grid.Count > 1) return (grid, "grilla32");
            }
        }

        // Anything else - props, atlases, odd sizes - falls back to gap scanning.
        var bands = SheetScanner.Scan(png);

        // Scenery atlases hold irregular pieces (bushes, ruins, palms) that a
        // horizontal band cuts across, shattering one sheet into hundreds of
        // fragments. These are placed per tile on the map anyway, so fall back
        // to the 32px grid when the band result is clearly shrapnel.
        if (bands.Count > MaxRegionsPerSheet)
        {
            // Only when the grid stays within a sane size. Cutting a 1024px
            // scenery atlas into 32px cells yields thousands of mostly-empty
            // GRHs, which would push the catalogue past the 65535 ceiling that
            // Personajes/Cabezas/Cascos impose - for art that is decoration.
            var grid = SheetScanner.Grid(png, 32);
            if (grid.Count > 0 && grid.Count <= MaxRegionsPerSheet)
                return (grid, "grilla32/atlas");
        }
        return (bands, "bandas");
    }

    // ---- checkscan --------------------------------------------------------

    /// <summary>
    /// Scans sheets that are already indexed and compares the result against the
    /// index. Calibration for the scanner: if it cannot reproduce geometry known
    /// to be correct, its output on unindexed sheets cannot be trusted either.
    /// </summary>
    private static int CheckScanCmd(string[] args)
    {
        if (args.Length < 3) return Usage();
        var idx = GrhIndex.Load(args[1]);
        string gfxDir = args[2];
        int sample = 40;
        for (int i = 3; i < args.Length - 1; i++)
            if (args[i] == "--sample") sample = int.Parse(args[i + 1]);

        var bySheet = new Dictionary<int, List<GrhEntry>>();
        foreach (var e in idx.Entries)
        {
            if (e.IsAnimated || e.FileNum < NewArtSheetBase) continue;
            if (!bySheet.TryGetValue(e.FileNum, out var l)) bySheet[e.FileNum] = l = new();
            l.Add(e);
        }

        // Sample across the whole range rather than the first N, so one odd
        // block cannot make the scanner look better or worse than it is.
        var rng = new Random(1);
        var picked = bySheet.Keys.OrderBy(_ => rng.Next()).Take(sample).OrderBy(k => k).ToList();

        // Which strategy reproduces each sheet is an empirical question, so try
        // them all and tally per signature rather than assuming.
        var winners = new Dictionary<string, Dictionary<string, int>>();
        int anyExact = 0;

        foreach (int sheet in picked)
        {
            string png = Path.Combine(gfxDir, $"{sheet}.png");
            if (!File.Exists(png)) continue;
            var known = bySheet[sheet];

            var candidates = new List<(string name, List<SheetScanner.Region> regions)>
            {
                ("bandas", SheetScanner.Scan(png)),
                ("grilla32", SheetScanner.Grid(png, 32)),
            };
            // A directional strip: try the row/col split its region count implies.
            candidates.Add(("cuerpo6655", SheetScanner.BodyGrid()));
            if (known.Count is 4) candidates.Add(("grilla1x4", SheetScanner.UniformGrid(png, 1, 4)));

            string sig = $"{known.Count} reg";
            if (!winners.TryGetValue(sig, out var tally)) winners[sig] = tally = new();

            bool matched = false;
            foreach (var (name, regions) in candidates)
            {
                bool exactMatch = regions.Count == known.Count && known
                    .OrderBy(k => k.SY).ThenBy(k => k.SX)
                    .Zip(regions.OrderBy(s => s.Y).ThenBy(s => s.X))
                    .All(p => p.First.SX == p.Second.X && p.First.SY == p.Second.Y
                           && p.First.Width == p.Second.W && p.First.Height == p.Second.H);
                if (exactMatch)
                {
                    tally[name] = tally.GetValueOrDefault(name) + 1;
                    matched = true;
                    break;
                }
            }
            if (matched) anyExact++;
            else tally["ninguna"] = tally.GetValueOrDefault("ninguna") + 1;
        }

        Console.WriteLine($"muestra: {picked.Count} laminas ya indexadas");
        Console.WriteLine($"reproducidas exactamente por alguna estrategia: {anyExact}");
        Console.WriteLine();
        foreach (var (sig, tally) in winners.OrderByDescending(k => k.Value.Values.Sum()).Take(15))
            Console.WriteLine($"  {sig,-10} {string.Join("  ", tally.Select(t => $"{t.Key}={t.Value}"))}");
        return 0;
    }

    // ---- index ------------------------------------------------------------

    /// <summary>
    /// Adds static GRHs for sheets that have no entry yet. Regions come from
    /// pixel scanning, which is why this runs in reviewable batches rather than
    /// over the whole catalogue at once: sprites whose shadow or glow touches a
    /// neighbour can be split wrong, and that is only visible by looking.
    /// </summary>
    private static int IndexCmd(string[] args)
    {
        if (args.Length < 3) return Usage();
        string indPath = args[1], gfxDir = args[2];
        bool apply = args.Contains("--apply");
        int limit = int.MaxValue;
        for (int i = 3; i < args.Length - 1; i++)
            if (args[i] == "--limit") limit = int.Parse(args[i + 1]);

        var idx = GrhIndex.Load(indPath);
        var indexedSheets = idx.Entries.Where(e => !e.IsAnimated).Select(e => e.FileNum).ToHashSet();
        var usedGrh = idx.Entries.Select(e => e.Index).ToHashSet();

        var missing = Directory.EnumerateFiles(gfxDir, "*.png")
            .Select(p => int.TryParse(Path.GetFileNameWithoutExtension(p), out int n) ? n : -1)
            .Where(n => n >= NewArtSheetBase && !indexedSheets.Contains(n))
            .OrderBy(n => n).Take(limit).ToList();

        Console.WriteLine($"laminas sin indexar: {missing.Count}");
        if (missing.Count == 0) return 0;

        int nextGrh = usedGrh.Max() + 1;
        var added = new List<GrhEntry>();
        var skipped = new List<int>();
        int failed = 0;

        foreach (int sheet in missing)
        {
            List<SheetScanner.Region> regions;
            string strategy;
            try
            {
                (regions, strategy) = ScanByClass(Path.Combine(gfxDir, $"{sheet}.png"), sheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {sheet}: no se pudo leer ({ex.Message})");
                failed++;
                continue;
            }

            if (regions.Count == 0)
            {
                Console.WriteLine($"  {sheet}: lamina vacia, salteo");
                failed++;
                continue;
            }

            // Hundreds of regions from a band scan means it latched onto stray
            // pixels rather than sprites. A grid split is different: a 1024px
            // atlas legitimately yields a thousand 32px cells, so the ceiling
            // applies only where a high count signals shrapnel.
            if (strategy == "bandas" && regions.Count > MaxRegionsPerSheet)
            {
                Console.WriteLine($"  {sheet}: {regions.Count} regiones, sospechoso -> requiere revision manual");
                skipped.Add(sheet);
                failed++;
                continue;
            }

            foreach (var r in regions)
            {
                added.Add(new GrhEntry
                {
                    Index = nextGrh++,
                    NumFrames = 1,
                    FileNum = sheet,
                    SX = (short)r.X, SY = (short)r.Y,
                    Width = (short)r.W, Height = (short)r.H,
                });
            }
            Console.WriteLine($"  {sheet}: {regions.Count} regiones [{strategy}] -> GRH {added[^regions.Count].Index}..{nextGrh - 1}");
        }

        Console.WriteLine();
        Console.WriteLine($"GRH nuevos : {added.Count}");
        Console.WriteLine($"laminas ok : {missing.Count - failed} / {missing.Count}");
        Console.WriteLine($"indice max : {(added.Count > 0 ? added[^1].Index : 0)}");
        if (skipped.Count > 0)
            Console.WriteLine($"a revisar  : {skipped.Count} -> {string.Join(" ", skipped.Take(20))}");

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        File.Copy(indPath, indPath + ".bak", overwrite: true);
        idx.Entries.AddRange(added);
        idx.Count = idx.Entries.Max(e => e.Index);
        idx.Save(indPath);
        Console.WriteLine($"\nescrito {indPath} (respaldo en {indPath}.bak)");
        return 0;
    }

    // ---- special ------------------------------------------------------------

    /// <summary>
    /// Fills in the GRH groups that used to be hardcoded in the renderer:
    /// water ranges, tree canopies and named UI graphics.
    ///
    /// Water matters beyond looks - InputHandler consults it to decide whether a
    /// tile can be walked on - so it is identified by two independent signals
    /// rather than colour alone: the tile must be animated and blue. Trees are
    /// tall, green, single-region props.
    /// </summary>
    private static int SpecialCmd(string[] args)
    {
        if (args.Length < 4) return Usage();
        string indPath = args[1], catPath = args[2], gfxDir = args[3];
        bool apply = args.Contains("--apply");

        var idx = GrhIndex.Load(indPath);
        var cat = Catalog.Load(catPath);
        var statics = idx.Entries.Where(e => !e.IsAnimated).ToDictionary(e => e.Index);
        var sheetColour = new Dictionary<int, (int r, int g, int b)?>();

        (int r, int g, int b)? Colour(int sheet)
        {
            if (sheetColour.TryGetValue(sheet, out var cached)) return cached;
            string png = Path.Combine(gfxDir, $"{sheet}.png");
            (int, int, int)? result = null;
            if (File.Exists(png))
            {
                using var bmp = new System.Drawing.Bitmap(png);
                long r = 0, g = 0, b = 0; int n = 0;
                for (int y = 0; y < Math.Min(bmp.Height, 96); y += 3)
                    for (int x = 0; x < Math.Min(bmp.Width, 96); x += 3)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.R <= 8 && c.G <= 8 && c.B <= 8) continue; // black is transparent
                        r += c.R; g += c.G; b += c.B; n++;
                    }
                if (n > 20) result = ((int)(r / n), (int)(g / n), (int)(b / n));
            }
            sheetColour[sheet] = result;
            return result;
        }

        // -- water: animated 32x32 tiles whose sheet reads blue ---------------
        var water = new List<int>();
        foreach (var anim in idx.Entries.Where(e => e.IsAnimated && e.Frames.Length > 0))
        {
            if (!statics.TryGetValue(anim.Frames[0], out var f0)) continue;
            if (f0.Width != 32 || f0.Height != 32) continue;
            var c = Colour(f0.FileNum);
            if (c is null) continue;
            if (c.Value.b > c.Value.r * 1.4 && c.Value.b > 70) water.Add(anim.Index);
        }

        // -- trees: tall green props ------------------------------------------
        var trees = new List<int>();
        foreach (var sheet in cat.Sheets.Where(s => s.Class == GrhClass.Prop && s.Grhs.Count == 1))
        {
            if (!statics.TryGetValue(sheet.Grhs[0], out var e)) continue;
            if (e.Height < e.Width * 1.1 || e.Height < 64) continue;
            var c = Colour(sheet.Sheet);
            if (c is null) continue;
            if (c.Value.g > c.Value.r * 1.15 && c.Value.g > c.Value.b * 1.15) trees.Add(sheet.Grhs[0]);
        }

        // -- UI: the inventory backdrop and selection highlight ---------------
        // Both were fixed 32x32-ish icons; pick stable ones from the item pool
        // so they at least resolve rather than pointing at nothing.
        var uiPool = cat.Sheets.Where(s => s.Class == GrhClass.Item && s.Grhs.Count == 1)
                               .OrderBy(s => s.Sheet).Select(s => s.Grhs[0]).ToList();

        cat.WaterRanges = Compress(water);
        cat.Trees = trees;
        cat.Ui = new Dictionary<string, int>();
        if (uiPool.Count > 1)
        {
            cat.Ui["inventoryBackground"] = uiPool[0];
            cat.Ui["selectionHighlight"] = uiPool[1];
        }
        // The beam mote is a small spark; reuse the first animation for it.
        var firstAnim = idx.Entries.FirstOrDefault(e => e.IsAnimated);
        if (firstAnim is not null) cat.Ui["beamMote"] = firstAnim.Index;

        Console.WriteLine($"agua   : {water.Count} GRH en {cat.WaterRanges.Count} rangos");
        foreach (var r in cat.WaterRanges.Take(8)) Console.WriteLine($"    {r[0]}..{r[1]}");
        Console.WriteLine($"arboles: {trees.Count} GRH");
        Console.WriteLine($"ui     : {string.Join(", ", cat.Ui.Select(k => $"{k.Key}={k.Value}"))}");

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        cat.Save(catPath);
        Console.WriteLine($"\nescrito {catPath}");
        return 0;
    }

    /// <summary>Collapses a sorted index list into inclusive ranges.</summary>
    private static List<int[]> Compress(List<int> values)
    {
        var ranges = new List<int[]>();
        foreach (int v in values.Distinct().OrderBy(v => v))
        {
            if (ranges.Count > 0 && ranges[^1][1] + 1 == v) ranges[^1][1] = v;
            else ranges.Add(new[] { v, v });
        }
        return ranges;
    }

    // ---- fxs ----------------------------------------------------------------

    /// <summary>
    /// Repoints Fxs.ind at animations that exist in the new catalogue, keeping
    /// each effect's offsets. Animacion is a signed Int16, so it cannot address
    /// past 32767 - the remap places all animations in 1..3103 precisely so this
    /// table stays addressable.
    /// </summary>
    private static int FxsCmd(string[] args)
    {
        if (args.Length < 3) return Usage();
        string fxPath = args[1], indPath = args[2];
        bool apply = args.Contains("--apply");

        var idx = GrhIndex.Load(indPath);
        // Only multi-frame entries: a static GRH would show as a frozen effect.
        // Animacion is a signed Int16, so anything above 32767 is unreachable -
        // that excludes the walk animations appended after the remap, which is
        // correct anyway: those belong to bodies, not to spell effects.
        var anims = idx.Entries
            .Where(e => e.IsAnimated && e.NumFrames > 1 && e.Index <= short.MaxValue)
            .Select(e => e.Index).OrderBy(i => i).ToList();
        if (anims.Count == 0) { Console.Error.WriteLine("El indice no tiene animaciones direccionables."); return 1; }
        var animSet = anims.ToHashSet();

        byte[] data = File.ReadAllBytes(fxPath);
        const int MiCabecera = 263, RecordSize = 6;
        short count = BitConverter.ToInt16(data, MiCabecera);

        int repointed = 0;
        for (int i = 0; i < count; i++)
        {
            int off = MiCabecera + 2 + i * RecordSize;
            short anim = BitConverter.ToInt16(data, off);
            if (anim > 0 && animSet.Contains(anim)) continue;

            // Spread the effects across the animation pool rather than piling
            // them onto the first few, so distinct spells stay distinguishable.
            short replacement = (short)anims[(i * anims.Count / Math.Max((int)count, 1)) % anims.Count];
            BitConverter.GetBytes(replacement).CopyTo(data, off);
            repointed++;
        }

        Console.WriteLine($"efectos          : {count}");
        Console.WriteLine($"animaciones libres: {anims.Count}  (rango {anims[0]}..{anims[^1]})");
        Console.WriteLine($"repuntados       : {repointed}");

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        File.Copy(fxPath, fxPath + ".bak", overwrite: true);
        File.WriteAllBytes(fxPath, data);
        Console.WriteLine($"\nescrito {fxPath} (respaldo en {fxPath}.bak)");
        return 0;
    }

    // ---- npcs ---------------------------------------------------------------

    /// <summary>
    /// Clamps NPCs.dat Body and Head into the regenerated tables. These are
    /// indices into Personajes.ind and Cabezas.ind, not GRHs, so most already
    /// land inside the new tables and only the overflow needs moving - which is
    /// why they are wrapped rather than reassigned wholesale.
    /// </summary>
    private static int NpcsCmd(string[] args)
    {
        if (args.Length < 3) return Usage();
        string npcPath = args[1], initDir = args[2];
        bool apply = args.Contains("--apply");

        int numBodies = CountInd(Path.Combine(initDir, "Personajes.ind"));
        int numHeads = CountInd(Path.Combine(initDir, "Cabezas.ind"));
        if (numBodies == 0 || numHeads == 0)
        {
            Console.Error.WriteLine("No se pudieron leer Personajes.ind / Cabezas.ind");
            return 1;
        }

        // The file is Latin-1, not UTF-16 like obj.dat.
        var enc = Encoding.GetEncoding(1252);
        string text = File.ReadAllText(npcPath, enc);

        int bodyFixed = 0, headFixed = 0;
        string Fix(string src, string key, int limit, ref int fixedCount)
        {
            int count = 0;
            // \r before the line end: the file is CRLF, so anchoring with $
            // directly after \d+ would never match.
            string result = System.Text.RegularExpressions.Regex.Replace(src,
                $@"^({key}=)(\d+)\s*$",
                m =>
                {
                    int v = int.Parse(m.Groups[2].Value);
                    if (v <= limit) return m.Value;
                    count++;
                    return m.Groups[1].Value + Wrap(v, limit);
                },
                System.Text.RegularExpressions.RegexOptions.Multiline
                | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            fixedCount = count;
            return result;
        }

        text = Fix(text, "Body", numBodies, ref bodyFixed);
        text = Fix(text, "Head", numHeads, ref headFixed);

        Console.WriteLine($"Personajes.ind: {numBodies} cuerpos -> Body corregidos: {bodyFixed}");
        Console.WriteLine($"Cabezas.ind   : {numHeads} cabezas -> Head corregidos: {headFixed}");

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        File.Copy(npcPath, npcPath + ".bak", overwrite: true);
        File.WriteAllText(npcPath, text, enc);
        Console.WriteLine($"\nescrito {npcPath} (respaldo en {npcPath}.bak)");
        return 0;
    }

    // ---- objdat -------------------------------------------------------------

    /// <summary>
    /// Repoints obj.dat's art fields at the new catalogue. The old values cannot
    /// be translated, since they addressed art that was dropped rather than
    /// renumbered, so each item is reassigned from the pool matching its type.
    ///
    /// Cycling through the pool means items of a type share icons once the pool
    /// is exhausted - wrong in detail, but every item is visible and correctly
    /// categorised, which is the state the manual pass starts from.
    /// </summary>
    private static int ObjDatCmd(string[] args)
    {
        if (args.Length < 4) return Usage();
        string objPath = args[1], catPath = args[2], initDir = args[3];
        bool apply = args.Contains("--apply");

        var cat = Catalog.Load(catPath);
        string text = File.ReadAllText(objPath, Encoding.Unicode);
        var items = ObjDat.Parse(text);

        // Icon pools: single-region sheets of the right class. Weapons and
        // shields keep their 32x32 inventory icons, which is what the 1-region
        // sheets in those blocks are.
        List<int> Pool(params GrhClass[] classes) => cat.Sheets
            .Where(s => classes.Contains(s.Class) && s.Grhs.Count == 1)
            .OrderBy(s => s.Sheet).Select(s => s.Grhs[0]).ToList();

        var iconItem = Pool(GrhClass.Item);
        var iconWeapon = Pool(GrhClass.Weapon);
        var iconShield = Pool(GrhClass.Shield);
        var iconHelmet = Pool(GrhClass.Helmet);

        // Table sizes bound the Anim and NumRopaje indices.
        int numArmas = CountIni(Path.Combine(initDir, "Armas.dat"), "NumArmas");
        int numEscudos = CountIni(Path.Combine(initDir, "Escudos.dat"), "NumEscudos");
        int numBodies = CountInd(Path.Combine(initDir, "Personajes.ind"));

        Console.WriteLine($"items en obj.dat : {items.Count}");
        Console.WriteLine($"iconos disponibles: item={iconItem.Count} arma={iconWeapon.Count} escudo={iconShield.Count} casco={iconHelmet.Count}");
        Console.WriteLine($"tablas           : armas={numArmas} escudos={numEscudos} cuerpos={numBodies}");
        Console.WriteLine();

        var assign = new Dictionary<int, (int grh, int anim, int ropaje)>();
        var counters = new Dictionary<string, int>();
        int Next(List<int> pool, string key)
        {
            if (pool.Count == 0) return 0;
            int i = counters.GetValueOrDefault(key);
            counters[key] = i + 1;
            return pool[i % pool.Count];
        }

        var perType = new Dictionary<int, int>();
        foreach (var item in items)
        {
            var pool = item.ObjType switch
            {
                ObjDat.TypeWeapon => iconWeapon,
                ObjDat.TypeShield => iconShield,
                ObjDat.TypeHelmet => iconHelmet,
                _ => iconItem,
            };
            int grh = Next(pool, item.ObjType.ToString());

            // Anim indexes Armas.dat for weapons and Escudos.dat for shields.
            int anim = 0;
            if (item.HasAnim && item.Anim > 0)
                anim = item.ObjType == ObjDat.TypeShield
                    ? Wrap(item.Anim, numEscudos)
                    : Wrap(item.Anim, numArmas);

            int ropaje = item.HasRopaje && item.NumRopaje > 0 ? Wrap(item.NumRopaje, numBodies) : 0;

            assign[item.Number] = (grh, anim, ropaje);
            perType[item.ObjType] = perType.GetValueOrDefault(item.ObjType) + 1;
        }

        foreach (var (type, count) in perType.OrderByDescending(k => k.Value).Take(8))
            Console.WriteLine($"  ObjType {type,3} : {count,5} items");

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        string updated = ObjDat.Rewrite(text, assign);
        File.Copy(objPath, objPath + ".bak", overwrite: true);
        File.WriteAllText(objPath, updated, Encoding.Unicode);
        Console.WriteLine($"\nescrito {objPath} (respaldo en {objPath}.bak)");
        return 0;
    }

    /// <summary>Keeps an index inside a table, preserving it when already valid.</summary>
    private static int Wrap(int value, int count)
        => count <= 0 ? 0 : (value <= count ? value : (value - 1) % count + 1);

    private static int CountIni(string path, string key)
    {
        if (!File.Exists(path)) return 0;
        foreach (string line in File.ReadLines(path))
            if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(line[(key.Length + 1)..].Trim(), out int v) ? v : 0;
        return 0;
    }

    private static int CountInd(string path)
        => File.Exists(path) ? BitConverter.ToInt16(File.ReadAllBytes(path), 263) : 0;

    // ---- entities -----------------------------------------------------------

    /// <summary>
    /// Builds Personajes.ind, Cabezas.ind and Cascos.ind from the classified
    /// catalogue, appending the directional walk animations each body needs.
    ///
    /// Bodies are the reason this is not a mechanical remap: a body sheet holds
    /// 22 static frames laid out 6/6/5/5, and the game wants one animated GRH
    /// per facing, so the animations are synthesised here rather than found.
    /// </summary>
    private static int EntitiesCmd(string[] args)
    {
        if (args.Length < 4) return Usage();
        string indPath = args[1], catPath = args[2], initDir = args[3];
        bool apply = args.Contains("--apply");

        var idx = GrhIndex.Load(indPath);
        var cat = Catalog.Load(catPath);
        var statics = idx.Entries.Where(e => !e.IsAnimated).ToDictionary(e => e.Index);

        int nextGrh = idx.Entries.Max(e => e.Index) + 1;
        var newAnims = new List<GrhEntry>();

        // -- bodies, weapons and shields --------------------------------------
        // All three use the same 150x180 layout of 22 frames laid out 6/6/5/5,
        // and all three need one animated GRH per facing. Weapons and shields
        // are overlays drawn on top of a body, so they animate in step with it.
        (List<int[]> dirs, List<int> heights, int skipped) BuildWalkers(GrhClass cls)
        {
            var result = new List<int[]>();
            var heights = new List<int>();
            int bad = 0;
            foreach (var sheet in cat.Sheets.Where(s => s.Class == cls).OrderBy(s => s.Sheet))
            {
                // Frames come out in reading order, matching how the rows were written.
                var frames = sheet.Grhs.Where(statics.ContainsKey)
                                       .OrderBy(g => statics[g].SY).ThenBy(g => statics[g].SX)
                                       .ToList();
                if (frames.Count != EntityTables.BodyRowLengths.Sum()) { bad++; continue; }

                var dirs = new int[4];
                int cursor = 0;
                for (int row = 0; row < EntityTables.BodyRowLengths.Length; row++)
                {
                    var rowFrames = frames.Skip(cursor).Take(EntityTables.BodyRowLengths[row]).ToArray();
                    cursor += rowFrames.Length;
                    newAnims.Add(new GrhEntry
                    {
                        Index = nextGrh,
                        NumFrames = (short)rowFrames.Length,
                        Frames = rowFrames,
                        Speed = (float)Math.Round(rowFrames.Length * EntityTables.MsPerWalkFrame),
                    });
                    dirs[row] = nextGrh++;
                }
                result.Add(dirs);
                heights.Add(statics[frames[0]].Height);
            }
            return (result, heights, bad);
        }

        var (bodyDirs, bodyHeights, bodySkipped) = BuildWalkers(GrhClass.Body);
        var bodies = bodyDirs
            .Select((d, i) => (d, (short)0, EntityTables.HeadOffsetFor(bodyHeights[i])))
            .ToList();

        var (weapons, _, weaponSkipped) = BuildWalkers(GrhClass.Weapon);
        var (shields, _, shieldSkipped) = BuildWalkers(GrhClass.Shield);

        // -- heads and helmets ------------------------------------------------
        // These are static strips: one pose per facing, no animation involved.
        List<int[]> BuildStrips(GrhClass cls, out int skipped)
        {
            var result = new List<int[]>();
            int bad = 0;
            foreach (var sheet in cat.Sheets.Where(s => s.Class == cls).OrderBy(s => s.Sheet))
            {
                var poses = sheet.Grhs.Where(statics.ContainsKey)
                                      .OrderBy(g => statics[g].SX).ToList();
                if (poses.Count < 4) { bad++; continue; }
                result.Add(new[] { poses[0], poses[1], poses[2], poses[3] });
            }
            skipped = bad;
            return result;
        }

        var heads = BuildStrips(GrhClass.Head, out int headSkipped);
        var helmets = BuildStrips(GrhClass.Helmet, out int helmetSkipped);

        Console.WriteLine($"cuerpos : {bodies.Count,5}  (salteados {bodySkipped})");
        Console.WriteLine($"cabezas : {heads.Count,5}  (salteados {headSkipped})");
        Console.WriteLine($"cascos  : {helmets.Count,5}  (salteados {helmetSkipped})");
        Console.WriteLine($"armas   : {weapons.Count,5}  (salteados {weaponSkipped})");
        Console.WriteLine($"escudos : {shields.Count,5}  (salteados {shieldSkipped})");
        Console.WriteLine($"animaciones nuevas: {newAnims.Count}  (GRH {(newAnims.Count > 0 ? newAnims[0].Index : 0)}..{nextGrh - 1})");

        int maxGrh = nextGrh - 1;
        Console.WriteLine($"indice maximo     : {maxGrh}  {(maxGrh <= 65535 ? "OK" : "EXCEDE EL TECHO UInt16")}");
        if (maxGrh > 65535)
        {
            Console.Error.WriteLine("\nABORTA: las animaciones de cuerpo empujan el indice sobre 65535.");
            return 1;
        }

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        File.Copy(indPath, indPath + ".bak", overwrite: true);
        idx.Entries.AddRange(newAnims);
        idx.Count = maxGrh;
        idx.Save(indPath);

        foreach (var (name, write) in new (string, Action)[]
        {
            ("Personajes.ind", () => EntityTables.WritePersonajes(Path.Combine(initDir, "Personajes.ind"), bodies)),
            ("Cabezas.ind",    () => EntityTables.WriteHeadTable(Path.Combine(initDir, "Cabezas.ind"), heads)),
            ("Cascos.ind",     () => EntityTables.WriteHeadTable(Path.Combine(initDir, "Cascos.ind"), helmets)),
            ("Armas.dat",      () => EntityTables.WriteEquipIni(Path.Combine(initDir, "Armas.dat"), "NumArmas", "Arma", weapons)),
            ("Escudos.dat",    () => EntityTables.WriteEquipIni(Path.Combine(initDir, "Escudos.dat"), "NumEscudos", "ESC", shields)),
        })
        {
            string target = Path.Combine(initDir, name);
            if (File.Exists(target)) File.Copy(target, target + ".bak", overwrite: true);
            write();
            Console.WriteLine($"escrito {target}");
        }
        return 0;
    }

    // ---- thin ---------------------------------------------------------------

    /// <summary>
    /// Caps how many 32px cells a single terrain sheet contributes. A 2048x2048
    /// water or lava texture yields 4096 near-identical cells; 157 such sheets
    /// account for 46140 GRHs, which alone pushes the compacted catalogue past
    /// the 65535 ceiling. Keeping an evenly-spaced sample preserves the tiling
    /// variety the editor needs at a fraction of the index cost.
    /// </summary>
    private static int ThinCmd(string[] args)
    {
        if (args.Length < 2) return Usage();
        string indPath = args[1];
        bool apply = args.Contains("--apply");
        int cap = 64;
        for (int i = 2; i < args.Length - 1; i++)
            if (args[i] == "--cap") cap = int.Parse(args[i + 1]);

        var idx = GrhIndex.Load(indPath);

        // Frames referenced by an animation must survive regardless of sampling.
        var referenced = idx.Entries.Where(e => e.IsAnimated)
            .SelectMany(e => e.Frames).ToHashSet();

        var bySheet = idx.Entries.Where(e => !e.IsAnimated && e.FileNum >= NewArtSheetBase)
            .GroupBy(e => e.FileNum)
            .Where(g => g.Count() > cap)
            .ToList();

        var drop = new HashSet<int>();
        foreach (var g in bySheet)
        {
            var cells = g.OrderBy(e => e.SY).ThenBy(e => e.SX).ToList();
            // Evenly spaced sample, so the kept cells span the whole texture
            // rather than clustering in one corner.
            double step = (double)cells.Count / cap;
            var keep = new HashSet<int>();
            for (int i = 0; i < cap; i++) keep.Add(cells[(int)(i * step)].Index);
            foreach (var c in cells)
                if (!keep.Contains(c.Index) && !referenced.Contains(c.Index)) drop.Add(c.Index);
        }

        Console.WriteLine($"laminas por encima de {cap} celdas : {bySheet.Count}");
        Console.WriteLine($"GRH a descartar                   : {drop.Count}");
        Console.WriteLine($"entradas restantes                : {idx.Entries.Count - drop.Count}");

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        File.Copy(indPath, indPath + ".bak", overwrite: true);
        idx.Entries.RemoveAll(e => drop.Contains(e.Index));
        idx.Count = idx.Entries.Count > 0 ? idx.Entries.Max(e => e.Index) : 0;
        idx.Save(indPath);
        Console.WriteLine($"\nescrito {indPath} (respaldo en {indPath}.bak)");
        return 0;
    }

    // ---- remap ------------------------------------------------------------

    /// <summary>
    /// Drops every GRH that points at legacy art and renumbers what remains from
    /// 1, contiguously. Emits the old-to-new map, which is what makes the later
    /// phases reversible: with it plus the .bak, any table can be rebuilt without
    /// reindexing anything.
    /// </summary>
    private static int RemapCmd(string[] args)
    {
        if (args.Length < 3) return Usage();
        string indPath = args[1], mapPath = args[2];
        bool apply = args.Contains("--apply");

        var idx = GrhIndex.Load(indPath);
        var statics = idx.Entries.Where(e => !e.IsAnimated).ToDictionary(e => e.Index);

        // An animation belongs to the new catalogue when its frames do.
        bool IsNewArt(GrhEntry e) => e.IsAnimated
            ? e.Frames.Length > 0 && statics.TryGetValue(e.Frames[0], out var f) && f.FileNum >= NewArtSheetBase
            : e.FileNum >= NewArtSheetBase;

        var keep = idx.Entries.Where(IsNewArt).ToList();
        var dropped = idx.Entries.Count - keep.Count;

        // Animations are placed first, below 32767, because Fxs.ind stores its
        // Animacion field as a signed Int16 and would silently wrap above that.
        // Everything else follows; ordering within each group stays by original
        // index so the result is deterministic and diffable.
        keep = keep.OrderByDescending(e => e.IsAnimated).ThenBy(e => e.Index).ToList();

        var map = new Dictionary<int, int>(keep.Count);
        for (int i = 0; i < keep.Count; i++) map[keep[i].Index] = i + 1;

        int animCount = keep.Count(e => e.IsAnimated);

        // An animation whose frames did not all survive would render as a hole.
        var broken = new List<int>();
        foreach (var e in keep.Where(e => e.IsAnimated))
            if (e.Frames.Any(f => !map.ContainsKey(f))) broken.Add(e.Index);

        Console.WriteLine($"entradas totales : {idx.Entries.Count}");
        Console.WriteLine($"  conservadas    : {keep.Count}  (arte nuevo)");
        Console.WriteLine($"  descartadas    : {dropped}  (arte viejo)");
        Console.WriteLine($"rango nuevo      : 1..{keep.Count}");
        Console.WriteLine($"animaciones rotas: {broken.Count}");
        foreach (int b in broken.Take(5)) Console.WriteLine($"    grh {b}");

        Console.WriteLine($"animaciones      : {animCount}  (renumeradas en 1..{animCount})");
        Console.WriteLine();
        Console.WriteLine($"techo 65535 (Personajes/Cabezas/Cascos): {(keep.Count <= 65535 ? "OK" : "EXCEDIDO")}");
        Console.WriteLine($"techo 32767 (Fxs.ind, animaciones)     : {(animCount <= 32767 ? "OK" : "EXCEDIDO")}");

        if (broken.Count > 0)
        {
            Console.Error.WriteLine("\nABORTA: hay animaciones con cuadros descartados.");
            return 1;
        }
        if (keep.Count > 65535)
        {
            Console.Error.WriteLine("\nABORTA: el catalogo compactado excede el techo UInt16.");
            return 1;
        }
        if (animCount > 32767)
        {
            Console.Error.WriteLine("\nABORTA: hay mas animaciones que el Int16 con signo de Fxs.ind.");
            return 1;
        }

        if (!apply) { Console.WriteLine("\n(dry-run - pasar --apply para escribir)"); return 0; }

        foreach (var e in keep)
        {
            e.Index = map[e.Index];
            if (e.IsAnimated)
                for (int i = 0; i < e.Frames.Length; i++) e.Frames[i] = map[e.Frames[i]];
        }
        keep.Sort((a, b) => a.Index.CompareTo(b.Index));

        File.Copy(indPath, indPath + ".bak", overwrite: true);
        idx.Entries = keep;
        idx.Count = keep.Count;
        idx.Save(indPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(mapPath))!);
        File.WriteAllText(mapPath, JsonSerializer.Serialize(
            map.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value), Json));

        Console.WriteLine($"\nescrito {indPath} (respaldo en {indPath}.bak)");
        Console.WriteLine($"mapa old->new en {mapPath}");
        return 0;
    }

    // ---- classify ---------------------------------------------------------

    private static int ClassifyCmd(string[] args)
    {
        if (args.Length < 3) return Usage();
        var idx = GrhIndex.Load(args[1]);

        var bySheet = new Dictionary<int, List<GrhEntry>>();
        foreach (var e in idx.Entries)
        {
            if (e.IsAnimated || e.FileNum < NewArtSheetBase) continue;
            if (!bySheet.TryGetValue(e.FileNum, out var list))
                bySheet[e.FileNum] = list = new List<GrhEntry>();
            list.Add(e);
        }

        // An animated GRH marks its sheet as carrying effects/animation.
        var animSheets = new HashSet<int>();
        var staticByIndex = idx.Entries.Where(e => !e.IsAnimated).ToDictionary(e => e.Index);
        foreach (var a in idx.Entries.Where(e => e.IsAnimated))
            if (a.Frames.Length > 0 && staticByIndex.TryGetValue(a.Frames[0], out var f0) && f0.FileNum >= NewArtSheetBase)
                animSheets.Add(f0.FileNum);

        var cat = new Catalog { Generated = DateTime.UtcNow.ToString("o") };
        foreach (var (sheet, entries) in bySheet.OrderBy(kv => kv.Key))
            cat.Sheets.Add(Catalog.Classify(sheet, entries));

        cat.Save(args[2]);

        Console.WriteLine($"laminas clasificadas: {cat.Sheets.Count} -> {args[2]}");
        Console.WriteLine($"  con animacion     : {animSheets.Count}");
        Console.WriteLine();
        foreach (var g in cat.Sheets.GroupBy(s => s.Class).OrderByDescending(g => g.Count()))
        {
            int high = g.Count(s => s.Confidence == "high");
            Console.WriteLine($"  {g.Key,-10} {g.Count(),5} laminas   ({high} alta confianza)");
        }
        return 0;
    }
}

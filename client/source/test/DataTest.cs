using System;
using System.IO;
using ArgentumNextgen.Data;

namespace ArgentumNextgen;

/// <summary>
/// Standalone test for data loaders - run via: dotnet run
/// Validates that binary files parse correctly against actual game data.
/// </summary>
public static class DataTest
{
    public static void Main(string[] args)
    {
        // Resolve data path relative to this exe or use arg
        string basePath = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "..", "Data");
        string dataPath = Path.GetFullPath(basePath);
        string initPath = Path.Combine(dataPath, "INIT");
        string mapsPath = Path.Combine(dataPath, "Maps");
        string graficosPath = Path.Combine(dataPath, "Graficos");

        Console.WriteLine("=== Argentum Nextgen — Data Loader Test ===\n");

        int errors = 0;

        // Test 1: GRH Loader
        errors += TestGrh(Path.Combine(initPath, "Graficos.ind"));

        // Test 2: Bodies
        errors += TestBodies(Path.Combine(initPath, "Personajes.ind"));

        // Test 3: Heads
        errors += TestHeads(Path.Combine(initPath, "Cabezas.ind"));

        // Test 4: Cascos
        errors += TestCascos(Path.Combine(initPath, "Cascos.ind"));

        // Test 5: FX
        errors += TestFx(Path.Combine(initPath, "Fxs.ind"));

        // Test 6: Map loading
        errors += TestMap(mapsPath, 1);

        // Test 7: Texture files exist
        errors += TestTextures(graficosPath);

        Console.WriteLine($"\n=== RESULT: {(errors == 0 ? "ALL PASSED" : $"{errors} ERRORS")} ===");
        Environment.Exit(errors > 0 ? 1 : 0);
    }

    static int TestGrh(string path)
    {
        Console.Write("[TEST] GrhLoader (Graficos.ind)... ");
        try
        {
            var grhs = GrhLoader.Load(path);
            int staticCount = 0, animCount = 0;
            for (int i = 1; i < grhs.Length; i++)
            {
                if (grhs[i].NumFrames == 1 && grhs[i].FileNum > 0) staticCount++;
                else if (grhs[i].NumFrames > 1) animCount++;
            }

            Console.WriteLine($"OK — {grhs.Length} slots, {staticCount} static, {animCount} animated");

            // Validate some entries have reasonable values
            if (staticCount < 100)
            {
                Console.WriteLine($"  [WARN] Only {staticCount} static GRHs — expected thousands");
                return 1;
            }

            // Sample: print first 5 non-empty static GRHs
            int printed = 0;
            for (int i = 1; i < grhs.Length && printed < 5; i++)
            {
                if (grhs[i].NumFrames == 1 && grhs[i].FileNum > 0)
                {
                    Console.WriteLine($"  GRH[{i}]: File={grhs[i].FileNum} Pos=({grhs[i].SX},{grhs[i].SY}) Size={grhs[i].PixelWidth}x{grhs[i].PixelHeight}");
                    printed++;
                }
            }

            // Sample: print first 3 animations
            printed = 0;
            for (int i = 1; i < grhs.Length && printed < 3; i++)
            {
                if (grhs[i].NumFrames > 1 && grhs[i].Frames != null)
                {
                    Console.WriteLine($"  GRH[{i}]: Anim {grhs[i].NumFrames} frames, speed={grhs[i].Speed}, frames=[{string.Join(",", grhs[i].Frames[..Math.Min(4, grhs[i].Frames.Length)])}...]");
                    printed++;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }

    static int TestBodies(string path)
    {
        Console.Write("[TEST] BodyLoader (Personajes.ind)... ");
        try
        {
            var bodies = BodyLoader.LoadBodies(path);
            int valid = 0;
            for (int i = 1; i < bodies.Length; i++)
                if (bodies[i].Walk[1] != 0 || bodies[i].Walk[3] != 0) valid++;

            Console.WriteLine($"OK — {bodies.Length - 1} entries, {valid} with walk anims");

            // Sample
            for (int i = 1; i < bodies.Length && i <= 3; i++)
            {
                Console.WriteLine($"  Body[{i}]: Walk N={bodies[i].Walk[1]} E={bodies[i].Walk[2]} S={bodies[i].Walk[3]} W={bodies[i].Walk[4]} HeadOff=({bodies[i].HeadOffsetX},{bodies[i].HeadOffsetY})");
            }

            if (valid < 5)
            {
                Console.WriteLine($"  [WARN] Only {valid} valid bodies — expected dozens");
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }

    static int TestHeads(string path)
    {
        Console.Write("[TEST] BodyLoader.LoadHeads (Cabezas.ind)... ");
        try
        {
            var heads = BodyLoader.LoadHeads(path);
            int valid = 0;
            for (int i = 1; i < heads.Length; i++)
                if (heads[i].Head[1] != 0 || heads[i].Head[3] != 0) valid++;

            Console.WriteLine($"OK — {heads.Length - 1} entries, {valid} with head GRHs");

            for (int i = 1; i < heads.Length && i <= 3; i++)
            {
                Console.WriteLine($"  Head[{i}]: N={heads[i].Head[1]} E={heads[i].Head[2]} S={heads[i].Head[3]} W={heads[i].Head[4]}");
            }

            if (valid < 5)
            {
                Console.WriteLine($"  [WARN] Only {valid} valid heads");
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }

    static int TestCascos(string path)
    {
        Console.Write("[TEST] BodyLoader.LoadCascos (Cascos.ind)... ");
        try
        {
            var cascos = BodyLoader.LoadCascos(path);
            int valid = 0;
            for (int i = 1; i < cascos.Length; i++)
                if (cascos[i].Head[1] != 0 || cascos[i].Head[3] != 0) valid++;

            Console.WriteLine($"OK — {cascos.Length - 1} entries, {valid} with casco GRHs");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }

    static int TestFx(string path)
    {
        Console.Write("[TEST] FxLoader (Fxs.ind)... ");
        try
        {
            var fxs = FxLoader.Load(path);
            int valid = 0;
            for (int i = 1; i < fxs.Length; i++)
                if (fxs[i].Animacion != 0) valid++;

            Console.WriteLine($"OK — {fxs.Length - 1} entries, {valid} with animations");

            for (int i = 1; i < fxs.Length && i <= 3; i++)
            {
                Console.WriteLine($"  FX[{i}]: Anim={fxs[i].Animacion} Offset=({fxs[i].OffsetX},{fxs[i].OffsetY})");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }

    static int TestMap(string mapsPath, int mapNum)
    {
        Console.Write($"[TEST] MapLoader (Mapa{mapNum})... ");
        try
        {
            var mapData = MapLoader.Load(mapsPath, mapNum);

            int blockedCount = 0, layer2Count = 0, layer3Count = 0, layer4Count = 0;
            int triggerCount = 0, exitCount = 0, npcCount = 0, objCount = 0;
            int emptyLayer1 = 0;

            for (int y = 1; y <= 100; y++)
            {
                for (int x = 1; x <= 100; x++)
                {
                    ref var t = ref mapData.Tiles[x, y];
                    if (t.Blocked) blockedCount++;
                    if (t.Layer1 == 0) emptyLayer1++;
                    if (t.Layer2 != 0) layer2Count++;
                    if (t.Layer3 != 0) layer3Count++;
                    if (t.Layer4 != 0) layer4Count++;
                    if (t.Trigger != 0) triggerCount++;
                    if (t.ExitMap != 0) exitCount++;
                    if (t.NpcIndex != 0) npcCount++;
                    if (t.ObjIndex != 0) objCount++;
                }
            }

            Console.WriteLine("OK");
            Console.WriteLine($"  Blocked: {blockedCount}, Layer2: {layer2Count}, Layer3: {layer3Count}, Layer4: {layer4Count}");
            Console.WriteLine($"  Triggers: {triggerCount}, Exits: {exitCount}, NPCs: {npcCount}, Objects: {objCount}");
            Console.WriteLine($"  Empty Layer1: {emptyLayer1}/10000");

            // Sanity: Layer 1 should be mostly filled
            if (emptyLayer1 > 9000)
            {
                Console.WriteLine("  [FAIL] Almost all Layer1 tiles are empty — parsing likely wrong");
                return 1;
            }

            // Sample tiles
            for (int y = 50; y <= 52; y++)
            {
                for (int x = 50; x <= 52; x++)
                {
                    ref var t = ref mapData.Tiles[x, y];
                    Console.WriteLine($"  Tile({x},{y}): L1={t.Layer1} L2={t.Layer2} L3={t.Layer3} L4={t.Layer4} Blk={t.Blocked} Trg={t.Trigger}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }

    static int TestTextures(string graficosPath)
    {
        Console.Write("[TEST] Texture files (Graficos/)... ");
        try
        {
            var files = Directory.GetFiles(graficosPath, "*.png");
            Console.WriteLine($"OK — {files.Length} PNG files");

            // Check specific files exist (1.png is always needed)
            if (!File.Exists(Path.Combine(graficosPath, "1.png")))
            {
                Console.WriteLine("  [FAIL] 1.png missing!");
                return 1;
            }
            // Check a range
            int found = 0;
            for (int i = 1; i <= 100; i++)
            {
                if (File.Exists(Path.Combine(graficosPath, $"{i}.png")))
                    found++;
            }
            Console.WriteLine($"  Files 1-100: {found}/100 present");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
            return 1;
        }
    }
}

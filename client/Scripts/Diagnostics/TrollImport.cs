using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Diagnostics;

// Explicit offline asset importer. Never executed by normal client startup.
public partial class TrollImport : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private static void Run()
    {
        string root = ProjectSettings.GlobalizePath("res://../");
        bool boss = OS.GetCmdlineUserArgs().Contains("--boss");
        bool refresh = OS.GetCmdlineUserArgs().Contains("--refresh-art");
        string art = Path.Combine(root, boss ? "resources/art-source/troll-rey" : "resources/art-source/troll-coloso");
        string init = Path.Combine(root, "resources/data/INIT");
        var provider = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
        var grhs = GrhLoader.Load(provider);
        var bodies = BodyLoader.LoadBodies(provider);
        int texture = boss ? 45014 : 45013, npc = boss ? 973 : 972;
        int cellW = boss ? 256 : 96, cellH = boss ? 352 : 128;
        string texturePath = Path.Combine(root, $"resources/data/Graficos/{texture}.png");
        string grhPath = Path.Combine(init, "Graficos.ind"), bodyPath = Path.Combine(init, "Personajes.ind");
        byte[] gb = File.ReadAllBytes(grhPath), bb = File.ReadAllBytes(bodyPath);
        if (!provider.ReadBytes("INIT/Graficos.ind").SequenceEqual(gb) ||
            !provider.ReadBytes("INIT/Personajes.ind").SequenceEqual(bb))
            throw new Exception("Active overrides differ from resource catalogs");
        // Body table is still UInt16 in this checkout. Use a verified free
        // contiguous range rather than widening unrelated existing tables.
        int first = 0;
        for (int candidate = 33600; candidate + 35 <= ushort.MaxValue; candidate++)
            if (Enumerable.Range(candidate, 36).All(i => i >= grhs.Length || grhs[i].NumFrames == 0))
            { first = candidate; break; }
        if (first == 0) throw new Exception("No free 36-GRH range");
        int body = bodies.Length;
        bool wide = BitConverter.ToInt32(bb, 263) == -32000;
        int count = wide ? BitConverter.ToInt32(bb, 267) : BitConverter.ToInt16(bb, 263);
        if (count + 1 != body || bb.Length != (wide ? 271 : 265) + count * (wide ? 20 : 12))
            throw new Exception("Unexpected body table layout");
        if (!wide && first + 35 > ushort.MaxValue) throw new Exception("Legacy body GRH capacity exceeded");
        string[] paths = { "server/dat/NPCs-HOSTILES.dat", "resources/data/INIT/NPCs.dat", "client/Data/INIT/NPCs.dat" };
        string[] texts = paths.Select(p => File.ReadAllText(Path.Combine(root, p), Encoding.Latin1)).ToArray();
        if (refresh && (!boss || bodies.Length <= 522 || grhs[bodies[522].Walk[3]].Frames is not { Length: 8 } frames || grhs[frames[0]].FileNum != texture))
            throw new Exception("Refresh ownership mismatch");
        if (!refresh && (File.Exists(texturePath) || texts.Any(t => t.Contains($"[NPC{npc}]", StringComparison.OrdinalIgnoreCase))))
            throw new Exception("Troll already imported or IDs occupied; refusing overwrite");

        using var source = Image.LoadFromFile(Path.Combine(art, "source.png"));
        if (source.GetWidth() < 1024 || source.GetHeight() < 1024) throw new Exception("Source atlas too small");
        // Same black color key as the production texture loader; nearest scaling
        // retains crisp pixels and a single scale prevents animation breathing.
        source.Convert(Image.Format.Rgba8);
        for (int y = 0; y < source.GetHeight(); y++)
        for (int x = 0; x < source.GetWidth(); x++)
        {
            var c = source.GetPixel(x, y);
            if (c.A < .12f || (c.R <= 3f / 255 && c.G <= 3f / 255 && c.B <= 3f / 255)) source.SetPixel(x, y, Colors.Transparent);
        }
        using var atlas = Image.CreateEmpty(cellW * 8, cellH * 4, false, Image.Format.Rgba8);
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 8; col++)
        {
            int sx = col * source.GetWidth() / 8, sy = row * source.GetHeight() / 4;
            using var cell = source.GetRegion(new Rect2I(sx, sy,
                (col + 1) * source.GetWidth() / 8 - sx, (row + 1) * source.GetHeight() / 4 - sy));
            int width = boss ? 214 : 86, height = boss ? 320 : 115;
            cell.Resize(width, height, Image.Interpolation.Nearest);
            var bounds = cell.GetUsedRect();
            if (bounds.Size.Y < (boss ? 240 : 85) || bounds.Size.Y > (boss ? 320 : 110)) throw new Exception("Bad sprite bounds/background");
            int dy = cellH - 4 - bounds.End.Y;
            atlas.BlitRect(cell, new Rect2I(0, 0, width, height), new Vector2I(col * cellW + (cellW - width) / 2, row * cellH + dy));
        }
        if (atlas.SavePng(texturePath) != Error.Ok) throw new Exception("Cannot save atlas");
        if (refresh) { GD.Print("[TROLL] Art refreshed; catalogs preserved."); return; }
        using (var w = new BinaryWriter(File.Open(grhPath, FileMode.Append)))
        {
            for (int i = 0; i < 32; i++)
            {
                w.Write(first + i); w.Write((short)1); w.Write(texture);
                w.Write((short)(i % 8 * cellW)); w.Write((short)(i / 8 * cellH));
                w.Write((short)cellW); w.Write((short)cellH);
            }
            for (int d = 0; d < 4; d++)
            {
                w.Write(first + 32 + d); w.Write((short)8);
                for (int f = 0; f < 8; f++) w.Write(first + d * 8 + f);
                w.Write(boss ? 1100f : 800f);
            }
        }
        using (var w = new BinaryWriter(File.Open(bodyPath, FileMode.Open)))
        {
            w.BaseStream.Position = wide ? 267 : 263;
            if (wide) w.Write(body); else w.Write((short)body);
            w.BaseStream.Position = w.BaseStream.Length;
            for (int d = 0; d < 4; d++)
                if (wide) w.Write(first + 32 + d); else w.Write((ushort)(first + 32 + d));
            w.Write((short)0); w.Write((short)-100);
        }
        string entry = $"\r\n[NPC{npc}]\r\nName=Troll Coloso de la Cienaga\r\nDesc=Una bestia de piedra y musgo que aplasta a los intrusos con su garrote.\r\nBody={body}\r\nHead=0\r\nHeading=3\r\nNpcType=0\r\nMovement=3\r\nHostile=1\r\nAttackable=1\r\nReSpawn=1\r\nDomable=0\r\nAlineacion=2\r\nMinHP=8500\r\nMaxHP=8500\r\nMinHIT=90\r\nMaxHIT=135\r\nDEF=35\r\nDEFm=15\r\nPoderAtaque=220\r\nPoderEvasion=65\r\nGiveEXP=14000\r\nGiveGLDMin=650\r\nGiveGLDMax=1100\r\nLanzaSpells=0\r\nNROITEMS=0\r\nAguaValida=0\r\nTierraInvalida=0\r\n";
        if (boss)
            entry = entry.Replace("Troll Coloso de la Cienaga", "Gorath, Rey de la Cienaga")
                .Replace("MinHP=8500", "MinHP=95000").Replace("MaxHP=8500", "MaxHP=95000")
                .Replace("MinHIT=90", "MinHIT=150").Replace("MaxHIT=135", "MaxHIT=220")
                .Replace("DEF=35", "DEF=55").Replace("DEFm=15", "DEFm=30")
                .Replace("PoderAtaque=220", "PoderAtaque=320")
                .Replace("GiveEXP=14000", "GiveEXP=120000")
                .Replace("GiveGLDMin=650", "GiveGLDMin=6000").Replace("GiveGLDMax=1100", "GiveGLDMax=10000");
        for (int i = 0; i < paths.Length; i++)
        {
            string t = Regex.Replace(texts[i], @"(?im)^(NumNPCs\s*=\s*)(\d+)", m => m.Groups[1].Value + Math.Max(npc, int.Parse(m.Groups[2].Value)));
            File.WriteAllText(Path.Combine(root, paths[i]), t.TrimEnd() + "\r\n" + entry, Encoding.Latin1);
        }
        var loaded = GrhLoader.Load(provider);
        var loadedBodies = BodyLoader.LoadBodies(provider);
        for (int d = 0; d < 4; d++)
        {
            int g = loadedBodies[body].Walk[d + 1];
            if (g != first + 32 + d || loaded[g].NumFrames != 8) throw new Exception("Animation roundtrip failed");
            for (int f = 0; f < 8; f++)
                if (loaded[loaded[g].Frames![f]].FileNum != texture) throw new Exception("Wrong texture reference");
        }
        File.WriteAllText(Path.Combine(art, "registration.txt"), $"NPC={npc}\nBody={body}\nTexture={texture}\nFirstGrh={first}\nLastGrh={first + 35}\nCell={cellW}x{cellH}\nFrames=8x4\nCycleMs={(boss ? 1100 : 800)}\n");
        GD.Print($"[TROLL] PASS: NPC {npc}, body {body}, GRH {first}..{first + 35}, 32 frames, hostile catalogs synchronized.");
    }
}

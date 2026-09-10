using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline, explicitly invoked art import and equipment compatibility preview.</summary>
public partial class ForgeFrostSmoke : Node2D
{
    private static readonly string[] Keys = { "volcanic-armor", "glacial-armor", "volcanic-shield", "glacial-shield", "volcanic-helmet", "glacial-helmet" };
    private static readonly string[] Names = { "Armadura del Juramento Volcanico", "Armadura del Centinela Glacial", "Escudo de la Caldera", "Escudo de la Estrella Boreal", "Yelmo de la Forja Negra", "Yelmo de la Corona Invernal" };
    private static readonly string[] ObjectPaths = { "server/dat/obj.dat", "resources/data/INIT/obj.dat", "client/Data/INIT/obj.dat" };
    private readonly GameData _data = new();
    private readonly GameState _state = new();
    private readonly GrhAnimator _animator = new();
    private string _root = "", _art = "";
    private bool _ready;
    private bool _idle;
    private int _frame;
    private static int Start(int item) => 33572 + item * 40;
    private static int Columns(int item) => item < 2 ? 8 : item < 4 ? 4 : 2;
    private static int CellSize(int item) => item < 4 ? 64 : 32;
    private static int Icon(int item) => Start(item) + (item < 2 ? 36 : item < 4 ? 20 : 4);

    public override async void _Ready()
    {
        try
        {
            _root = ProjectSettings.GlobalizePath("res://../");
            _art = Path.Combine(_root, "resources/art-source/forge-and-frost");
            var args = OS.GetCmdlineUserArgs();
            _idle = Array.IndexOf(args, "--idle") >= 0;
            if (Array.IndexOf(args, "--import") >= 0) Import();
            if (Array.IndexOf(args, "--refresh-art") >= 0)
            {
                var active = GrhLoader.Load(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
                for (int i = 0; i < 6; i++)
                    if (active[Start(i)].FileNum != 45007 + i) throw new Exception("Art ownership mismatch");
                BuildAtlases();
                // A front resting pose is the only armor frame replaced by this
                // adjustment. Keep all other walking views byte-for-byte intact.
                for (int item = 0; item < 2; item++)
                {
                    using var before = Image.LoadFromFile(TexturePath(item));
                    using var after = Image.LoadFromFile(AtlasPath(item));
                    for (int f = 0; f < 32; f++)
                    {
                        if (f == 16) continue;
                        var rect = new Rect2I(f % 8 * 64, f / 8 * 64, 64, 64);
                        using var oldFrame = before.GetRegion(rect);
                        using var newFrame = after.GetRegion(rect);
                        if (!oldFrame.GetData().SequenceEqual(newFrame.GetData())) throw new Exception("Unrelated walking frame changed");
                    }
                }
                for (int i = 0; i < 6; i++) File.Copy(AtlasPath(i), TexturePath(i), true);
            }
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            Validate();
            _state.Config.ShowNames = false; _state.Config.ShowShadows = false;
            _ready = true;
            for (_frame = 0; _frame < 8; _frame++)
            {
                QueueRedraw();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var screen = GetViewport().GetTexture().GetImage();
                using var capture = screen.GetRegion(new Rect2I(0, 0, 800, 600));
                if (capture.SavePng(Path.Combine(_art, _idle ? "idle-preview.png" : $"preview-{_frame}.png")) != Error.Ok) throw new Exception("Cannot save preview");
                if (_idle) break;
            }
            GD.Print("[FORGE-FROST] PASS: six items 1677-1682; 64 armor frames, 32 shield frames, 8 helmet views; catalogs and movement verified.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private string AtlasPath(int i) => Path.Combine(_art, Keys[i] + "-atlas.png");
    private string TexturePath(int i) => Path.Combine(_root, $"resources/data/Graficos/{45007 + i}.png");
    private void Import()
    {
        string init = Path.Combine(_root, "resources/data/INIT");
        var provider = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
        var grhs = GrhLoader.Load(provider);
        for (int id = Start(0); id < Start(6); id++)
            if (id < grhs.Length && grhs[id].NumFrames > 0) throw new Exception("GRH collision: " + id);
        foreach (var table in new[] { ("Personajes.ind", 518, 12), ("Cascos.ind", 112, 8) })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(init, table.Item1));
            if (bytes.Length != 265 + table.Item2 * table.Item3 || BitConverter.ToInt16(bytes, 263) != table.Item2)
                throw new Exception("Unexpected table: " + table.Item1);
        }
        foreach (string file in new[] { "Graficos.ind", "Personajes.ind", "Cascos.ind", "Escudos.dat" })
            if (!provider.ReadBytes("INIT/" + file).SequenceEqual(File.ReadAllBytes(Path.Combine(init, file))))
                throw new Exception("Conflicting active override: " + file);
        string shields = File.ReadAllText(Path.Combine(init, "Escudos.dat"));
        if (!shields.Contains("NumEscudos=46") || shields.Contains("[ESC47]")) throw new Exception("Unexpected shields");
        var texts = ObjectPaths.Select(p => File.ReadAllText(Path.Combine(_root, p), Encoding.Latin1)).ToArray();
        if (texts.Any(t => !t.Contains("NumOBJs=1676") || t.Contains("[OBJ1677]"))) throw new Exception("Unexpected objects");
        for (int i = 0; i < 6; i++) if (File.Exists(TexturePath(i))) throw new Exception("Texture occupied");
        BuildAtlases(); // Validate/stage every bitmap before touching the shared catalogs.
        using (var w = new BinaryWriter(File.Open(Path.Combine(init, "Graficos.ind"), FileMode.Append)))
        for (int item = 0; item < 6; item++)
        {
            int columns = Columns(item), cell = CellSize(item), frames = item < 4 ? columns * 4 : 4;
            File.Copy(AtlasPath(item), TexturePath(item), false);
            for (int f = 0; f < frames; f++) Static(w, Start(item) + f, item, f % columns * cell, f / columns * cell, cell, cell);
            if (item < 4)
            for (int d = 0; d < 4; d++)
            {
                w.Write(Start(item) + frames + d); w.Write((short)columns);
                for (int f = 0; f < columns; f++) w.Write(Start(item) + d * columns + f);
                w.Write(444f);
            }
            Static(w, Icon(item), item, columns * cell, 0, 32, 32);
        }
        using (var w = new BinaryWriter(File.Open(Path.Combine(init, "Personajes.ind"), FileMode.Open)))
        {
            w.BaseStream.Position = 263; w.Write((short)520); w.BaseStream.Position = w.BaseStream.Length;
            for (int i = 0; i < 2; i++)
            {
                for (int d = 0; d < 4; d++) w.Write((ushort)(Start(i) + 32 + d));
                w.Write((short)0); w.Write((short)-45);
            }
        }
        using (var w = new BinaryWriter(File.Open(Path.Combine(init, "Cascos.ind"), FileMode.Open)))
        {
            w.BaseStream.Position = 263; w.Write((short)114); w.BaseStream.Position = w.BaseStream.Length;
            for (int i = 4; i < 6; i++) for (int d = 0; d < 4; d++) w.Write((ushort)(Start(i) + d));
        }
        shields = shields.Replace("NumEscudos=46", "NumEscudos=48").TrimEnd() + "\r\n";
        for (int i = 2; i < 4; i++)
        {
            shields += $"\r\n[ESC{45 + i}]\r\n";
            for (int d = 1; d <= 4; d++) shields += $"Dir{d}={Start(i) + 15 + d}\r\n";
        }
        File.WriteAllText(Path.Combine(init, "Escudos.dat"), shields, new UTF8Encoding(false));
        var entries = new StringBuilder();
        for (int i = 0; i < 6; i++)
        {
            entries.Append($"\r\n[OBJ{1677 + i}]\r\nName={Names[i]}\r\nGrhIndex={Icon(i)}\r\nObjType={(i < 2 ? 3 : i < 4 ? 16 : 17)}\r\nAgarrable=0\r\n");
            entries.Append(i < 2 ? $"NumRopaje={519 + i}\r\nMINDEF=28\r\nMAXDEF=35\r\nValor=180000\r\n"
                : $"Anim={(i < 4 ? 45 + i : 109 + i)}\r\nMINDEF={(i < 4 ? 5 : 10)}\r\nMAXDEF={(i < 4 ? 8 : 20)}\r\nValor=50000\r\n");
        }
        for (int i = 0; i < ObjectPaths.Length; i++)
            File.WriteAllText(Path.Combine(_root, ObjectPaths[i]), texts[i].Replace("NumOBJs=1676", "NumOBJs=1682").TrimEnd() + "\r\n" + entries, Encoding.Latin1);
    }

    private void BuildAtlases()
    {
        for (int item = 0; item < 6; item++)
        {
            using var src = Image.LoadFromFile(Path.Combine(_art, Keys[item] + ".png"));
            if (src.GetPixel(0, 0).A != 0) throw new Exception("Source requires alpha: " + Keys[item]);
            int columns = Columns(item), rows = item < 4 ? 4 : 2, cellSize = CellSize(item);
            using var atlas = Image.CreateEmpty(columns * cellSize + 32, rows * cellSize, false, Image.Format.Rgba8);
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int x = col * src.GetWidth() / columns, y = row * src.GetHeight() / rows;
                bool frontRest = item < 2 && row == 2 && col == 0;
                using var cell = frontRest
                    ? Image.LoadFromFile(Path.Combine(_art, (item == 0 ? "volcanic" : "glacial") + "-idle.png"))
                    : src.GetRegion(new Rect2I(x, y, (col + 1) * src.GetWidth() / columns - x, (row + 1) * src.GetHeight() / rows - y));
                if (cell.GetPixel(0, 0).A != 0) throw new Exception("Cell requires genuine transparency");
                var bounds = Bounds(cell);
                if (bounds.Size.Y < 100) throw new Exception("Empty source cell");
                float scale = item < 2 ? 44f / 205f : (item < 4 ? 24f : item == 4 ? 18f : 20f) / bounds.Size.Y;
                if (frontRest) scale = 44f / bounds.Size.Y;
                using var sprite = cell.GetRegion(bounds);
                int width = (int)MathF.Round(bounds.Size.X * scale), height = (int)MathF.Round(bounds.Size.Y * scale);
                sprite.Resize(width, height, Image.Interpolation.Nearest);
                int targetX, targetY;
                if (item < 2)
                {
                    int left = cell.GetWidth(), right = -1;
                    for (int cy = bounds.Position.Y; cy < bounds.Position.Y + (frontRest ? (int)(10 / scale * 44f / 205f) : 10); cy++)
                    for (int cx = 0; cx < cell.GetWidth(); cx++)
                        if (cell.GetPixel(cx, cy).A > .5f) { left = Math.Min(left, cx); right = Math.Max(right, cx); }
                    targetX = 32 - (int)MathF.Round(((left + right) * .5f - bounds.Position.X) * scale);
                    targetY = 16;
                }
                else if (item < 4)
                {
                    int[] handX = { 25, 34, 38, 30 };
                    targetX = handX[row] - width / 2;
                    targetY = 38 - height / 2 + (col == 1 ? 1 : col == 3 ? -1 : 0);
                }
                else { targetX = 16 - width / 2; targetY = 31 - height; }
                if (targetX < 0 || targetY < 0 || targetX + width > cellSize || targetY + height > cellSize)
                    throw new Exception($"Clipping {Keys[item]} {row}/{col}");
                atlas.BlitRect(sprite, new Rect2I(0, 0, width, height), new Vector2I(col * cellSize + targetX, row * cellSize + targetY));
            }
            int iconRow = item < 4 ? 2 : 1;
            using var iconCell = atlas.GetRegion(new Rect2I(0, iconRow * cellSize, cellSize, cellSize));
            using var icon = iconCell.GetRegion(Bounds(iconCell));
            float iconScale = 28f / Math.Max(icon.GetWidth(), icon.GetHeight());
            icon.Resize(Math.Max(1, (int)(icon.GetWidth() * iconScale)), Math.Max(1, (int)(icon.GetHeight() * iconScale)), Image.Interpolation.Nearest);
            atlas.BlitRect(icon, new Rect2I(0, 0, icon.GetWidth(), icon.GetHeight()), new Vector2I(columns * cellSize + (32 - icon.GetWidth()) / 2, (32 - icon.GetHeight()) / 2));
            if (atlas.SavePng(AtlasPath(item)) != Error.Ok) throw new Exception("Cannot save atlas");
        }
    }

    private void Validate()
    {
        if (_data.Bodies.Length < 521 || _data.Shields.Length < 49 || _data.Cascos.Length < 115 || _data.Objects.Length < 1683)
            throw new Exception("Incomplete equipment catalogs");
        for (int i = 0; i < 6; i++)
        {
            if (_data.Objects[1677 + i].Name != Names[i] || _data.Objects[1677 + i].GrhIndex != Icon(i)) throw new Exception("Incorrect item");
            using var atlas = Image.LoadFromFile(TexturePath(i));
            if (i < 2)
            {
                using var idle = atlas.GetRegion(new Rect2I(0, 128, 64, 64));
                var bounds = Bounds(idle);
                int runs = 0; bool previous = false;
                for (int x = 0; x < 64; x++)
                {
                    bool foot = false;
                    for (int y = bounds.End.Y - 3; y < bounds.End.Y; y++) foot |= idle.GetPixel(x, y).A >= .5f;
                    if (foot && !previous) runs++;
                    previous = foot;
                }
                if (runs != 2) throw new Exception("Front idle must show two separated boots");
            }
            for (int d = 1; d <= 4; d++)
            {
                int grh = i < 2 ? _data.Bodies[519 + i].Walk[d] : i < 4 ? _data.Shields[45 + i].Walk[d] : _data.Cascos[109 + i].Head[d];
                int count = i < 2 ? 8 : i < 4 ? 4 : 1;
                if (_data.Grhs[grh].NumFrames != count) throw new Exception("Missing directional frames");
                var hashes = new HashSet<string>();
                for (int f = 0; f < count; f++)
                {
                    var r = _data.ResolveGrh(grh, f)!;
                    if (r.FileNum != 45007 + i || r.SX + r.PixelWidth > atlas.GetWidth() || r.SY + r.PixelHeight > atlas.GetHeight()) throw new Exception("Bad frame rectangle");
                    using var frame = atlas.GetRegion(new Rect2I(r.SX, r.SY, r.PixelWidth, r.PixelHeight));
                    if (frame.GetPixel(0, 0).A != 0 || Bounds(frame).Size.Y < 10) throw new Exception("Invalid transparency/frame");
                    hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.GetData())));
                }
                if (hashes.Count != count) throw new Exception("Duplicate animation frames");
            }
            foreach (string path in ObjectPaths)
            {
                var ini = SimpleIni.Parse(File.ReadAllText(Path.Combine(_root, path), Encoding.Latin1));
                string section = $"OBJ{1677 + i}";
                int expected = i < 2 ? 519 + i : i < 4 ? 45 + i : 109 + i;
                if (ini.GetInt(section, i < 2 ? "NumRopaje" : "Anim", 0) != expected || ini.GetInt(section, "GrhIndex", 0) != Icon(i)) throw new Exception("Client/server item mismatch");
            }
        }
        WalkMovementSmoke.Run(_data, 519, 520);
    }

    private static Rect2I Bounds(Image image)
    {
        int l = image.GetWidth(), t = image.GetHeight(), r = -1, b = -1;
        for (int y = 0; y < image.GetHeight(); y++) for (int x = 0; x < image.GetWidth(); x++)
            if (image.GetPixel(x, y).A >= .12f) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y); }
        return r < l ? new Rect2I() : new Rect2I(l, t, r - l + 1, b - t + 1);
    }
    private static void Static(BinaryWriter w, int id, int item, int x, int y, int width, int height)
    { w.Write(id); w.Write((short)1); w.Write(45007 + item); w.Write((short)x); w.Write((short)y); w.Write((short)width); w.Write((short)height); }

    public override void _Draw()
    {
        if (!_ready) return;
        DrawRect(new Rect2(0, 0, 800, 600), new Color(.08f, .1f, .12f));
        DrawString(ThemeDB.FallbackFont, new Vector2(25, 32), "FORJA NEGRA  /  CORONA INVERNAL", fontSize: 23);
        for (int set = 0; set < 2; set++)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(25, 68 + set * 250), set == 0 ? "1677 Armadura   -   1679 Escudo   -   1681 Yelmo" : "1678 Armadura   -   1680 Escudo   -   1682 Yelmo", fontSize: 17);
            for (int d = 1; d <= 4; d++)
            {
                DrawSetTransform(new Vector2(64 + (d - 1) * 180, 167 + set * 250), 0, new Vector2(2, 2));
                var ch = new Character { Body = 519 + set, Head = 1, CascoAnim = 113 + set, ShieldAnim = 47 + set, Heading = d, Moving = !_idle, WalkFrame = _frame, FovAlpha = 1 };
                CharRenderer.DrawCharacter(this, ch, Vector2.Zero, _data, _animator, state: _state);
                DrawSetTransform(Vector2.Zero);
                // Bare head and opposite-set equipment exercise independent slots.
                ch.CascoAnim = 0; ch.ShieldAnim = 0;
                CharRenderer.DrawCharacter(this, ch, new Vector2(55 + (d - 1) * 180, 258 + set * 250), _data, _animator, state: _state);
                ch.CascoAnim = 114 - set; ch.ShieldAnim = 48 - set;
                CharRenderer.DrawCharacter(this, ch, new Vector2(118 + (d - 1) * 180, 258 + set * 250), _data, _animator, state: _state);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Explicit offline import and equipped preview; never runs during normal startup.</summary>
public partial class GuarnieriSmoke : Node2D
{
    private const int BodyId = 518, ItemId = 1676, TextureId = 45006, FirstGrh = 33535, AuraId = 104;
    private const short HeadOffsetY = -49;
    private readonly GameData _data = new();
    private readonly GameState _state = new();
    private readonly GrhAnimator _animator = new();
    private bool _ready;
    private int _frame;
    private string _art = "";

    public override async void _Ready()
    {
        try
        {
            string root = ProjectSettings.GlobalizePath("res://../");
            _art = Path.Combine(root, "resources/art-source/guarnieri");
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--import") >= 0) Import(root);
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--refresh-art") >= 0)
            {
                string bodyPath = Path.Combine(root, "resources/data/INIT/Personajes.ind");
                byte[] bytes = File.ReadAllBytes(bodyPath);
                if (bytes.Length < 265 + BodyId * 12 || BitConverter.ToUInt16(bytes, 265 + (BodyId - 1) * 12) != FirstGrh + 32)
                    throw new Exception("Guarnieri body ownership mismatch");
                BuildAtlas(Path.Combine(root, $"resources/data/Graficos/{TextureId}.png"));
                using var writer = new BinaryWriter(File.Open(bodyPath, FileMode.Open));
                writer.BaseStream.Position = 265 + (BodyId - 1) * 12 + 10;
                writer.Write(HeadOffsetY);
            }
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            Validate(root);
            _state.Config.ShowShadows = false;
            _state.Config.ShowNames = false;
            _ready = true;
            for (_frame = 0; _frame < 8; _frame++)
            {
                QueueRedraw();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var screen = GetViewport().GetTexture().GetImage();
                using var capture = screen.GetRegion(new Rect2I(0, 0, 800, 600));
                if (capture.SavePng(Path.Combine(_art, $"preview-{_frame}.png")) != Error.Ok)
                    throw new Exception("Failed to save equipped preview");
            }
            GD.Print("[GUARNIERI] PASS: item 1676, body 518, 32 distinct frames, aura 104, server/client catalogs agree.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private void Import(string root)
    {
        string init = Path.Combine(root, "resources/data/INIT");
        string bodyPath = Path.Combine(init, "Personajes.ind");
        string grhPath = Path.Combine(init, "Graficos.ind");
        string texturePath = Path.Combine(root, $"resources/data/Graficos/{TextureId}.png");
        var provider = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
        var grhs = GrhLoader.Load(provider);
        for (int i = FirstGrh; i <= FirstGrh + 36; i++)
            if (i < grhs.Length && grhs[i].NumFrames > 0) throw new Exception("GRH occupied: " + i);
        if (File.Exists(texturePath)) throw new Exception("Texture occupied");
        byte[] bodyBytes = File.ReadAllBytes(bodyPath);
        if (bodyBytes.Length != 265 + 517 * 12 || BitConverter.ToInt16(bodyBytes, 263) != 517)
            throw new Exception("Unexpected body format/count");
        string[] objectPaths = { "server/dat/obj.dat", "resources/data/INIT/obj.dat", "client/Data/INIT/obj.dat" };
        string[] objectTexts = objectPaths.Select(p => File.ReadAllText(Path.Combine(root, p), Encoding.Latin1)).ToArray();
        if (objectTexts.Any(t => !t.Contains("NumOBJs=1675") || t.Contains("[OBJ1676]")))
            throw new Exception("Unexpected object catalog");
        // Abort before touching shared data if higher-priority loose overrides diverge.
        if (!provider.ReadBytes("INIT/Personajes.ind").SequenceEqual(bodyBytes)
            || !provider.ReadBytes("INIT/Graficos.ind").SequenceEqual(File.ReadAllBytes(grhPath)))
            throw new Exception("Active graphics/body overrides differ from source");
        BuildAtlas(texturePath);
        using (var writer = new BinaryWriter(File.Open(grhPath, FileMode.Append)))
        {
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 8; col++)
                Static(writer, FirstGrh + row * 8 + col, col * 64, row * 64, 64, 64);
            for (int row = 0; row < 4; row++)
            {
                writer.Write(FirstGrh + 32 + row); writer.Write((short)8);
                for (int col = 0; col < 8; col++) writer.Write(FirstGrh + row * 8 + col);
                writer.Write(444f); // Same complete-cycle duration as the Nigromante.
            }
            Static(writer, FirstGrh + 36, 512, 0, 32, 32);
        }
        using (var writer = new BinaryWriter(File.Open(bodyPath, FileMode.Open)))
        {
            writer.BaseStream.Position = 263; writer.Write((short)BodyId);
            writer.BaseStream.Position = writer.BaseStream.Length;
            for (int d = 0; d < 4; d++) writer.Write((ushort)(FirstGrh + 32 + d));
            writer.Write((short)0); writer.Write(HeadOffsetY);
        }
        string entry = "\r\n[OBJ1676]\r\nName=Tunica Guarnieri\r\nGrhIndex=33571\r\nObjType=3\r\n"
            + "NumRopaje=518\r\nCreaAura=104\r\nAgarrable=0\r\nMINDEF=16\r\nMAXDEF=22\r\nValor=180000\r\n";
        for (int i = 0; i < objectPaths.Length; i++)
            File.WriteAllText(Path.Combine(root, objectPaths[i]),
                objectTexts[i].Replace("NumOBJs=1675", "NumOBJs=1676").TrimEnd() + "\r\n" + entry, Encoding.Latin1);
    }

    private void BuildAtlas(string destination)
    {
        using var source = Image.LoadFromFile(Path.Combine(_art, "guarnieri-source.png"));
        if (source == null || source.GetPixel(0, 0).A != 0) throw new Exception("Source must have genuine transparency");
        using var atlas = Image.CreateEmpty(544, 256, false, Image.Format.Rgba8);
        // One scale for the whole sheet avoids frame-to-frame breathing. Anchor to
        // the collar, not the changing hem/sleeve bounds, so the separate head stays attached.
        float scale = 48f / 195f;
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 8; col++)
        {
            int x = col * source.GetWidth() / 8, y = row * source.GetHeight() / 4;
            using var cell = source.GetRegion(new Rect2I(x, y,
                (col + 1) * source.GetWidth() / 8 - x, (row + 1) * source.GetHeight() / 4 - y));
            Rect2I bounds = Bounds(cell);
            if (bounds.Size.Y < 150 || bounds.Size.Y > 210) throw new Exception("Unexpected source cell dimensions");
            int collarLeft = cell.GetWidth(), collarRight = -1;
            for (int cy = bounds.Position.Y; cy < bounds.Position.Y + 9; cy++)
            for (int cx = 0; cx < cell.GetWidth(); cx++)
                if (cell.GetPixel(cx, cy).A > .5f) { collarLeft = Math.Min(collarLeft, cx); collarRight = Math.Max(collarRight, cx); }
            if (collarRight < collarLeft) throw new Exception("Missing collar");
            float collarX = (collarLeft + collarRight) * .5f;
            using var sprite = cell.GetRegion(bounds);
            int width = (int)MathF.Round(bounds.Size.X * scale), height = (int)MathF.Round(bounds.Size.Y * scale);
            sprite.Resize(width, height, Image.Interpolation.Nearest);
            int targetX = 32 - (int)MathF.Round((collarX - bounds.Position.X) * scale), targetY = 12;
            if (targetX < 1 || targetX + width > 63 || targetY + height > 63) throw new Exception("Sprite clips frame");
            atlas.BlitRect(sprite, new Rect2I(0, 0, width, height), new Vector2I(col * 64 + targetX, row * 64 + targetY));
        }
        using var icon = atlas.GetRegion(new Rect2I(0, 128, 64, 64));
        icon.Resize(32, 32, Image.Interpolation.Nearest);
        atlas.BlitRect(icon, new Rect2I(0, 0, 32, 32), new Vector2I(512, 0));
        if (atlas.SavePng(destination) != Error.Ok) throw new Exception("Cannot save atlas");
    }

    private void Validate(string root)
    {
        if (_data.Objects.Length <= ItemId || _data.Bodies.Length <= BodyId || _data.Auras.Length <= AuraId)
            throw new Exception("Missing Guarnieri catalogs");
        var obj = _data.Objects[ItemId];
        if (obj.Name != "Tunica Guarnieri" || obj.CreaAura != AuraId || obj.GrhIndex != FirstGrh + 36)
            throw new Exception("Wrong Guarnieri item");
        if (_data.Auras[AuraId].ProceduralStyle != 6) throw new Exception("Wrong aura style");
        if (_data.Bodies[BodyId].HeadOffsetY != HeadOffsetY) throw new Exception("Wrong head attachment");
        using var atlas = Image.LoadFromFile(Path.Combine(root, $"resources/data/Graficos/{TextureId}.png"));
        for (int heading = 1; heading <= 4; heading++)
        {
            int grh = _data.Bodies[BodyId].Walk[heading];
            if (_data.Grhs[grh].NumFrames != 8 || _data.Grhs[grh].Speed != 444f) throw new Exception("Wrong walk cadence");
            var hashes = new System.Collections.Generic.HashSet<string>();
            for (int frame = 0; frame < 8; frame++)
            {
                var f = _data.ResolveGrh(grh, frame)!;
                if (f.FileNum != TextureId || f.PixelWidth != 64 || f.PixelHeight != 64) throw new Exception("Wrong sprite reference");
                using var cell = atlas.GetRegion(new Rect2I(f.SX, f.SY, 64, 64));
                if (cell.GetPixel(0, 0).A != 0 || Bounds(cell).Position.Y != 12) throw new Exception("Bad transparency/collar registration");
                hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cell.GetData())));
            }
            if (hashes.Count != 8) throw new Exception("Duplicated walk frames");
        }
        foreach (string p in new[] { "server/dat/obj.dat", "resources/data/INIT/obj.dat", "client/Data/INIT/obj.dat" })
        {
            var ini = SimpleIni.Parse(File.ReadAllText(Path.Combine(root, p), Encoding.Latin1));
            if (ini.GetInt("OBJ1676", "NumRopaje", 0) != BodyId || ini.GetInt("OBJ1676", "CreaAura", 0) != AuraId)
                throw new Exception("Object catalog mismatch: " + p);
        }
        var ch = new Character { AuraIndexA = AuraId };
        _state.Characters[_state.UserCharIndex] = ch;
        AuraPreviewCommand.Apply(_state, _data, "104");
        if (ch.PreviewAuraIndex != AuraId) throw new Exception("Aura preview unavailable");
        AuraPreviewCommand.Apply(_state, _data, "off");
        if (ch.PreviewAuraIndex != 0 || ch.AuraIndexA != AuraId) throw new Exception("Aura preview altered equipment");
        WalkMovementSmoke.Run(_data, BodyId);
    }

    private static Rect2I Bounds(Image image)
    {
        int left = image.GetWidth(), top = image.GetHeight(), right = -1, bottom = -1;
        for (int y = 0; y < image.GetHeight(); y++)
        for (int x = 0; x < image.GetWidth(); x++)
            if (image.GetPixel(x, y).A >= .12f)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return right < left ? new Rect2I() : new Rect2I(left, top, right - left + 1, bottom - top + 1);
    }

    private static void Static(BinaryWriter writer, int id, int x, int y, int width, int height)
    {
        writer.Write(id); writer.Write((short)1); writer.Write(TextureId);
        writer.Write((short)x); writer.Write((short)y); writer.Write((short)width); writer.Write((short)height);
    }

    public override void _Draw()
    {
        if (!_ready) return;
        DrawRect(new Rect2(0, 0, 800, 600), new Color(.09f, .12f, .11f));
        DrawString(ThemeDB.FallbackFont, new Vector2(24, 36), "TUNICA GUARNIERI  /  8 cuadros por direccion", fontSize: 22);
        for (int heading = 1; heading <= 4; heading++)
        {
            var pos = new Vector2(75 + (heading - 1) * 190, 135);
            var ch = new Character { Body = BodyId, Head = 1, Heading = heading, Moving = true, WalkFrame = _frame, FovAlpha = 1 };
            DrawSetTransform(pos, 0, new Vector2(2, 2));
            RunicAuraRenderer.Draw(this, _data.Auras[AuraId], new Vector2(16, 27), _frame * 650, front: false);
            CharRenderer.DrawCharacter(this, ch, Vector2.Zero, _data, _animator, state: _state);
            RunicAuraRenderer.Draw(this, _data.Auras[AuraId], new Vector2(16, 27), _frame * 650, front: true);
            DrawSetTransform(Vector2.Zero);
        }
        for (int heading = 1; heading <= 4; heading++)
        for (int frame = 0; frame < 8; frame++)
        {
            var ch = new Character { Body = BodyId, Head = 1, Heading = heading, Moving = true, WalkFrame = frame, FovAlpha = 1 };
            CharRenderer.DrawCharacter(this, ch, new Vector2(42 + frame * 96, 295 + (heading - 1) * 80), _data, _animator, state: _state);
        }
    }
}

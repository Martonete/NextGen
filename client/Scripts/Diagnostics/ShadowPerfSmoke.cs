using System;
using System.Diagnostics;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

public partial class ShadowPerfSmoke : Node2D
{
    private readonly GameData _data = new();
    private bool _done;
    public override void _Ready()
    {
        _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_done) return;
        _done = true;
        var frames = new Ao20ShadowRenderer.SpritePart[8][];
        for (int f = 0; f < 8; f++)
        {
            var body = _data.ResolveGrh(_data.Bodies[512].Walk[3],f)!;
            var head = _data.ResolveGrh(_data.Heads[1].Head[3],0)!;
            frames[f] = new[] {
                new Ao20ShadowRenderer.SpritePart(body, _data.Textures!.GetTexture(body.FileNum)!,
                    new Vector2(112 + 16 - body.PixelWidth/2, 224 + 32 - body.PixelHeight)),
                new Ao20ShadowRenderer.SpritePart(head, _data.Textures.GetTexture(head.FileNum)!, new Vector2(112,176)) };
        }
        var light = new Ao20ShadowRenderer.CornerColors(Colors.White,Colors.White,Colors.White,Colors.White);
        Ao20ShadowRenderer.ClearCharacterCache();
        for (int pass = 0; pass < 2; pass++)
        {
            long builds = Ao20ShadowRenderer.CompositeBuilds;
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1024; i++)
                Ao20ShadowRenderer.DrawCharacterShadow(this, i % 16, frames[(i/16)%8], new Vector2(100,100), light);
            watch.Stop();
            GD.Print($"[SHADOW-PERF] pass={pass} 1024 draws: {watch.Elapsed.TotalMilliseconds:F2} ms; allocated={GC.GetAllocatedBytesForCurrentThread()-bytes}");
            if (Ao20ShadowRenderer.CachedCompositeCount != 8
                || Ao20ShadowRenderer.CompositeBuilds - builds != (pass == 0 ? 8 : 0))
                throw new Exception("Identical appearances/animation frames did not share the cache");
        }
        for (int i = 0; i < 270; i++)
        {
            var original = frames[0][1];
            frames[0][1] = new(original.Resolved, original.Texture, new Vector2(110 + i * .01f, 176));
            Ao20ShadowRenderer.DrawCharacterShadow(this, 1, frames[0], new Vector2(100,100), light);
        }
        if (Ao20ShadowRenderer.CachedCompositeCount != 256) throw new Exception("Cache limit failed");
        Ao20ShadowRenderer.ClearCharacterCache();
        if (Ao20ShadowRenderer.CachedCompositeCount != 0) throw new Exception("Cache cleanup failed");
        var lines = new Vector2[60];
        for (int i = 0; i < lines.Length; i++) lines[i] = new Vector2(100+i, 100 + MathF.Sin(i)*10);
        foreach (bool batched in new[] { false, true })
        {
            var watch = Stopwatch.StartNew();
            for (int repeat = 0; repeat < 1024; repeat++)
            {
                if (batched)
                {
                    DrawMultiline(lines, Colors.Blue, 4, true);
                    DrawMultiline(lines, Colors.White, 1.3f, true);
                }
                else for (int i = 0; i < lines.Length; i+=2)
                {
                    DrawLine(lines[i], lines[i+1], Colors.Blue, 4, true);
                    DrawLine(lines[i], lines[i+1], Colors.White, 1.3f, true);
                }
            }
            GD.Print($"[ELECTRIC-PERF] batched={batched}: {watch.Elapsed.TotalMilliseconds:F2} ms");
        }
        GetTree().Quit();
    }
}

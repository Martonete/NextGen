using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Filmstrip of the Relampago strike (FX 102) at fixed ages, for tuning the look.</summary>
public partial class LightningSmoke : Node2D
{
    private readonly GameData _data = new();
    private readonly GrhAnimator _animator = new();
    private WorldRenderer? _renderer;
    private static readonly float[] Ages = { 0.02f, 0.06f, 0.10f, 0.16f, 0.23f, 0.30f, 0.38f, 0.48f, 0.60f };

    public override async void _Ready()
    {
        try
        {
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            _renderer = new WorldRenderer();
            AddChild(_renderer);
            QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            string output = ProjectSettings.GlobalizePath("user://lightning-strip.png");
            if (image.SavePng(output) != Error.Ok) throw new Exception("Capture failed");
            GD.Print("[LIGHTNING-SMOKE] PASS " + output);
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    public override void _Draw()
    {
        if (!_data.IsLoaded || _renderer == null) return;
        DrawRect(new Rect2(0, 0, 800, 600), new Color(0.16f, 0.19f, 0.12f));
        for (int i = 0; i < Ages.Length; i++)
        {
            int col = i % 3, row = i / 3;
            Vector2 pos = new(80 + col * 260, 130 + row * 190);
            var ch = new Character { Body = 1, Head = 1, Heading = 3, FovAlpha = 1 };
            CharRenderer.DrawCharacter(this, ch, pos, _data, _animator);
            _renderer.DrawLightningShape(this, pos + new Vector2(16, 27), Ages[i], 1f, 4);
            DrawString(ThemeDB.FallbackFont, pos + new Vector2(-10, 50), $"{Ages[i]:0.00}s", fontSize: 12);
        }
    }
}

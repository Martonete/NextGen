using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

public partial class ArmorWalkSmoke : Node2D
{
    private readonly GameData _data = new();
    private readonly GrhAnimator _animator = new();
    private readonly GameState _state = new();
    private int _body = 512;

    public override async void _Ready()
    {
        try
        {
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--orange") >= 0) _body = 513;
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            _state.Config.ShowShadows = false;
            _state.Config.ShowNames = false;
            foreach (int index in _data.Bodies[_body].Walk)
                if (index > 0 && !_data.WalkRegistration.ContainsKey(index))
                {
                    for (int f = 0; f < _data.Grhs[index].NumFrames; f++)
                    {
                        var crop = _data.ResolveGrh(index, f)!;
                        GD.Print($"[ARMOR] {index}/{f}: {crop.FileNum} {crop.SX},{crop.SY},{crop.PixelWidth},{crop.PixelHeight}");
                    }
                    throw new Exception("Nigromante direction was not registered: " + index);
                }
            GD.Print($"[ARMOR] Registered {_data.WalkRegistration.Count} cropped walk strips");
            WalkMovementSmoke.Run(_data);
            QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            string output = ProjectSettings.GlobalizePath($"user://armor-head-{_body}.png");
            if (image.SavePng(output) != Error.Ok) throw new Exception("Capture failed");
            GD.Print("[ARMOR] PASS " + output);
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    public override void _Draw()
    {
        if (!_data.IsLoaded) return;
        DrawRect(new Rect2(0, 0, 800, 600), new Color(.2f, .25f, .28f));
        for (int heading = 1; heading <= 4; heading++)
        for (int frame = 0; frame < 8; frame++)
        {
            var ch = new Character { Body = _body, Head = 1, Heading = heading,
                Moving = true, WalkFrame = frame, FovAlpha = 1 };
            var pos = new Vector2(48 + frame * 90, 65 + (heading - 1) * 110);
            DrawLine(pos + new Vector2(16, -60), pos + new Vector2(16, 32), new Color(.4f,.45f,.48f));
            CharRenderer.DrawCharacter(this, ch, pos, _data, _animator, state: _state);
        }
    }
}

using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

public partial class TrollBossPreview : Node2D
{
    private readonly GameData _data = new();
    private readonly GrhAnimator _animator = new();
    private int _frame;
    public override async void _Ready()
    {
        try
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            GetWindow().Size = new Vector2I(1100, 600);
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            for (int d = 1; d <= 4; d++)
            {
                var cycle = _data.Grhs[_data.Bodies[522].Walk[d]];
                if (cycle.NumFrames != 8 || cycle.PixelHeight != 352 || cycle.Speed != 1100)
                    throw new Exception("Boss animation invalid");
            }
            for (_frame = 0; _frame < 8; _frame++)
            {
                QueueRedraw();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(ProjectSettings.GlobalizePath($"res://../resources/art-source/troll-rey/preview-{_frame}.png"));
            }
            GD.Print("[TROLL BOSS] PASS: 4 directions, 8 frames, 256x352 cells, real character renderer.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 2200, 1200), new Color(.12f, .17f, .13f));
        if (_data.Bodies.Length <= 522) return;
        for (int d = 1; d <= 4; d++)
        {
            var pos = new Vector2(135 + (d - 1) * 250, 370);
            DrawLine(pos + new Vector2(-100, 28), pos + new Vector2(120, 28), Colors.DarkKhaki);
            var boss = new Character { Body = 522, Heading = d, Moving = true, WalkFrame = _frame, FovAlpha = 1 };
            CharRenderer.DrawCharacter(this, boss, pos, _data, _animator);
            var troll = new Character { Body = 521, Heading = d, Moving = true, WalkFrame = _frame, FovAlpha = 1 };
            CharRenderer.DrawCharacter(this, troll, pos + new Vector2(0, 155), _data, _animator);
        }
    }
}

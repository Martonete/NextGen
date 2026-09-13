using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

public partial class HouseArtSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode = Window.ModeEnum.Windowed;
            GetWindow().Size = new Vector2I(800, 600);
            GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            var data = new GameData(); data.LoadAll(resources);
            var state = new GameState { MapData = MapLoader.Load(resources, 28), CurrentMap = 28,
                UserPosX = 31, UserPosY = 57, UserCharIndex = 1 };
            state.Characters[1] = new Character { CharIndex = 1, Body = 1, Head = 1,
                Heading = 3, PosX = 31, PosY = 57 };
            var renderer = new WorldRenderer();
            renderer.SetRenderWindow(new Vector2I(800, 600));
            renderer.Init(state, data, new GrhAnimator(), resources);
            AddChild(renderer);
            renderer.RebuildWaterMap(); renderer.BuildRoofRegions();
            string output = ProjectSettings.GlobalizePath("res://../art-source/tanaris-houses");
            Directory.CreateDirectory(output);
            foreach (bool inside in new[] {false, true})
            {
                if (inside) { state.UserPosY = 52; state.Characters[1].PosY = 52; }
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var screenshot = GetViewport().GetTexture().GetImage();
                if (screenshot.SavePng(Path.Combine(output, inside ? "interior.png" : "exterior.png")) != Error.Ok)
                    throw new IOException("Cannot save house preview.");
            }
            GD.Print("HOUSE SMOKE PASS");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

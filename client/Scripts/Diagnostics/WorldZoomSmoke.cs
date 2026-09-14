using System;
using System.Reflection;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline real-map zoom and click regression; never connects or saves user settings.</summary>
public partial class WorldZoomSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var root = GetWindow();
            root.Mode = Window.ModeEnum.Fullscreen;
            var native = DisplayServer.ScreenGetSize(root.CurrentScreen);
            root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            ResolutionManager.ApplyResolution(native.X, native.Y, true);
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            var data = new GameData(); data.LoadAll(resources);
            var state = new GameState { MapData = MapLoader.Load(resources, 28), CurrentMap = 28,
                UserPosX = 38, UserPosY = 61, UserCharIndex = 1 };
            state.Characters[1] = new Character { CharIndex = 1, Body = 1, Head = 1, Heading = 3, Name = "Zoom de prueba",
                PosX = 38, PosY = 61, FovAlpha = 1 };
            var container = new SubViewportContainer { Size = native, Stretch = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            AddChild(container);
            var viewport = new SubViewport { RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            container.AddChild(viewport);
            var renderer = new WorldRenderer();
            renderer.Init(state, data, new GrhAnimator(), resources); viewport.AddChild(renderer);
            renderer.RebuildWaterMap(); renderer.BuildRoofRegions();
            var input = new InputHandler(new AoTcpClient(), state, new KeyBindings(), viewport);
            var pick = typeof(InputHandler).GetMethod("ViewportToTile", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (float zoom in new[] { ResolutionManager.WorldZoom })
            {
                viewport.Size = new Vector2I(ResolutionManager.RenderPixelW, ResolutionManager.RenderPixelH);
                container.Scale = new Vector2((float)native.X / viewport.Size.X, (float)native.Y / viewport.Size.Y);
                await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                var expected = new Vector2I(ResolutionManager.RenderPixelW, ResolutionManager.RenderPixelH);
                if (viewport.Size != expected) throw new Exception($"Zoom {zoom}: viewport {viewport.Size} != {expected}");
                foreach (float offset in new[] { 0f, 7.5f, 31f })
                {
                    state.ScreenOffsetX = offset;
                    var point = new Vector2((ResolutionManager.HalfTilesX + 2) * 32 + 16 - offset,
                        (ResolutionManager.HalfTilesY + 1) * 32 + 16);
                    var tile = ((int, int))pick.Invoke(input, new object[] { point })!;
                    if (tile != (40, 62)) throw new Exception($"Zoom {zoom}: wrong moving click {tile}");
                }
                state.ScreenOffsetX = 0;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(ProjectSettings.GlobalizePath($"user://world-zoom-{zoom}.png"));
            }
            GD.Print($"[WORLD-ZOOM] PASS: fixed {ResolutionManager.WorldZoom}x viewport, real map, stationary/moving tile picking.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

using System;
using System.IO;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline visual smoke scene. Uses production assets without connecting or saving a game.</summary>
public partial class RenderSmoke : Node
{
    private GameData _data = new();
    private readonly GrhAnimator _animator = new();
    private readonly GameState _state = new();
    private bool _testMovement;
    private float _movementTime;

    public override async void _Ready()
    {
        try
        {
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            _data.LoadAll(resources);
            WalkMovementSmoke.Run(_data);
            var map = MapLoader.Load(resources, 28);
            _state.MapData = map;
            _state.CurrentMap = 28;
            _state.UserPosX = 50;
            _state.UserPosY = 50;
            _state.UserCharIndex = 1;
            _state.Config.ShowNames = false;
            var grounds = new Dictionary<int, int>();
            for (int y = 1; y <= map.Height; y++)
                for (int x = 1; x <= map.Width; x++)
                {
                    int grh = map.Tiles[x, y].Layer1;
                    var sprite = _data.ResolveGrh(grh, 0);
                    if (grh > 0 && !WorldRenderer.IsWaterGrh(grh) && sprite?.PixelWidth == 32 && sprite.PixelHeight == 32)
                        grounds[grh] = grounds.GetValueOrDefault(grh) + 1;
                }
            int ground = 0, count = 0;
            foreach (var entry in grounds)
                if (entry.Value > count) { ground = entry.Key; count = entry.Value; }
            if (ground == 0) throw new Exception("No valid terrain found");
            // In-memory clearing so every test has the same canopy/character overlap.
            for (int y = 36; y <= 64; y++)
                for (int x = 34; x <= 66; x++)
                    map.Tiles[x, y] = new MapTile { Layer1 = ground };
            foreach (var (x, y, grh) in new[] {
                (44, 46, 7000), (48, 46, 7222), (53, 46, 7223), (57, 47, 7001),
                (43, 52, 7224), (49, 52, 7222), (56, 52, 7225),
                (45, 58, 7000), (52, 58, 7223), (59, 58, 7002) })
                map.Tiles[x, y].Layer3 = grh;
            _state.Characters[1] = new Character { CharIndex = 1, Body = 1, Head = 1, Heading = 3, PosX = 50, PosY = 50 };
            ResolutionManager.ApplyResolution(800, 600, true);
            var renderer = new WorldRenderer();
            renderer.Init(_state, _data, _animator, resources);
            AddChild(renderer);
            renderer.RebuildWaterMap();
            renderer.BuildRoofRegions();
            var output = ProjectSettings.GlobalizePath("user://render-smoke");
            Directory.CreateDirectory(output);
            var testConfig = new GameConfig { ShowReactiveEffects = false };
            var testConfigPath = Path.Combine(output, "config-roundtrip");
            testConfig.Save(testConfigPath);
            if (GameConfig.Load(testConfigPath).ShowReactiveEffects)
                throw new Exception("Reactive effect preference did not persist");
            var copy = new GameConfig();
            copy.CopyFrom(testConfig.Clone());
            if (copy.ShowReactiveEffects) throw new Exception("Reactive effect preference did not copy");
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "vegetation.png");
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "vegetation-wind.png");
            _state.Config.ShowVegetationWind = false;
            _state.Config.TreeRoofTransparency = false;
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "vegetation-disabled.png");
            _state.Config.ShowVegetationWind = true;
            _state.Config.TreeRoofTransparency = true;
            _state.MapData = MapLoader.Load(resources, 28);
            renderer.ResetMapVisualCaches();
            renderer.RebuildWaterMap();
            renderer.BuildRoofRegions();
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "tanaris.png");
            _state.Meditating = true;
            await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "reactive-meditation.png");
            foreach (int level in new[] { 1, 13, 25, 35, 50 })
            {
                _state.Level = level;
                await ToSignal(GetTree().CreateTimer(2.2), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Save(output, $"meditation-level-{level}.png");
            }
            _state.Meditating = false;
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            var packets = new ArgentumNextgen.Network.PacketHandler(_state);
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 11, 0, 0, 0 });
            if (_state.Characters[1].ApocalypseTime >= 0) throw new Exception("Other spell triggered Apocalypse");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 0, 0, 0, 0 });
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 13, 0, 0, 0 });
            if (_state.Characters[1].ApocalypseTime != 0) throw new Exception("Apocalypse packet did not trigger impact");
            await ToSignal(GetTree().CreateTimer(0.16), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "apocalypse-impact.png");
            await ToSignal(GetTree().CreateTimer(0.45), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "apocalypse-embers.png");
            await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
            if (_state.Characters[1].ApocalypseTime >= 0) throw new Exception("Apocalypse did not expire");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 13, 0, 0, 0 });
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 13, 0, 0, 0 });
            if (_state.Characters[1].ApocalypseTime != 0) throw new Exception("Repeated Apocalypse did not restart");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 0, 0, 0, 0 });
            if (_state.Characters[1].ApocalypseTime >= 0) throw new Exception("Clear FX did not cancel Apocalypse");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 11, 0, 0, 0 });
            if (_state.Characters[1].ElectricDischargeTime != 0 || _state.Characters[1].ApocalypseTime >= 0)
                throw new Exception("Electric discharge packet did not trigger independently");
            int particlesBeforeElectric = _state.MapParticles.Count;
            packets.HandleBinaryData(new byte[] { 211, 1, 0, 106, 0 });
            if (_state.MapParticles.Count != particlesBeforeElectric || _state.Characters[1].SuppressNextSpellImpact)
                throw new Exception("Electric discharge emitted generic explosion particles");
            await ToSignal(GetTree().CreateTimer(0.09), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "electric-impact.png");
            await ToSignal(GetTree().CreateTimer(0.18), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "electric-arcs.png");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 11, 0, 0, 0 });
            if (_state.Characters[1].ElectricDischargeTime != 0) throw new Exception("Electric discharge did not restart");
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            if (_state.Characters[1].ElectricDischargeTime >= 0) throw new Exception("Electric discharge did not expire");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 11, 0, 0, 0 });
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 0, 0, 0, 0 });
            if (_state.Characters[1].ElectricDischargeTime >= 0) throw new Exception("Clear FX did not cancel electricity");
            _state.Config.ShowReactiveEffects = false;
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 11, 0, 0, 0 });
            if (Array.IndexOf(_state.Characters[1].ActiveFxSlots, 11) < 0)
                throw new Exception("Electric discharge classic fallback missing");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 0, 0, 0, 0 });
            _state.Config.ShowReactiveEffects = true;
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 8, 0, 0, 0 });
            if (_state.Characters[1].BindingTime != 0 || _state.Characters[1].ElectricDischargeTime >= 0)
                throw new Exception("Binding did not start independently");
            await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "paralysis-closing.png");
            await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "paralysis-tight.png");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 8, 0, 0, 0 });
            if (_state.Characters[1].BindingTime != 0) throw new Exception("Binding did not restart");
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            if (_state.Characters[1].BindingTime >= 0) throw new Exception("Binding did not expire");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 8, 0, 0, 0 });
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 0, 0, 0, 0 });
            if (_state.Characters[1].BindingTime >= 0) throw new Exception("Clear FX did not cancel binding");
            // -24 signed i16 (E8 FF) marks Inmovilizar without changing packet size.
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 8, 0, 232, 255 });
            if (_state.Characters[1].BindingIsParalysis) throw new Exception("Inmovilizar marker ignored");
            int bindingParticles = _state.MapParticles.Count;
            packets.HandleBinaryData(new byte[] { 211, 1, 0, 106, 0 });
            if (_state.MapParticles.Count != bindingParticles) throw new Exception("Binding emitted a flash");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "binding-tight.png");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 8, 0, 0, 0 });
            if (!_state.Characters[1].BindingIsParalysis) throw new Exception("Paralysis marker ignored");
            packets.HandleBinaryData(new byte[] { 43, 1, 0, 0, 0, 0, 0 });
            _state.Characters[1].HitFlash(true);
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "reactive-impact.png");
            _testMovement = true;
            await ToSignal(GetTree().CreateTimer(0.55), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "reactive-dust.png");
            _testMovement = false;
            _movementTime = 0;
            _state.Characters[1].PosX = 50;
            _state.Characters[1].MoveOffsetX = 0;
            for (int y = 36; y <= 64; y++)
                for (int x = 34; x <= 66; x++)
                    _state.MapData.Tiles[x, y] = new MapTile { Layer1 = 1505 };
            _state.Characters[1].Navigating = true;
            renderer.ResetMapVisualCaches();
            renderer.RebuildWaterMap();
            renderer.BuildRoofRegions();
            _testMovement = true;
            await ToSignal(GetTree().CreateTimer(0.65), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "reactive-water.png");
            _testMovement = false;
            _state.Characters[1].Invisible = true;
            await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "reactive-invisible.png");
            GD.Print("[RENDER-SMOKE] PASS: " + output);
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("[RENDER-SMOKE] FAIL: " + ex);
            GetTree().Quit(1);
        }
    }

    private void Save(string output, string name)
    {
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(output, name)) != Error.Ok)
            throw new IOException("Could not save render capture");
    }

    public override void _Process(double delta)
    {
        if (_data.IsLoaded) _animator.Update((float)delta, _data);
        if (_testMovement)
        {
            _movementTime += (float)delta;
            float x = 1600 + _movementTime * 120;
            var player = _state.Characters[1];
            player.PosX = (int)(x / 32);
            player.MoveOffsetX = x - player.PosX * 32;
            player.Moving = true;
            player.Heading = 2;
        }
    }
}

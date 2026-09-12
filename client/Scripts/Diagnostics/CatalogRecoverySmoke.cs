using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline regression: real Tanaris tiles, player and login backdrop, without networking.</summary>
public partial class CatalogRecoverySmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            var data = new GameData();
            data.LoadAll(resources);
            if (data.Bodies.Length < 518 || data.Weapons.Length < 86 || data.Objects.Length < 1676)
                throw new Exception("Incompatible character/object catalog; expected the Vigilia dataset.");
            if (!WorldRenderer.IsWaterGrh(1505) || WorldRenderer.IsWaterGrh(1))
                throw new Exception("Incompatible water catalog.");
            var state = new GameState
            {
                MapData = MapLoader.Load(resources, 28), CurrentMap = 28,
                UserPosX = 33, UserPosY = 61, UserCharIndex = 1
            };
            state.Characters[1] = new Character
            {
                CharIndex = 1, Body = 514, Head = 1, Heading = 3, PosX = 33, PosY = 61
            };
            ResolutionManager.ApplyResolution(800, 600, true);
            var renderer = new WorldRenderer();
            renderer.SetRenderWindow(new Vector2I(800, 600));
            renderer.Init(state, data, new GrhAnimator(), resources);
            AddChild(renderer);
            renderer.RebuildWaterMap();
            renderer.BuildRoofRegions();
            string output = ProjectSettings.GlobalizePath("user://catalog-recovery");
            Directory.CreateDirectory(output);
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "tanaris-28-33-61.png");
            for (int i = 0; i < 18; i++)
            {
                int id = i < 12 ? i + 1 : 1670 + i - 12;
                var obj = data.Objects[id];
                state.Inventory[i] = new InventorySlot
                {
                    ObjIndex = id, Name = obj.Name, Amount = 1, GrhIndex = obj.GrhIndex
                };
            }
            var inventory = new InventoryPanel
            {
                Position = new Vector2(605, 30), Size = new Vector2(175, 175)
            };
            inventory.Init(state, data, new AoTcpClient());
            AddChild(inventory);
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "inventory.png");
            inventory.QueueFree();
            renderer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var backdrop = new LoginBackdrop();
            AddChild(backdrop);
            backdrop.Init(data, resources);
            backdrop.SetActive(true);
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Save(output, "login-backdrop.png");
            GD.Print($"[CATALOG-RECOVERY] PASS. Screenshots: {output}");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr(ex);
            GetTree().Quit(1);
        }
    }

    private void Save(string output, string name)
    {
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(output, name)) != Error.Ok)
            throw new IOException("Cannot save recovery screenshot.");
    }
}

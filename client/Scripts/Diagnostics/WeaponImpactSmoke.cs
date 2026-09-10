using System;
using System.Reflection;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

public partial class WeaponImpactSmoke : Node2D
{
    private readonly GameData _data = new();
    private readonly GrhAnimator _animator = new();
    public override async void _Ready()
    {
        try
        {
            TestPackets();
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            string output = ProjectSettings.GlobalizePath("user://weapon-impacts.png");
            if (image.SavePng(output) != Error.Ok) throw new Exception("Capture failed");
            GD.Print("[WEAPON-SMOKE] PASS " + output);
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private static void TestPackets()
    {
        var state = new GameState { IsLogged = true, MapData = new MapData(100,100) };
        var ch = new Character { CharIndex = 1, PosX = 50, PosY = 50, FovAlpha = 1 };
        state.Characters[1] = ch;
        state.Config.ShowReactiveEffects = true; state.Config.ShowParticles = true;
        var packets = new PacketHandler(state);
        void Send(byte kind, byte payload = 3) => packets.HandleBinaryData(new byte[] {43,1,0,kind,0,payload,0});
        packets.HandleBinaryData(new byte[] {43,1,0,201});
        Check(state.WeaponImpacts.Count == 0, "Partial packet must not emit a strike");
        packets.HandleBinaryData(new byte[] {0,3,0});
        Check(state.WeaponImpacts.Count == 1, "Completed packet emits one strike");
        Send(201,11);
        Check(state.WeaponImpacts.Count == 1 && state.WeaponImpacts[0].Critical, "Critical upgrades base strike");
        Send(201,7); Check(state.WeaponImpacts.Count == 1, "Invalid direction rejected");
        Send(13,1); Check(state.WeaponImpacts.Count == 1, "Spell does not emit a weapon strike");
        state.WeaponImpacts.Clear();
        for (byte kind = 201; kind <= 206; kind++) Send(kind);
        Check(state.WeaponImpacts.Count == 6, "All six styles decode");
        Send(207);
        Check(ch.GmTeleportAuraTime == 0f && state.WeaponImpacts.Count == 6 && ch.ActiveFxSlots[0] == 0,
            "GM teleport aura decodes outside weapon and classic FX slots");
        Send(0);
        Check(ch.GmTeleportAuraTime < 0f, "Clearing classic FX also clears GM teleport aura");
        ch.Invisible = true; Send(201);
        Check(state.WeaponImpacts.Count == 6, "Invisible target cannot emit a strike");
        ch.Invisible = false;
        for (int i = 0; i < 200; i++) Send(201);
        Check(state.WeaponImpacts.Count == 128, "Burst memory bound");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var renderer = new WorldRenderer();
        try
        {
            typeof(WorldRenderer).GetField("_state", flags)!.SetValue(renderer, state);
            var update = (Action<float>)typeof(WorldRenderer).GetMethod("UpdateWeaponImpacts", flags)!
                .CreateDelegate(typeof(Action<float>), renderer);
            state.Paused = true; update(.2f);
            Check(state.WeaponImpacts[0].Age == 0, "Pause freezes age");
            state.Paused = false;
            state.Characters.Clear(); update(.01f);
            Check(state.WeaponImpacts.Count == 128, "Lethal removal preserves contact snapshot");
            update(.35f); Check(state.WeaponImpacts.Count == 0, "All styles expire");
            state.Characters[1] = ch; Send(201);
            state.MapData = new MapData(100,100); update(0);
            Check(state.WeaponImpacts.Count == 0, "Map change clears effects");
            Send(201); state.Config.ShowParticles = false; update(0);
            Check(state.WeaponImpacts.Count == 0, "Disabled particles clear effects");
            Send(201); Check(state.WeaponImpacts.Count == 0, "Disabled particles do not queue");
        }
        finally { renderer.Free(); }
        GD.Print("[WEAPON-SMOKE] Protocol, critical, visibility, lifecycle and capacity PASS");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }

    public override void _Draw()
    {
        if (!_data.IsLoaded) return;
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--bench") >= 0)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 1024; i++)
                    WorldRenderer.DrawWeaponImpactShape(this, new Vector2(100,100), 201, 3, .25f, false);
                GD.Print($"[STRIKE-PERF] pass={pass} 1024 sword effects: {watch.Elapsed.TotalMilliseconds:F2} ms");
            }
        }
        DrawRect(new Rect2(0,0,800,600), new Color(.075f,.10f,.12f));
        string[] labels = { "Espada", "Hacha / martillo", "Daga", "Flecha", "Cuerda de arco", "Sin arma" };
        for (int row = 0; row < 6; row++)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(15, 35 + row * 90), labels[row], fontSize: 14);
            for (int col = 0; col < 5; col++)
            {
                Vector2 pos = new(180 + col * 120, 50 + row * 90);
                var ch = new Character { Body = 1, Head = 1, Heading = 3, FovAlpha = 1 };
                CharRenderer.DrawCharacter(this, ch, pos, _data, _animator);
                WorldRenderer.DrawWeaponImpactShape(this, pos + new Vector2(16,4), 201 + row, 3,
                    col == 4 ? .25f : .08f + col * .22f, col == 4);
                if (col == 4) DrawString(ThemeDB.FallbackFont, pos + new Vector2(-8,50), "Crítico", fontSize: 12);
            }
        }
    }
}

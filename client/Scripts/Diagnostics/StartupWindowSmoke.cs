using System;
using System.Diagnostics;
using System.Reflection;
using Godot;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Real startup, no login request: checks native and Godot window state after loading.</summary>
public partial class StartupWindowSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var elapsed = Stopwatch.StartNew();
            var main = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
            AddChild(main);
            T Field<T>(string name) => (T)typeof(Main).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            while (!Field<bool>("_startupPreloadDone"))
            {
                if (elapsed.Elapsed.TotalSeconds > 60) throw new Exception("Startup timeout");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Field<LoginForm>("_loginForm").AccountInput!.Text = "";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var root = GetTree().Root;
            if (root.Mode != Window.ModeEnum.Fullscreen || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen)
                throw new Exception("Window not actually fullscreen");
            var screenSize = DisplayServer.ScreenGetSize(root.CurrentScreen);
            // This Windows driver reports a 1px inset per edge in non-exclusive fullscreen.
            var sizeDelta = DisplayServer.WindowGetSize() - screenSize;
            var positionDelta = DisplayServer.WindowGetPosition() - DisplayServer.ScreenGetPosition(root.CurrentScreen);
            if (Math.Abs(sizeDelta.X) > 2 || Math.Abs(sizeDelta.Y) > 2 || Math.Abs(positionDelta.X) > 2 || Math.Abs(positionDelta.Y) > 2)
                throw new Exception($"Fullscreen bounds mismatch: size={DisplayServer.WindowGetSize()}, position={DisplayServer.WindowGetPosition()}");
            if (Field<Control>("_startupLoadingScreen").Visible) throw new Exception("Loading overlay still blocks login");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(ProjectSettings.GlobalizePath("user://startup-window-smoke.png"));
            GD.Print($"[STARTUP-SMOKE] PASS native fullscreen {screenSize}, login ready in {elapsed.Elapsed.TotalSeconds:F2}s (engine initialization excluded)");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

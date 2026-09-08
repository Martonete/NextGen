using System;
using System.IO;
using System.Reflection;
using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline integration of Main's real HUD. No login or packets are sent.</summary>
public partial class FloatingHudSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var main = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
            AddChild(main);
            T Field<T>(string name) => (T)typeof(Main).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            main.SetProcess(false);
            Field<Control>("_startupLoadingScreen").Hide();
            Field<LoginForm>("_loginForm").HideForm();
            Field<Control>("_gameUI").Show();
            Field<Control>("_viewportContainer").Hide();
            var state = Field<GameState>("_state");
            state.AccountName = "offline-hud-test"; state.UserName = "preview"; state.IsLogged = true;
            state.Spells[0] = new SpellSlot { SpellId = 11, Name = "Descarga eléctrica" };
            state.Inventory[0] = new InventorySlot { ObjIndex = 1674, Amount = 1, Name = "Espada Corazón Carmesí", GrhIndex = 33513 };
            var quickbar = Field<Quickbar>("_quickbar");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            quickbar._Process(0.25);
            quickbar.Slots[0].Spell = true; quickbar.Slots[0].Id = 11; quickbar.Slots[0].Name = "Descarga eléctrica";
            quickbar.Slots[1].Id = 1674; quickbar.Slots[1].Name = "Espada Corazón Carmesí";
            int calls = 0;
            quickbar.Execute = (_, index) => { if (index != 0) throw new Exception("Wrong resolved index"); calls++; };
            var key = new InputEventKey { Keycode = Key.Key1, Pressed = true };
            quickbar.HandleKey(key);
            if (calls != 1) throw new Exception("Quickbar did not execute");
            state.ChatActive = true; quickbar.HandleKey(key);
            if (calls != 1) throw new Exception("Quickbar executed during chat");
            state.ChatActive = false;
            state.Spells[4] = state.Spells[0]; state.Spells[0] = new SpellSlot();
            if (quickbar.Slots[0].Resolve(state) != 4) throw new Exception("Spell reorder broke binding");
            state.Inventory[5] = state.Inventory[0]; state.Inventory[0] = new InventorySlot();
            if (quickbar.Slots[1].Resolve(state) != 5) throw new Exception("Inventory reorder broke binding");
            state.Inventory[5].Amount = 0;
            if (quickbar.Slots[1].Resolve(state) != -1) throw new Exception("Absent object still available");
            for (int i = 0; i < state.Inventory.Length; i++) state.Inventory[i] ??= new InventorySlot();
            for (int i = 0; i < state.Spells.Length; i++) state.Spells[i] ??= new SpellSlot();
            state.Inventory[5].Amount = 1;
            var data = Field<ArgentumNextgen.Data.GameData>("_gameData");
            Field<InventoryPanel>("_inventoryPanel").Init(state, data, new ArgentumNextgen.Network.AoTcpClient());
            Field<SpellPanel>("_spellPanel").Init(state, data, new ArgentumNextgen.Network.AoTcpClient());
            Field<StatBarOverlay>("_statBarOverlay").SetStats(495, 495, 2810, 2810, 999, 999, 100, 100, 100, 100, 1, 10);
            var window = Field<FloatingHudWindow>("_inventoryWindow");
            var oldPosition = window.Position;
            window.Hide(); window.Show();
            if (window.Position != oldPosition) throw new Exception("Hide/show lost position");
            typeof(Quickbar).GetMethod("OpenSlot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(quickbar, new object[] { 2 });
            typeof(Quickbar).GetField("_capture", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(quickbar, 2);
            state.QuickbarEditing = true;
            quickbar.HandleKey(new InputEventKey { Keycode = Key.F12, Pressed = true });
            if (!state.QuickbarEditing) throw new Exception("Reserved key was accepted");
            quickbar.HandleKey(new InputEventKey { Keycode = Key.End, Pressed = true });
            if (state.QuickbarEditing || quickbar.Slots[2].Key != Key.End) throw new Exception("Rebinding failed");
            typeof(Quickbar).GetMethod("LoadSlots", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(quickbar, null);
            if (quickbar.Slots[2].Key != Key.End || quickbar.Slots[1].Id != 1674) throw new Exception("Persistence failed");
            if (!state.QuickbarKeys.Contains(Key.Key1)) throw new Exception("Legacy macro not suppressed");
            quickbar._Process(0.25);
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            Field<Control>("_loginBackdrop").Hide();
            Field<Control>("_gameUI").Show();
            foreach (string name in new[] { "_inventoryWindow", "_statusWindow", "_quickWindow" }) Field<Control>(name).Show();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string output = ProjectSettings.GlobalizePath("user://floating-hud-smoke.png");
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(output) != Error.Ok) throw new IOException("Capture failed");
            typeof(Quickbar).GetMethod("OpenSlot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(quickbar, new object[] { 0 });
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var menuImage = GetViewport().GetTexture().GetImage();
            menuImage.SavePng(ProjectSettings.GlobalizePath("user://floating-hud-menu-smoke.png"));
            GD.Print($"[FLOATING-HUD] PASS actions, chat guard, stable IDs, missing objects, visibility; screenshot: {output}");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

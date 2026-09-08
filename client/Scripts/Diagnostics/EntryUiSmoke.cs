using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline pre-game UI preview. Never reads saved credentials or connects.</summary>
public partial class EntryUiSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            ResolutionManager.ApplyResolution(800, 600, true);
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            var data = new GameData();
            data.LoadAll(resources);
            var backdrop = new LoginBackdrop();
            AddChild(backdrop);
            backdrop.Init(data, resources);
            backdrop.SetActive(true);
            var state = new GameState();
            string output = ProjectSettings.GlobalizePath("user://entry-ui-smoke");
            Directory.CreateDirectory(output);
            var login = new LoginForm(state, output);
            AddChild(login);
            login.ShowForm();
            login.AccountInput!.Text = "Aventurero";
            login.FocusAccountInput();
            await Capture(output, "login.png");
            login.OnConnectPressed();
            if (login.StatusLabel!.Text != "Ingrese cuenta y contraseña") throw new Exception("Empty password validation failed");
            login.HideForm();
            state.CharacterList.Add(new CharacterPreview { Name = "Nier", Class = "Mago", Level = 50, Body = 517, Head = 1, Weapon = 85 });
            state.CharacterList.Add(new CharacterPreview { Name = "Centinela", Class = "Paladín", Level = 35, Body = 515, Head = 2 });
            var select = new CharSelectForm();
            select.Init(state, data);
            AddChild(select);
            select.ShowForm();
            foreach (var ch in state.CharacterList) select.CharList!.AddItem($"{ch.Name} — Lvl {ch.Level} ({ch.Class})");
            select.CharList!.Select(0);
            select.CharList.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
            int entered = 0;
            select.OnEnterPressed = () => entered++;
            select.EnterButton!.EmitSignal(Button.SignalName.Pressed);
            if (entered != 1) throw new Exception("Enter callback failed");
            select.EnterButton.Disabled = true;
            select.CharList.EmitSignal(ItemList.SignalName.ItemActivated, 0L);
            if (entered != 1) throw new Exception("Disabled enter allowed double click");
            select.EnterButton.Disabled = false;
            await Capture(output, "characters.png");
            GD.Print($"[ENTRY-UI] PASS validation, enter callback, disabled double click; captures: {output}");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private async System.Threading.Tasks.Task Capture(string output, string name)
    {
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(output, name)) != Error.Ok) throw new IOException("Screenshot failed");
    }
}

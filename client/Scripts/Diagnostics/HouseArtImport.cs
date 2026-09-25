using System;
using System.IO;
using Godot;

namespace ArgentumNextgen.Diagnostics;

// Offline asset conversion only. Never attached to the game scene.
public partial class HouseArtImport : Node
{
    public override void _Ready()
    {
        try
        {
            string root = ProjectSettings.GlobalizePath("res://../art-source/tanaris-houses");
            foreach (int id in new[] { 5501, 5502, 5505 })
            {
                using var original = Image.LoadFromFile(Path.Combine(root, $"originals/{id}.png"));
                using var generated = Image.LoadFromFile(Path.Combine(root, $"generated/{id}.png"));
                if (original == null || generated == null) throw new IOException($"Missing house sheet {id}");
                generated.Resize(original.GetWidth(), original.GetHeight(), Image.Interpolation.Lanczos);
                generated.Convert(Image.Format.Rgb8);
                // Keep the indexed sprite's original silhouette and empty slots exactly.
                // This is a layout mask, not procedural replacement artwork.
                for (int y = 0; y < original.GetHeight(); y++)
                for (int x = 0; x < original.GetWidth(); x++)
                {
                    var old = original.GetPixel(x, y);
                    if (old.A == 0 || (old.R <= 3f/255 && old.G <= 3f/255 && old.B <= 3f/255))
                        generated.SetPixel(x, y, Colors.Black);
                }
                string target = ProjectSettings.GlobalizePath($"res://../resources/data/Graficos/{id}.png");
                if (generated.SavePng(target) != Error.Ok) throw new IOException(target);
                GD.Print($"HOUSE IMPORT {id}: {generated.GetWidth()}x{generated.GetHeight()}");
            }
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Diagnostics;

public partial class HouseAssetAudit : Node
{
    public override void _Ready()
    {
        try
        {
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            var data = new GameData(); data.LoadAll(resources);
            var map = MapLoader.Load(resources, 28);
            var seen = new HashSet<int>();
            for (int y = 48; y <= 68; y++)
            for (int x = 25; x <= 39; x++)
            {
                var tile = map.Tiles.Get(x,y);
                foreach (var id in new[] {tile.Layer2, tile.Layer3, tile.Layer4})
                {
                    if (id <= 0 || !seen.Add(id)) continue;
                    var g = data.ResolveGrh(id,0);
                    if (g != null) GD.Print($"HOUSE {x},{y} grh={id} file={g.FileNum} rect={g.SX},{g.SY},{g.PixelWidth},{g.PixelHeight} layer={(tile.Layer4==id?4:tile.Layer3==id?3:2)}");
                }
            }
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

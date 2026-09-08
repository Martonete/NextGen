using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Diagnostics;

public partial class DungeonWorkshop : Node2D
{
    private readonly GameData _data = new();
    private int[] _ids = Array.Empty<int>();
    private MapData? _overview;
    private readonly ArgentumNextgen.Rendering.GrhAnimator _animator = new();
    public override async void _Ready()
    {
        try
        {
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            _data.LoadAll(resources);
            DisplayServer.WindowSetSize(new Vector2I(1000, 800));
            GetTree().Root.ContentScaleSize = new Vector2I(1000, 800);
            var output = ProjectSettings.GlobalizePath("user://dungeon-workshop");
            Directory.CreateDirectory(output);
            if(OS.GetCmdlineUserArgs().Any(a=>a=="--build-vigilia"||a=="--preview-vigilia"||a=="--refresh-vigilia"))
            {
                var map=VigiliaBuilder.Build(_data);
                string root=Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),".."));
                if(OS.GetCmdlineUserArgs().Any(a=>a=="--build-vigilia"||a=="--refresh-vigilia"))
                {
                    VigiliaBuilder.WriteNewDungeon(root,map,OS.GetCmdlineUserArgs().Contains("--refresh-vigilia"));
                    VigiliaBuilder.ConnectEntrance(root,Path.Combine(output,"town-backup"));
                }
                var loaded=MapLoader.Load(resources,205);VigiliaBuilder.Validate(loaded,_data);
                if(!VigiliaBuilder.Encode(map,false).SequenceEqual(VigiliaBuilder.Encode(loaded,false)))throw new Exception("Map roundtrip differs");
                var state=new ArgentumNextgen.Game.GameState{MapData=loaded,CurrentMap=205,UserCharIndex=1,
                    MapColorR=105,MapColorG=115,MapColorB=125,AmbientLightColor=new Color(0.48f,0.52f,0.56f)};
                state.Config.ShowNames=true;
                state.Characters[1]=new ArgentumNextgen.Game.Character{CharIndex=1,Body=1,Head=1,Heading=3};
                int ci=2;
                for(int ly=1;ly<=100;ly++)for(int lx=1;lx<=100;lx++)
                {
                    var tile=loaded.Tiles[lx,ly];if(tile.LightRange<=0)continue;
                    state.MapLights.Add(new ArgentumNextgen.Game.MapLight {X=lx,Y=ly,Range=tile.LightRange,
                        R=(byte)tile.LightR,G=(byte)tile.LightG,B=(byte)tile.LightB,Active=true});
                }
                new ArgentumNextgen.Game.LightSystem().RecalculateLights(state);
                foreach(var s in VigiliaBuilder.Spawns)
                    state.Characters[ci]=new ArgentumNextgen.Game.Character{CharIndex=ci++,Body=s.npc==1100?12:s.npc==1101?197:s.npc==1102?149:422,
                        Head=s.npc==1101?508:0,Heading=3,PosX=s.x,PosY=s.y};
                ArgentumNextgen.Game.ResolutionManager.ApplyResolution(800,600,true);
                var renderer=new ArgentumNextgen.Rendering.WorldRenderer();renderer.Init(state,_data,_animator,resources);AddChild(renderer);
                renderer.BuildRoofRegions();renderer.RebuildWaterMap();renderer.MarkLightmapDirty();
                foreach(var room in VigiliaBuilder.Rooms)
                {
                    state.UserPosX=room.x;state.UserPosY=room.y+3;
                    state.Characters[1].PosX=state.UserPosX;state.Characters[1].PosY=state.UserPosY;
                    renderer.InvalidateStaticLayers();
                    await ToSignal(GetTree().CreateTimer(0.3),SceneTreeTimer.SignalName.Timeout);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var pic=GetViewport().GetTexture().GetImage();pic.SavePng(Path.Combine(output,$"vigilia-{room.x}-{room.y}.png"));
                }
                var entrance=MapLoader.Load(resources,28);
                if(entrance.Tiles[55,48].ExitMap!=205||entrance.Tiles[54,48].Blocked)
                    throw new Exception("Live resource provider cannot resolve entrance");
                state.MapData=entrance;state.CurrentMap=28;state.MapLights.Clear();state.TileLightColors=null;
                state.MapColorR=160;state.MapColorG=160;state.MapColorB=160;
                state.Characters.Clear();state.Characters[1]=new ArgentumNextgen.Game.Character{
                    CharIndex=1,Body=1,Head=1,Heading=3,PosX=54,PosY=48};
                state.UserPosX=55;state.UserPosY=48;renderer.ResetMapVisualCaches();renderer.BuildRoofRegions();renderer.RebuildWaterMap();
                await ToSignal(GetTree().CreateTimer(0.3),SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using(var pic=GetViewport().GetTexture().GetImage())pic.SavePng(Path.Combine(output,"vigilia-entrada.png"));
                renderer.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                _overview=loaded;QueueRedraw();
                await ToSignal(GetTree().CreateTimer(0.1),SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using(var pic=GetViewport().GetTexture().GetImage())pic.SavePng(Path.Combine(output,"vigilia-plano.png"));
                GD.Print("[VIGILIA] Build/roundtrip/room renders PASS");GetTree().Quit();return;
            }
            foreach (int mapId in new[] {83, 171})
            {
                var map = MapLoader.Load(resources, mapId);
                var counts = new Dictionary<int, int>();
                for (int y=1;y<=map.Height;y++) for(int x=1;x<=map.Width;x++)
                {
                    var tile = map.Tiles[x,y];
                    foreach(int grh in new[]{tile.Layer1,tile.Layer2,tile.Layer3})
                        if(grh>0) counts[grh]=counts.GetValueOrDefault(grh)+1;
                }
                _ids=counts.OrderByDescending(p=>p.Value).Take(80).Select(p=>p.Key).ToArray();
                GD.Print($"[DUNGEON] Map {mapId}: "+string.Join(",",counts.OrderByDescending(p=>p.Value).Take(20)));
                QueueRedraw();
                await ToSignal(GetTree().CreateTimer(0.3),SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var picture=GetViewport().GetTexture().GetImage();
                picture.SavePng(Path.Combine(output,$"assets-{mapId}.png"));
            }
            var town=MapLoader.Load(resources,28);
            for(int y=45;y<=55;y++) for(int x=45;x<=55;x++)
            { var t=town.Tiles[x,y]; if(!t.Blocked&&t.ExitMap==0&&t.Layer3==0) GD.Print($"[ENTRY] {x},{y} L1={t.Layer1} L2={t.Layer2} NPC={t.NpcIndex} OBJ={t.ObjIndex}"); }
            GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(0,0,1000,800),new Color(0.12f,0.13f,0.16f));
        if(_overview!=null)
        {
            DrawString(ThemeDB.FallbackFont,new Vector2(20,28),"CRIPTA DE LA VIGILIA",fontSize:21);
            for(int y=1;y<=100;y++)for(int x=1;x<=100;x++)
            {
                var tile=_overview.Tiles[x,y];
                var color=tile.Blocked?new Color(0.17f,0.2f,0.23f):new Color(0.44f,0.49f,0.45f);
                if(tile.LightRange>0)color=new Color(0.9f,0.65f,0.25f);
                if(tile.NpcIndex>0)color=new Color(0.75f,0.25f,0.2f);
                if(tile.ExitMap>0)color=new Color(0.25f,0.8f,0.8f);
                if(tile.Layer1!=1)DrawRect(new Rect2(100+x*5,40+y*5,5,5),color);
            }
            foreach(var r in VigiliaBuilder.Rooms)
                DrawString(ThemeDB.FallbackFont,new Vector2(100+r.x*5-r.name.Length*3,40+(r.y-r.h/2)*5),r.name,fontSize:12);
            DrawString(ThemeDB.FallbackFont,new Vector2(20,580),"Rojo: enemigos   |   Ambar: luces   |   Turquesa: regreso a Tanaris",fontSize:15);
            return;
        }
        for(int i=0;i<_ids.Length;i++)
        {
            Vector2 at=new(i%10*100,i/10*100);
            var grh=_data.ResolveGrh(_ids[i],0); if(grh==null) continue;
            var texture=_data.Textures?.GetTexture(grh.FileNum); if(texture==null)continue;
            float scale=Math.Min(1,Math.Min(90f/grh.PixelWidth,75f/grh.PixelHeight));
            DrawTextureRectRegion(texture,new Rect2(at+new Vector2(5,3),new Vector2(grh.PixelWidth,grh.PixelHeight)*scale),
                new Rect2(grh.SX,grh.SY,grh.PixelWidth,grh.PixelHeight));
            DrawString(ThemeDB.FallbackFont,at+new Vector2(4,94),_ids[i].ToString(),fontSize:16);
        }
    }
    public override void _Process(double delta){if(_data.IsLoaded)_animator.Update((float)delta,_data);}
}

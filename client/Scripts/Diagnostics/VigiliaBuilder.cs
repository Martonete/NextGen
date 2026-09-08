using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Deterministic authored dungeon. Explicit diagnostic command only; never runs at login.</summary>
public static class VigiliaBuilder
{
    public const int MapId = 205;
    public static readonly (int x,int y,int w,int h,string name)[] Rooms = {
        (50,85,15,11,"Vestibulo"), (50,65,21,13,"Galeria de la Vigilia"),
        (22,65,17,13,"Osario"), (78,65,17,13,"Deposito funerario"),
        (22,38,17,15,"Capilla rota"), (50,38,21,15,"Sala del juramento"),
        (78,38,17,15,"Guardia sepulcral"), (50,13,25,15,"Trono del custodio") };
    public static readonly (int x,int y,int npc)[] Spawns = {
        (46,65,1100),(55,63,1100),(21,63,1100),(25,68,1101),(76,62,1100),(81,68,1101),
        (22,51,1100),(78,51,1100),(18,36,1101),(26,41,1102),(45,36,1101),(55,41,1102),
        (75,35,1101),(82,41,1102),(50,26,1101),(45,16,1101),(55,16,1101),(50,10,1103) };

    public static MapData Build(GameData data)
    {
        var map = new MapData(100,100);
        for(int y=1;y<=100;y++) for(int x=1;x<=100;x++) map.Tiles[x,y]=new MapTile{Blocked=true,Layer1=1};
        void Floor(int x,int y) { map.Tiles[x,y]=new MapTile {Layer1=9428+(y%4)*4+x%4,Trigger=1}; }
        foreach(var r in Rooms)
            for(int y=r.y-r.h/2;y<=r.y+r.h/2;y++) for(int x=r.x-r.w/2;x<=r.x+r.w/2;x++) Floor(x,y);
        void Link(int ax,int ay,int bx,int by)
        {
            for(int y=Math.Min(ay,by)-1;y<=Math.Max(ay,by)+1;y++)
                for(int x=Math.Min(ax,bx)-1;x<=Math.Max(ax,bx)+1;x++) Floor(x,y);
        }
        Link(50,85,50,65);Link(22,65,78,65);Link(22,65,22,38);Link(78,65,78,38);
        Link(22,38,78,38);Link(50,65,50,38);Link(50,38,50,13);
        // Direction-specific masonry; upper walls have three visible courses.
        for(int y=2;y<98;y++) for(int x=2;x<99;x++)
        {
            if(!map.Tiles[x,y].Blocked) continue;
            ref var tile=ref map.Tiles[x,y];
            if(!map.Tiles[x,y+1].Blocked) tile.Layer1=9356+x%4;
            else if(!map.Tiles[x,y+2].Blocked) tile.Layer1=9352+x%4;
            else if(!map.Tiles[x,y+3].Blocked) tile.Layer1=9348+x%4;
            else if(!map.Tiles[x-1,y].Blocked) tile.Layer1=9370+(y%4)*4;
            else if(!map.Tiles[x+1,y].Blocked) tile.Layer1=9369+(y%4)*4;
            else if(!map.Tiles[x,y-1].Blocked) tile.Layer1=9390;
        }
        void Decor(int x,int y,int grh)
        { ref var t=ref map.Tiles[x,y];t.Layer3=grh;t.Blocked=true; }
        void Lamp(int x,int y,bool cold=false)
        {
            Decor(x,y,28742);ref var t=ref map.Tiles[x,y];t.LightRange=5;
            t.LightR=(short)(cold?75:235);t.LightG=(short)(cold?145:153);t.LightB=(short)(cold?160:65);
        }
        foreach(var r in Rooms)
        {
            Lamp(r.x-r.w/2+1,r.y-r.h/2+1,r.y<45);
            Lamp(r.x+r.w/2-1,r.y-r.h/2+1,r.y<45);
            // Recessed tombs leave the central fighting and circulation spaces open.
            Decor(r.x-r.w/2+1,r.y+2,29949);
            Decor(r.x+r.w/2-1,r.y+2,29951);
        }
        foreach(int y in new[]{60,65,70}) {Decor(18,y,29950);Decor(26,y,29952);}
        foreach(int x in new[]{18,26}) {Decor(x,34,29939);Decor(x,43,29949);}
        Decor(50,7,29939);Decor(41,10,29950);Decor(59,10,29950);
        Decor(74,69,502);Decor(82,69,502);
        foreach(var p in new[]{(74,67,38),(82,67,37),(20,42,38),(24,42,37)})
        {map.Tiles[p.Item1,p.Item2].ObjIndex=(short)p.Item3;map.Tiles[p.Item1,p.Item2].ObjAmount=12;}
        // Arrival never sits on an exit. The return stairs are two tiles south.
        map.Tiles[50,89].ExitMap=28;map.Tiles[50,89].ExitX=54;map.Tiles[50,89].ExitY=48;
        Lamp(48,89);Lamp(52,89);
        foreach(var spawn in Spawns) map.Tiles[spawn.x,spawn.y].NpcIndex=(short)spawn.npc;
        Validate(map,data);
        return map;
    }

    public static void Validate(MapData map,GameData data)
    {
        var visited=new HashSet<(int,int)>();var queue=new Queue<(int,int)>();queue.Enqueue((50,87));visited.Add((50,87));
        while(queue.Count>0)
        {
            var (x,y)=queue.Dequeue();
            foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {int nx=x+dx,ny=y+dy;if(nx<1||ny<1||nx>100||ny>100||map.Tiles[nx,ny].Blocked)continue;
                if(visited.Add((nx,ny)))queue.Enqueue((nx,ny));}
        }
        int walkable=0;
        for(int y=1;y<=100;y++)for(int x=1;x<=100;x++)
        {
            var t=map.Tiles[x,y];if(!t.Blocked)walkable++;
            foreach(int grh in new[]{t.Layer1,t.Layer2,t.Layer3})
                if(grh>0&&data.ResolveGrh(grh,0)==null)throw new Exception($"Missing GRH {grh}");
            if((t.NpcIndex>0||t.ObjIndex>0||t.ExitMap>0)&&!visited.Contains((x,y)))throw new Exception($"Unreachable content {x},{y}");
        }
        if(walkable!=visited.Count)throw new Exception("Disconnected floor");
        foreach(var r in Rooms)if(!visited.Contains((r.x,r.y)))throw new Exception("Unreachable room "+r.name);
        if(map.Tiles[50,87].ExitMap>0)throw new Exception("Exit loop");
        Godot.GD.Print($"[VIGILIA] PASS: {walkable} connected tiles, {Rooms.Length} rooms, {Spawns.Length} NPC spawns.");
    }

    public static byte[] Encode(MapData map,bool info)
    {
        using var ms=new MemoryStream();using var w=new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(info?"AOINF\0":"AOMAP\0"));w.Write((ushort)1);
        w.Write((ushort)map.Width);w.Write((ushort)map.Height);w.Write(0u);
        for(int y=1;y<=map.Height;y++)for(int x=1;x<=map.Width;x++)
        {
            var t=map.Tiles[x,y];
            if(info)
            {
                w.Write((byte)((t.ExitMap>0?1:0)|(t.NpcIndex>0?2:0)|(t.ObjIndex>0?4:0)));
                if(t.ExitMap>0){w.Write(t.ExitMap);w.Write(t.ExitX);w.Write(t.ExitY);}
                if(t.NpcIndex>0)w.Write(t.NpcIndex);
                if(t.ObjIndex>0){w.Write(t.ObjIndex);w.Write(t.ObjAmount);}
            }
            else
            {
                w.Write((byte)((t.Blocked?1:0)|(t.Layer2!=0?2:0)|(t.Layer3!=0?4:0)|(t.Layer4!=0?8:0)
                    |(t.Trigger!=0?16:0)|(t.ParticleGroup!=0?32:0)|(t.LightRange>0?64:0)|(t.AnimatedWater?128:0)));
                w.Write(t.Layer1);if(t.Layer2!=0)w.Write(t.Layer2);if(t.Layer3!=0)w.Write(t.Layer3);if(t.Layer4!=0)w.Write(t.Layer4);
                if(t.Trigger!=0)w.Write(t.Trigger);if(t.ParticleGroup!=0)w.Write(t.ParticleGroup);
                if(t.LightRange>0){w.Write(t.LightRange);w.Write(t.LightR);w.Write(t.LightG);w.Write(t.LightB);}
            }
        }
        return ms.ToArray();
    }

    public static void WriteNewDungeon(string root,MapData map,bool replaceGenerated=false)
    {
        foreach(string folder in new[]{"resources/data/Maps","server/maps"})
            foreach(bool info in new[]{false,true})
            {
                string path=Path.Combine(root,folder,$"Mapa205.{(info?"aoinf":"aomap")}");
                byte[] bytes=Encode(map,info);
                if(File.Exists(path)) {if(!File.ReadAllBytes(path).SequenceEqual(bytes))
                    {if(!replaceGenerated)throw new Exception("Refusing to overwrite edited dungeon: "+path);File.WriteAllBytes(path,bytes);}}
                else {using var file=new FileStream(path,FileMode.CreateNew);file.Write(bytes);}
            }
    }

    public static void ConnectEntrance(string root,string backup)
    {
        var provider=new FileResourceProvider(Path.Combine(root,"resources/data"));
        var town=MapLoader.Load(provider,28);
        if(town.Tiles[55,48].ExitMap==205)return;
        var entry=town.Tiles[55,48];var marker=town.Tiles[55,47];
        if(entry.Blocked||entry.ExitMap!=0||entry.ObjIndex!=0||entry.NpcIndex!=0||marker.Layer3!=0||marker.NpcIndex!=0)
            throw new Exception("Entrance tile occupied; refusing to replace it");
        // Patch only the selected variable-length records, preserving all other bytes.
        foreach(bool info in new[]{false,true})
        {
            string name=$"Mapa28.{(info?"aoinf":"aomap")}";
            byte[] old=File.ReadAllBytes(Path.Combine(root,"resources/data/Maps",name));
            if(!old.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"server/maps",name))))
                throw new Exception("Town server/client copies differ: "+name);
            Directory.CreateDirectory(backup);
            string saved=Path.Combine(backup,name);if(!File.Exists(saved))File.WriteAllBytes(saved,old);
        }
        town.Tiles[55,48].ExitMap=205;town.Tiles[55,48].ExitX=50;town.Tiles[55,48].ExitY=87;
        town.Tiles[55,47].Layer3=29939;town.Tiles[55,47].Blocked=true;
        foreach(bool info in new[]{false,true})
        {
            string name=$"Mapa28.{(info?"aoinf":"aomap")}";
            byte[] original=File.ReadAllBytes(Path.Combine(root,"resources/data/Maps",name));
            byte[] updated=PatchRecord(original,info,55,info?48:47,town.Tiles[55,info?48:47]);
            foreach(string folder in new[]{"resources/data/Maps","server/maps"})File.WriteAllBytes(Path.Combine(root,folder,name),updated);
        }
    }

    private static byte[] PatchRecord(byte[] bytes,bool info,int targetX,int targetY,MapTile tile)
    {
        using var reader=new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position=8;int width=reader.ReadUInt16(),height=reader.ReadUInt16();reader.ReadUInt32();
        for(int y=1;y<=height;y++)for(int x=1;x<=width;x++)
        {
            int start=(int)reader.BaseStream.Position;byte f=reader.ReadByte();
            int length=info?((f&1)!=0?6:0)+((f&2)!=0?2:0)+((f&4)!=0?4:0)
                :4+((f&2)!=0?4:0)+((f&4)!=0?4:0)+((f&8)!=0?4:0)+((f&16)!=0?2:0)+((f&32)!=0?2:0)+((f&64)!=0?8:0);
            reader.BaseStream.Position+=length;
            if(x!=targetX||y!=targetY)continue;
            var single=new MapData(1,1);single.Tiles[1,1]=tile;byte[] record=Encode(single,info);
            using var result=new MemoryStream();result.Write(bytes,0,start);result.Write(record,16,record.Length-16);
            int end=(int)reader.BaseStream.Position;result.Write(bytes,end,bytes.Length-end);return result.ToArray();
        }
        throw new Exception("Missing entrance record");
    }
}

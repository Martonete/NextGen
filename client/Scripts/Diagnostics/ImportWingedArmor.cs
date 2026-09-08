using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Explicit offline import and visual QA for the Eclipse armor. Never runs in gameplay.</summary>
public partial class ImportWingedArmor : Node2D
{
    private readonly GameData _data = new();
    private readonly GrhAnimator _animator = new();
    private readonly GameState _state = new();
    public override async void _Ready()
    {
        try
        {
            string root = ProjectSettings.GlobalizePath("res://../");
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--import") >= 0) Import(root);
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            if (_data.Bodies.Length <= 514 || _data.Objects.Length <= 1670) throw new Exception("Armor catalog missing");
            for (int d = 1; d <= 4; d++)
                if (_data.Grhs[_data.Bodies[514].Walk[d]].NumFrames != 4) throw new Exception("Walk animation missing");
            _state.Config.ShowShadows = false;
            _state.Config.ShowNames = false;
            QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var capture = GetViewport().GetTexture().GetImage();
            capture.SavePng(Path.Combine(root,"resources/art-source/eclipse-alado/preview.png"));
            GD.Print("[WINGED-ARMOR] PASS body=514 object=1670 directions=4 frames=16");
            GetTree().Quit();
        }
        catch(Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private static void Import(string root)
    {
        string init = Path.Combine(root,"resources/data/INIT");
        string graphics = Path.Combine(root,"resources/data/Graficos/45000.png");
        if (File.Exists(graphics)) throw new Exception("Import already exists; refusing to overwrite");
        using var source = Image.LoadFromFile(Path.Combine(root,"resources/art-source/eclipse-alado/source.png"));
        if (source.GetPixel(0,0).A != 0) throw new Exception("Source must have genuine alpha");
        var provider = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
        var grhs = GrhLoader.Load(provider);
        for (int id=33400; id<=33420; id++)
            if (id<grhs.Length && grhs[id].NumFrames>0) throw new Exception("GRH collision " + id);
        byte[] bodyBytes = File.ReadAllBytes(Path.Combine(init,"Personajes.ind"));
        if (BitConverter.ToInt16(bodyBytes,263)!=513 || bodyBytes.Length!=265+513*12) throw new Exception("Unexpected body catalog");
        // Register all cells to a shared neck and baseline; no runtime crop compensation.
        using var atlas = Image.CreateEmpty(256,256,false,Image.Format.Rgba8);
        int[] neckX = {158,183,158,124};
        int[] neckY = {98,56,70,42};
        int[] footY = {306,297,286,271};
        for(int row=0;row<4;row++)
        for(int col=0;col<4;col++)
        {
            int x0=col*source.GetWidth()/4, x1=(col+1)*source.GetWidth()/4;
            int y0=row*source.GetHeight()/4, y1=(row+1)*source.GetHeight()/4;
            using var cell=source.GetRegion(new Rect2I(x0,y0,x1-x0,y1-y0));
            float scale=34f/(footY[row]-neckY[row]);
            cell.Resize((int)MathF.Round(cell.GetWidth()*scale),(int)MathF.Round(cell.GetHeight()*scale),Image.Interpolation.Nearest);
            atlas.BlitRect(cell,new Rect2I(0,0,cell.GetWidth(),cell.GetHeight()),
                new Vector2I(col*64+32-(int)MathF.Round(neckX[row]*scale),row*64+26-(int)MathF.Round(neckY[row]*scale)));
        }
        using var icon=atlas.GetRegion(new Rect2I(64,128,64,64));
        icon.Resize(32,32,Image.Interpolation.Nearest);
        using var packed=Image.CreateEmpty(288,256,false,Image.Format.Rgba8);
        packed.BlitRect(atlas,new Rect2I(0,0,256,256),Vector2I.Zero);
        packed.BlitRect(icon,new Rect2I(0,0,32,32),new Vector2I(256,0));
        string backup=Path.Combine(root,"resources/art-source/eclipse-alado/original-index");
        Directory.CreateDirectory(backup);
        foreach(string file in new[]{"Graficos.ind","Personajes.ind"}) File.Copy(Path.Combine(init,file),Path.Combine(backup,file),false);
        if(packed.SavePng(graphics)!=Error.Ok) throw new Exception("Atlas save failed");
        using(var writer=new BinaryWriter(File.Open(Path.Combine(init,"Graficos.ind"),FileMode.Append)))
        {
            for(int i=0;i<16;i++) Static(writer,33400+i,i%4*64,i/4*64,64,64);
            for(int d=0;d<4;d++)
            {
                writer.Write(33416+d); writer.Write((short)4);
                foreach(int frame in new[]{1,0,3,2}) writer.Write(33400+d*4+frame);
                writer.Write(444f);
            }
            Static(writer,33420,256,0,32,32);
        }
        using(var writer=new BinaryWriter(File.Open(Path.Combine(init,"Personajes.ind"),FileMode.Open)))
        {
            writer.BaseStream.Position=263; writer.Write((short)514);
            writer.BaseStream.Position=writer.BaseStream.Length;
            for(int d=0;d<4;d++) writer.Write((ushort)(33416+d));
            writer.Write((short)0); writer.Write((short)-38);
        }
    }
    private static void Static(BinaryWriter w,int id,int x,int y,int width,int height)
    {
        w.Write(id);w.Write((short)1);w.Write(45000);w.Write((short)x);w.Write((short)y);w.Write((short)width);w.Write((short)height);
    }
    public override void _Draw()
    {
        if(_data.Bodies.Length<=514)return;
        DrawRect(new Rect2(0,0,800,600),new Color(.12f,.16f,.17f));
        for(int d=1;d<=4;d++)
        for(int f=0;f<4;f++)
        {
            var pos=new Vector2(110+f*180,100+(d-1)*130);
            RunicAuraRenderer.Draw(this,_data.Auras[97],pos+new Vector2(16,27),f*400,front:false);
            CharRenderer.DrawCharacter(this,new Character {Body=514,Head=1,Heading=d,Moving=true,WalkFrame=f,FovAlpha=1},pos,_data,_animator,state:_state);
            RunicAuraRenderer.Draw(this,_data.Auras[97],pos+new Vector2(16,27),f*400,front:true);
        }
    }
}

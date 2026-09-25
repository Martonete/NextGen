using System;
using System.IO;
using System.Text;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Offline asset import/QA. Run explicitly; never attached to gameplay.</summary>
public partial class ImportFiveRelics : Node2D
{
    private static readonly string[] Keys = {"solar","abyss","astral","sword","staff"};
    private static readonly string[] Names = {"Armadura del Alba Solar","Armadura del Guardian Abisal","Tunica del Cartografo Astral","Espada Corazon Carmesi","Baculo de la Raiz Ancestral"};
    private static readonly int[] Auras = {99,100,101,102,98};
    private readonly GameData _data = new();
    private readonly GrhAnimator _animator = new();
    private readonly GameState _state = new();
    private bool _ready;
    public override async void _Ready()
    {
        try
        {
            string root=ProjectSettings.GlobalizePath("res://../");
            if(Array.IndexOf(OS.GetCmdlineUserArgs(),"--import")>=0) Import(root);
            if(Array.IndexOf(OS.GetCmdlineUserArgs(),"--refresh-atlases")>=0)
            {
                string art=Path.Combine(root,"resources/art-source/five-relics");
                if(!File.Exists(Path.Combine(art,"original-data/Armas.dat")))throw new Exception("Missing import ownership backup");
                BuildAtlases(root,art);
                for(int i=0;i<5;i++)File.Copy(Path.Combine(art,Keys[i]+"-atlas.png"),Path.Combine(root,$"resources/data/Graficos/{45001+i}.png"),true);
            }
            _data.LoadAll(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
            if(_data.Bodies.Length<518 || _data.Weapons.Length<86 || _data.Objects.Length<1676) throw new Exception("Catalog counts mismatch");
            for(int i=0;i<5;i++)
            {
                if(_data.Objects[1671+i].Name!=Names[i] || _data.Objects[1671+i].CreaAura!=Auras[i]) throw new Exception("Object/aura mismatch");
                for(int d=1;d<=4;d++)
                {
                    int grh=i<3?_data.Bodies[515+i].Walk[d]:_data.Weapons[84+i-3].Walk[d];
                    if(_data.Grhs[grh].NumFrames!=4)throw new Exception("Missing walk cycle");
                    for(int f=0;f<4;f++) if(_data.ResolveGrh(grh,f)?.FileNum!=45001+i)throw new Exception("Wrong frame texture");
                }
            }
            _state.Config.ShowShadows=false;_state.Config.ShowNames=false;
            _ready=true;
            for(int frame=0;frame<4;frame++)
            {
                _frame=frame;QueueRedraw();
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var capture=GetViewport().GetTexture().GetImage();
                if(capture.SavePng(Path.Combine(root,$"resources/art-source/five-relics/preview-{frame}.png"))!=Error.Ok)throw new Exception("Capture failed");
            }
            GD.Print("[FIVE-RELICS] PASS 5 objects, 3 bodies, 2 weapons, 80 walk frames, 5 icons");
            GetTree().Quit();
        }
        catch(Exception ex){GD.PrintErr(ex);GetTree().Quit(1);}
    }
    private int _frame;
    private static void Import(string root)
    {
        string init=Path.Combine(root,"resources/data/INIT");
        string art=Path.Combine(root,"resources/art-source/five-relics");
        var grhs=GrhLoader.Load(ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data")));
        for(int id=33430;id<33535;id++) if(id<grhs.Length&&grhs[id].NumFrames>0)throw new Exception("GRH collision "+id);
        byte[] bodies=File.ReadAllBytes(Path.Combine(init,"Personajes.ind"));
        if(BitConverter.ToInt16(bodies,263)!=514 || bodies.Length!=265+514*12)throw new Exception("Unexpected body catalog");
        string weapons=File.ReadAllText(Path.Combine(init,"Armas.dat"));
        if(!weapons.Contains("NumArmas=83")||weapons.Contains("[Arma84]"))throw new Exception("Unexpected weapon catalog");
        string[] objPaths={"server/dat/obj.dat","resources/data/INIT/obj.dat","client/Data/INIT/obj.dat"};
        foreach(string p in objPaths)
        {
            string content=File.ReadAllText(Path.Combine(root,p),Encoding.Latin1);
            if(!content.Contains("NumOBJs=1670")||content.Contains("[OBJ1671]"))throw new Exception("Unexpected object catalog "+p);
        }
        for(int i=0;i<5;i++) if(File.Exists(Path.Combine(root,$"resources/data/Graficos/{45001+i}.png")))throw new Exception("Texture exists");
        BuildAtlases(root,art);
        string backup=Path.Combine(art,"original-data");Directory.CreateDirectory(backup);
        foreach(string file in new[]{"Graficos.ind","Personajes.ind","Armas.dat"})File.Copy(Path.Combine(init,file),Path.Combine(backup,file),false);
        for(int i=0;i<objPaths.Length;i++)File.Copy(Path.Combine(root,objPaths[i]),Path.Combine(backup,$"obj-{i}.dat"),false);
        using(var w=new BinaryWriter(File.Open(Path.Combine(init,"Graficos.ind"),FileMode.Append)))
        for(int i=0;i<5;i++)
        {
            int start=33430+i*21;
            File.Copy(Path.Combine(art,Keys[i]+"-atlas.png"),Path.Combine(root,$"resources/data/Graficos/{45001+i}.png"),false);
            for(int f=0;f<16;f++)Static(w,start+f,45001+i,f%4*64,f/4*64,64,64);
            for(int d=0;d<4;d++)
            {
                w.Write(start+16+d);w.Write((short)4);
                for(int f=0;f<4;f++)w.Write(start+d*4+f);
                w.Write(444f);
            }
            Static(w,start+20,45001+i,256,0,32,32);
        }
        using(var w=new BinaryWriter(File.Open(Path.Combine(init,"Personajes.ind"),FileMode.Open)))
        {
            w.BaseStream.Position=263;w.Write((short)517);w.BaseStream.Position=w.BaseStream.Length;
            for(int i=0;i<3;i++)
            {
                for(int d=0;d<4;d++)w.Write((ushort)(33430+i*21+16+d));
                w.Write((short)0);w.Write((short)-40);
            }
        }
        weapons=weapons.Replace("NumArmas=83","NumArmas=85").TrimEnd()+"\r\n";
        for(int i=3;i<5;i++)
        {
            weapons+=$"\r\n[Arma{81+i}]\r\n";
            for(int d=1;d<=4;d++)weapons+=$"Dir{d}={33430+i*21+15+d}\r\n";
        }
        File.WriteAllText(Path.Combine(init,"Armas.dat"),weapons,new UTF8Encoding(false));
        string entries="";
        for(int i=0;i<5;i++)
        {
            entries+=$"\r\n[OBJ{1671+i}]\r\nName={Names[i]}\r\nGrhIndex={33450+i*21}\r\nObjType={(i<3?3:2)}\r\nAgarrable=0\r\nCreaAura={Auras[i]}\r\nValor=180000\r\n";
            entries+=i<3?$"NumRopaje={515+i}\r\nMINDEF={(i==2?16:28)}\r\nMAXDEF={(i==2?22:35)}\r\n":$"Anim={81+i}\r\nMinHIT={(i==3?18:5)}\r\nMaxHIT={(i==3?24:9)}\r\n";
            if(i==4)entries+="StaffPower=1\r\nStaffDamageBonus=10\r\n";
        }
        foreach(string p in objPaths)
        {
            string path=Path.Combine(root,p);
            string content=File.ReadAllText(path,Encoding.Latin1).Replace("NumOBJs=1670","NumOBJs=1675");
            File.WriteAllText(path,content.TrimEnd()+"\r\n"+entries,Encoding.Latin1);
        }
    }
    private static void BuildAtlases(string root,string art)
    {
        // Prepare all images and check transparency before altering any shared catalog.
        for(int i=0;i<5;i++)
        {
            using var src=Image.LoadFromFile(Path.Combine(art,Keys[i]+".png"));
            if(src.GetPixel(0,0).A!=0)throw new Exception("Opaque background: "+Keys[i]);
            using var atlas=Image.CreateEmpty(288,256,false,Image.Format.Rgba8);
            for(int row=0;row<4;row++) for(int col=0;col<4;col++)
            {
                int x0=col*src.GetWidth()/4,y0=row*src.GetHeight()/4;
                using var cell=src.GetRegion(new Rect2I(x0,y0,(col+1)*src.GetWidth()/4-x0,(row+1)*src.GetHeight()/4-y0));
                Rect2I bounds=VisibleBounds(cell);
                if(bounds.Size.X<10||bounds.Size.Y<30)throw new Exception("Empty cell");
                using var sprite=cell.GetRegion(bounds);
                float neckFraction=i==1?.12f:.045f;
                float scale=i<3?36f/(bounds.Size.Y*(1-neckFraction)):(i==3?39f:49f)/bounds.Size.Y;
                int width=Math.Max(1,(int)MathF.Round(bounds.Size.X*scale)),height=Math.Max(1,(int)MathF.Round(bounds.Size.Y*scale));
                sprite.Resize(width,height,Image.Interpolation.Nearest);
                int targetX,targetY;
                if(i<3)
                {
                    targetX=32-width/2;targetY=60-height;
                }
                else
                {
                    // Anatomical right hand projected in N/E/S/W. Grip follows the walk sway.
                    int[] handX={40,35,24,20};
                    int sway=col==1?-1:col==3?1:0;
                    targetX=handX[row]-width/2;targetY=42+sway-(int)MathF.Round(height*(i==3?.81f:.65f));
                }
                if(targetX<0||targetY<0||targetX+width>64||targetY+height>64)throw new Exception($"Cell would clip: {Keys[i]} row={row} col={col} bounds={bounds} dest={targetX},{targetY},{width},{height}");
                atlas.BlitRect(sprite,new Rect2I(0,0,width,height),new Vector2I(col*64+targetX,row*64+targetY));
            }
            using var icon=atlas.GetRegion(new Rect2I(0,128,64,64));
            icon.Resize(32,32,Image.Interpolation.Nearest);
            atlas.BlitRect(icon,new Rect2I(0,0,32,32),new Vector2I(256,0));
            if(atlas.SavePng(Path.Combine(art,Keys[i]+"-atlas.png"))!=Error.Ok)throw new Exception("Cannot save staged atlas");
        }
    }
    private static void Static(BinaryWriter w,int id,int file,int x,int y,int width,int height)
    {w.Write(id);w.Write((short)1);w.Write(file);w.Write((short)x);w.Write((short)y);w.Write((short)width);w.Write((short)height);}
    private static Rect2I VisibleBounds(Image image)
    {
        int left=image.GetWidth(),top=image.GetHeight(),right=-1,bottom=-1;
        for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)
        {
            // Very faint extraction residue must not determine sprite registration.
            if(image.GetPixel(x,y).A<.12f)continue;
            left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);
        }
        return right<left?new Rect2I():new Rect2I(left,top,right-left+1,bottom-top+1);
    }
    public override void _Draw()
    {
        if(!_ready)return;
        DrawRect(new Rect2(0,0,800,600),new Color(.12f,.16f,.17f));
        for(int i=0;i<5;i++)
        {
            DrawString(ThemeDB.FallbackFont,new Vector2(10+i*160,24),$"{1671+i}: {Keys[i]}",fontSize:14);
            for(int d=1;d<=4;d++)
            {
                var pos=new Vector2(58+i*160,90+(d-1)*130);
                var ch=new Character {Body=i<3?515+i:i==3?515:517,Head=1,WeaponAnim=i>=3?81+i:0,Heading=d,Moving=true,WalkFrame=_frame,FovAlpha=1};
                RunicAuraRenderer.Draw(this,_data.Auras[Auras[i]],pos+new Vector2(16,27),_frame*400,front:false);
                CharRenderer.DrawCharacter(this,ch,pos,_data,_animator,state:_state);
                RunicAuraRenderer.Draw(this,_data.Auras[Auras[i]],pos+new Vector2(16,27),_frame*400,front:true);
            }
        }
    }
}

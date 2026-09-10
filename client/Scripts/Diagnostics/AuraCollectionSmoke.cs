using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

public partial class AuraCollectionSmoke : Node2D
{
    private readonly GameData _data = new();
    private readonly GameState _state = new();
    private readonly GrhAnimator _animator = new();
    private readonly ParticleSystem _particles = new();
    public override async void _Ready()
    {
        try
        {
            var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
            _data.LoadAll(resources);
            _particles.LoadDefinitions(resources, "INIT/Particles.ini", _state);
            if (_data.Auras.Length < 104 || _state.ParticleDefs.Length < 114) throw new Exception("Catalog incomplete");
            var previewCharacter = new Character { AuraIndexW = 12 };
            _state.Characters[_state.UserCharIndex] = previewCharacter;
            int sentPackets = 0;
            var chat = new ChatSystem(_state)
            {
                SendPacket = _ => sentPackets++,
                OnAuraPreview = arg => AuraPreviewCommand.Apply(_state, _data, arg)
            };
            chat.OnChatSubmitted("/aura");
            if (previewCharacter.PreviewAuraIndex != 97) throw new Exception("Preview initial aura mismatch");
            chat.OnChatSubmitted("/AURA siguiente");
            if (previewCharacter.PreviewAuraIndex != 98) throw new Exception("Preview next mismatch");
            chat.OnChatSubmitted("/aura anterior");
            chat.OnChatSubmitted("/aura 999999");
            if (previewCharacter.PreviewAuraIndex != 97) throw new Exception("Invalid preview changed selection");
            chat.OnChatSubmitted("/aura 102");
            if (previewCharacter.PreviewAuraIndex != 102) throw new Exception("Preview explicit ID mismatch");
            if (_data.Auras[103].ProceduralStyle != 5) throw new Exception("GM teleport aura style mismatch");
            chat.OnChatSubmitted("/aura off");
            if (previewCharacter.PreviewAuraIndex != 0 || previewCharacter.AuraIndexW != 12 || sentPackets != 0)
                throw new Exception("Preview altered equipment or sent packets");
            _state.Characters.Clear();
            for (int i = 0; i < 6; i++)
            {
                if (_data.Auras[97+i].ProceduralStyle == 0) throw new Exception("Aura style missing");
                var def = _state.ParticleDefs[108+i];
                if (def.NumParticles > 20 || !def.FadeAlpha || !def.ScaleOverLife) throw new Exception("Particle budget/flags mismatch");
                foreach (int grh in def.GrhList)
                    if (_data.ResolveGrh(grh,0)?.FileNum <= 0) throw new Exception("Missing particle sprite");
                ParticleSystem.CreateMapStream(_state,108+i,1,1);
            }
            for (int i = 0; i < 120; i++) _particles.Update(1f/60,_state);
            foreach (var stream in _state.MapParticles)
                if (!Array.Exists(stream.Particles,p=>p.Alive)) throw new Exception("Empty particle preset");
            QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            string path = ProjectSettings.GlobalizePath("user://coleccion-vigilia.png");
            if (image.SavePng(path) != Error.Ok) throw new Exception("Capture failed");
            GD.Print("[AURA-COLLECTION] PASS " + path);
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
    public override void _Draw()
    {
        if (!_data.IsLoaded || _state.MapParticles.Count < 6) return;
        DrawRect(new Rect2(0,0,800,600),new Color(.06f,.08f,.12f));
        string[] names = {"Eclipse de Obsidiana","Espinas de Jade","Corona Solar","Agujas de Escarcha","Orbita Astral","Ascua Carmesi"};
        for (int i = 0; i < 6; i++)
        {
            Vector2 pos = new(105 + i%3*265,90 + i/3*260);
            DrawString(ThemeDB.FallbackFont,pos+new Vector2(-80,-45),$"{97+i}: {names[i]}",fontSize:14);
            RunicAuraRenderer.Draw(this,_data.Auras[97+i],pos+new Vector2(16,27),1350,front:false);
            CharRenderer.DrawCharacter(this,new Character {Body=1,Head=1,Heading=3,FovAlpha=1},pos,_data,_animator);
            RunicAuraRenderer.Draw(this,_data.Auras[97+i],pos+new Vector2(16,27),1350,front:true);
            DrawString(ThemeDB.FallbackFont,pos+new Vector2(-80,80),$"Particula {108+i}",fontSize:14);
            var def = _state.ParticleDefs[108+i];
            foreach (var p in _state.MapParticles[i].Particles)
            {
                if (!p.Alive) continue;
                float progress = p.MaxLife > 0 ? Math.Clamp(1-p.Life/p.MaxLife,0,1) : 0;
                float scale = def.ResizeX + (def.ResizeY-def.ResizeX)*progress;
                CharRenderer.DrawEffectGrh(this,_data,p.GrhIndex,0,pos+new Vector2(16+p.X,150+p.Y),
                    new Color(p.ColR/255f,p.ColG/255f,p.ColB/255f,1-progress),scale:scale);
            }
        }
    }
}

using System;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

public partial class ParticleCatalogSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            string root = ProjectSettings.GlobalizePath("res://../../resources/data/INIT/Particles.ini");
            var engine = new ParticleEngine();
            engine.LoadDefinitions(root);
            if (engine.Defs.Length < 114) throw new Exception("Missing catalog");
            for (int id = 108; id <= 113; id++)
            {
                var def = engine.Defs[id];
                if (def.NumParticles > 20 || def.Name.Length == 0 || !def.FadeAlpha || !def.ScaleOverLife)
                    throw new Exception("Invalid preset " + id);
                var stream = new EditorParticleStream {DefIndex=id, Particles=new EditorParticle[def.NumParticles]};
                for (int i=0;i<stream.Particles.Length;i++) stream.Particles[i]=new EditorParticle();
                for(int i=0;i<120;i++) ParticleEngine.UpdateSingleStream(stream,def,1000f/60);
                if (!Array.Exists(stream.Particles,p=>p.Alive)) throw new Exception("No live particles " + id);
            }
            string temp = ProjectSettings.GlobalizePath("user://particle-catalog-roundtrip.ini");
            engine.SaveDefinitions(temp);
            var copy = new ParticleEngine(); copy.LoadDefinitions(temp);
            for (int id=108;id<=113;id++)
                if (copy.Defs[id].Name != engine.Defs[id].Name || !copy.Defs[id].ScaleOverLife)
                    throw new Exception("Roundtrip failed");
            GD.Print("[PARTICLE-CATALOG] PASS 108..113 load/simulate/save/reload; source untouched");
            GetTree().Quit();
        }
        catch(Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }
}

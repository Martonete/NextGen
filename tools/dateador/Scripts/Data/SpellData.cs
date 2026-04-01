#nullable enable
using System.Collections.Generic;
using System.IO;

namespace AODateador.Data;

public class SpellData
{
    public int Index { get; set; }

    // Identification
    public string Nombre { get; set; } = string.Empty;
    public string Desc { get; set; } = string.Empty;
    public string PalabrasMagicas { get; set; } = string.Empty;

    // Messages
    public string HechizeroMsg { get; set; } = string.Empty;
    public string TargetMsg { get; set; } = string.Empty;
    public string PropioMsg { get; set; } = string.Empty;

    // Classification
    /// <summary>1=Properties, 2=Status, 3=Materialize, 4=Invocation, 5=Teleport, 6=Bubble, 7=SummonPet</summary>
    public int Tipo { get; set; }

    /// <summary>1=UserOnly, 2=NpcOnly, 3=UserAndNpc, 4=Terrain, 5=Self</summary>
    public int Target { get; set; }

    // Requirements
    public int MinSkill { get; set; }
    public int ManaRequerido { get; set; }
    public int StaRequerido { get; set; }

    // HP effect — SubeHP: 1=heal, 2=damage
    public int SubeHP { get; set; }
    public int MinHP { get; set; }
    public int MaxHP { get; set; }

    // Mana effect
    public int SubeMana { get; set; }
    public int MinMana { get; set; }
    public int MaxMana { get; set; }

    // Stamina effect
    public int SubeSta { get; set; }
    public int MinSta { get; set; }
    public int MaxSta { get; set; }

    // Agility effect
    public int SubeAgilidad { get; set; }
    public int MinAgi { get; set; }
    public int MaxAgi { get; set; }

    // Strength effect
    public int SubeFuerza { get; set; }
    public int MinFue { get; set; }
    public int MaxFue { get; set; }

    // Status effects
    public bool Invisibilidad { get; set; }
    public bool Paraliza { get; set; }
    public bool Inmoviliza { get; set; }
    public bool Envenena { get; set; }
    public bool Maldicion { get; set; }
    public bool Bendicion { get; set; }
    public bool CuraVeneno { get; set; }
    public bool RemoverParalisis { get; set; }
    public bool Revivir { get; set; }
    public bool Estupidez { get; set; }
    public bool Ceguera { get; set; }
    public bool Mimetiza { get; set; }
    public bool RemoverMaldicion { get; set; }
    public bool RemoverEstupidez { get; set; }

    // Staff
    public int NeedStaff { get; set; }
    public bool StaffAffected { get; set; }

    // Invocation
    public int NumNPC { get; set; }
    public int Cant { get; set; }

    // Audio / Visual
    public int WAV { get; set; }
    public int FXGrh { get; set; }
    public int Loops { get; set; }
}

public class SpellDatabase
{
    public List<SpellData> Spells { get; } = new();

    public static SpellDatabase Load(string datDir)
    {
        var db = new SpellDatabase();
        var path = Path.Combine(datDir, "Hechizos.dat");
        var ini = IniFile.Load(path);

        int count = ini.GetInt("INIT", "NumeroHechizos");

        for (int i = 1; i <= count; i++)
        {
            var sec = $"Hechizo{i}";
            if (!ini.HasSection(sec)) continue;

            var spell = new SpellData
            {
                Index = i,
                Nombre           = ini.GetString(sec, "Nombre"),
                Desc             = ini.GetString(sec, "Desc"),
                PalabrasMagicas  = ini.GetString(sec, "PalabrasMagicas"),
                HechizeroMsg     = ini.GetString(sec, "HechizeroMsg"),
                TargetMsg        = ini.GetString(sec, "TargetMsg"),
                PropioMsg        = ini.GetString(sec, "PropioMsg"),
                Tipo             = ini.GetInt(sec, "Tipo"),
                Target           = ini.GetInt(sec, "Target"),
                MinSkill         = ini.GetInt(sec, "MinSkill"),
                ManaRequerido    = ini.GetInt(sec, "ManaRequerido"),
                StaRequerido     = ini.GetInt(sec, "StaRequerido"),
                SubeHP           = ini.GetInt(sec, "SubeHP"),
                MinHP            = ini.GetInt(sec, "MinHP"),
                MaxHP            = ini.GetInt(sec, "MaxHP"),
                SubeMana         = ini.GetInt(sec, "SubeMana"),
                MinMana          = ini.GetInt(sec, "MinMana"),
                MaxMana          = ini.GetInt(sec, "MaxMana"),
                SubeSta          = ini.GetInt(sec, "SubeSta"),
                MinSta           = ini.GetInt(sec, "MinSta"),
                MaxSta           = ini.GetInt(sec, "MaxSta"),
                SubeAgilidad     = ini.GetInt(sec, "SubeAgilidad"),
                MinAgi           = ini.GetInt(sec, "MinAgi"),
                MaxAgi           = ini.GetInt(sec, "MaxAgi"),
                SubeFuerza       = ini.GetInt(sec, "SubeFuerza"),
                MinFue           = ini.GetInt(sec, "MinFue"),
                MaxFue           = ini.GetInt(sec, "MaxFue"),
                Invisibilidad    = ini.GetBool(sec, "Invisibilidad"),
                Paraliza         = ini.GetBool(sec, "Paraliza"),
                Inmoviliza       = ini.GetBool(sec, "Inmoviliza"),
                Envenena         = ini.GetBool(sec, "Envenena"),
                Maldicion        = ini.GetBool(sec, "Maldicion"),
                Bendicion        = ini.GetBool(sec, "Bendicion"),
                CuraVeneno       = ini.GetBool(sec, "CuraVeneno"),
                RemoverParalisis = ini.GetBool(sec, "RemoverParalisis"),
                Revivir          = ini.GetBool(sec, "Revivir"),
                Estupidez        = ini.GetBool(sec, "Estupidez"),
                Ceguera          = ini.GetBool(sec, "Ceguera"),
                Mimetiza         = ini.GetBool(sec, "Mimetiza"),
                RemoverMaldicion  = ini.GetBool(sec, "RemoverMaldicion"),
                RemoverEstupidez  = ini.GetBool(sec, "RemoverEstupidez"),
                NeedStaff        = ini.GetInt(sec, "NeedStaff"),
                StaffAffected    = ini.GetBool(sec, "StaffAffected"),
                NumNPC           = ini.GetInt(sec, "NumNPC"),
                Cant             = ini.GetInt(sec, "Cant"),
                WAV              = ini.GetInt(sec, "WAV"),
                FXGrh            = ini.GetInt(sec, "FXGrh"),
                Loops            = ini.GetInt(sec, "Loops"),
            };

            db.Spells.Add(spell);
        }

        return db;
    }

    public void Save(string datDir)
    {
        var path = Path.Combine(datDir, "Hechizos.dat");
        var ini = new IniFile();

        ini.Set("INIT", "NumeroHechizos", Spells.Count);

        foreach (var spell in Spells)
        {
            var sec = $"Hechizo{spell.Index}";

            ini.Set(sec, "Nombre",           spell.Nombre);
            ini.Set(sec, "Desc",             spell.Desc);
            ini.Set(sec, "PalabrasMagicas",  spell.PalabrasMagicas);
            ini.Set(sec, "HechizeroMsg",     spell.HechizeroMsg);
            ini.Set(sec, "TargetMsg",        spell.TargetMsg);
            ini.Set(sec, "PropioMsg",        spell.PropioMsg);
            ini.Set(sec, "Tipo",             spell.Tipo);
            ini.Set(sec, "Target",           spell.Target);
            ini.Set(sec, "MinSkill",         spell.MinSkill);
            ini.Set(sec, "ManaRequerido",    spell.ManaRequerido);
            ini.Set(sec, "StaRequerido",     spell.StaRequerido);
            ini.Set(sec, "SubeHP",           spell.SubeHP);
            ini.Set(sec, "MinHP",            spell.MinHP);
            ini.Set(sec, "MaxHP",            spell.MaxHP);
            ini.Set(sec, "SubeMana",         spell.SubeMana);
            ini.Set(sec, "MinMana",          spell.MinMana);
            ini.Set(sec, "MaxMana",          spell.MaxMana);
            ini.Set(sec, "SubeSta",          spell.SubeSta);
            ini.Set(sec, "MinSta",           spell.MinSta);
            ini.Set(sec, "MaxSta",           spell.MaxSta);
            ini.Set(sec, "SubeAgilidad",     spell.SubeAgilidad);
            ini.Set(sec, "MinAgi",           spell.MinAgi);
            ini.Set(sec, "MaxAgi",           spell.MaxAgi);
            ini.Set(sec, "SubeFuerza",       spell.SubeFuerza);
            ini.Set(sec, "MinFue",           spell.MinFue);
            ini.Set(sec, "MaxFue",           spell.MaxFue);
            ini.Set(sec, "Invisibilidad",    spell.Invisibilidad);
            ini.Set(sec, "Paraliza",         spell.Paraliza);
            ini.Set(sec, "Inmoviliza",       spell.Inmoviliza);
            ini.Set(sec, "Envenena",         spell.Envenena);
            ini.Set(sec, "Maldicion",        spell.Maldicion);
            ini.Set(sec, "Bendicion",        spell.Bendicion);
            ini.Set(sec, "CuraVeneno",       spell.CuraVeneno);
            ini.Set(sec, "RemoverParalisis", spell.RemoverParalisis);
            ini.Set(sec, "Revivir",          spell.Revivir);
            ini.Set(sec, "Estupidez",        spell.Estupidez);
            ini.Set(sec, "Ceguera",          spell.Ceguera);
            ini.Set(sec, "Mimetiza",         spell.Mimetiza);
            ini.Set(sec, "RemoverMaldicion", spell.RemoverMaldicion);
            ini.Set(sec, "RemoverEstupidez", spell.RemoverEstupidez);
            ini.Set(sec, "NeedStaff",        spell.NeedStaff);
            ini.Set(sec, "StaffAffected",    spell.StaffAffected);
            ini.Set(sec, "NumNPC",           spell.NumNPC);
            ini.Set(sec, "Cant",             spell.Cant);
            ini.Set(sec, "WAV",              spell.WAV);
            ini.Set(sec, "FXGrh",            spell.FXGrh);
            ini.Set(sec, "Loops",            spell.Loops);
        }

        ini.Save(path);
    }
}

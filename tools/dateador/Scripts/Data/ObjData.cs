#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace AODateador.Data;

public class ObjData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int ObjType { get; set; }
    public int GrhIndex { get; set; }
    public int Valor { get; set; }

    // true = FIXED (cannot pick up)
    public bool Agarrable { get; set; }

    // Weapon
    public int MinHIT { get; set; }
    public int MaxHIT { get; set; }
    public int WeaponAnim { get; set; }
    public bool DosManos { get; set; }
    public bool Proyectil { get; set; }
    public int Municion { get; set; }

    // Armor
    public int MinDef { get; set; }
    public int MaxDef { get; set; }
    public int ShieldAnim { get; set; }
    public int CascoAnim { get; set; }
    public int NumRopaje { get; set; }

    // Potion
    public int TipoPocion { get; set; }
    public int MinModificador { get; set; }
    public int MaxModificador { get; set; }
    public int DuracionEfecto { get; set; }
    public int MinHam { get; set; }
    public int MinAgua { get; set; }

    // Door
    public int Llave { get; set; }
    public int Cerrada { get; set; }
    public int IndexAbierta { get; set; }
    public int IndexCerrada { get; set; }

    // Crafting
    public int SkHerreria { get; set; }
    public int SkCarpinteria { get; set; }
    public int LingH { get; set; }
    public int LingO { get; set; }
    public int LingP { get; set; }
    public int Madera { get; set; }

    // Restrictions
    public bool Real { get; set; }
    public bool Caos { get; set; }
    public int Lvl { get; set; }
    public List<string> ClaseProhibida { get; set; } = new();

    // Spell
    public int HechIndex { get; set; }

    // Sound
    public int SND1 { get; set; }

    // Flags
    public bool CuraVeneno { get; set; }
    public bool Envenena { get; set; }
    public int Refuerzo { get; set; }
    public bool Newbie { get; set; }
    public bool Paraliza { get; set; }

    public int StaffPower { get; set; }
    public int StaffDamageBonus { get; set; }
    public int DefensaMagicaMin { get; set; }
    public int DefensaMagicaMax { get; set; }
    public int MinSkill { get; set; }
    public int CreaAura { get; set; }
    public int Upgrade { get; set; }
    public string ForoId { get; set; } = "";
    public int MochilaType { get; set; }
}

public class ObjDatabase
{
    public List<ObjData> Objects { get; set; } = new();

    public static ObjDatabase Load(string datDir)
    {
        var db = new ObjDatabase();
        string path = Path.Combine(datDir, "Obj.dat");
        if (!File.Exists(path)) return db;

        var ini = IniFile.Load(path);
        int count = ini.GetInt("INIT", "NumOBJs", 0);

        for (int i = 1; i <= count; i++)
        {
            string section = $"OBJ{i}";
            if (!ini.HasSection(section)) continue;

            var obj = new ObjData
            {
                Index = i,
                Name = ini.GetString(section, "Name"),
                ObjType = ini.GetInt(section, "ObjType"),
                GrhIndex = ini.GetInt(section, "GrhIndex"),
                Valor = ini.GetInt(section, "Valor"),
                Agarrable = ini.GetInt(section, "Agarrable") == 1,

                // Weapon
                MinHIT = ini.GetInt(section, "MinHIT"),
                MaxHIT = ini.GetInt(section, "MaxHIT"),
                WeaponAnim = ini.GetInt(section, "Anim"),
                DosManos = ini.GetInt(section, "DosManos") == 1,
                Proyectil = ini.GetBool(section, "Proyectil"),
                Municion = ini.GetInt(section, "Municion"),

                // Armor
                MinDef = ini.GetInt(section, "MinDef"),
                MaxDef = ini.GetInt(section, "MaxDef"),
                ShieldAnim = ini.GetInt(section, "ShieldAnim"),
                CascoAnim = ini.GetInt(section, "CascoAnim"),
                NumRopaje = ini.GetInt(section, "NumRopaje"),

                // Potion
                TipoPocion = ini.GetInt(section, "TipoPocion"),
                MinModificador = ini.GetInt(section, "MinModificador"),
                MaxModificador = ini.GetInt(section, "MaxModificador"),
                DuracionEfecto = ini.GetInt(section, "DuracionEfecto"),
                MinHam = ini.GetInt(section, "MinHam"),
                MinAgua = ini.GetInt(section, "MinAgua"),

                // Door
                Llave = ini.GetInt(section, "Llave"),
                Cerrada = ini.GetInt(section, "Cerrada"),
                IndexAbierta = ini.GetInt(section, "IndexAbierta"),
                IndexCerrada = ini.GetInt(section, "IndexCerrada"),

                // Crafting
                SkHerreria = ini.GetInt(section, "SkHerreria"),
                SkCarpinteria = ini.GetInt(section, "SkCarpinteria"),
                LingH = ini.GetInt(section, "LingH"),
                LingO = ini.GetInt(section, "LingO"),
                LingP = ini.GetInt(section, "LingP"),
                Madera = ini.GetInt(section, "Madera"),

                // Restrictions
                Real = ini.GetInt(section, "Real") == 1,
                Caos = ini.GetInt(section, "Caos") == 1,
                Lvl = ini.GetInt(section, "Lvl"),

                // Spell
                HechIndex = ini.GetInt(section, "HechIndex"),

                // Sound
                SND1 = ini.GetInt(section, "SND1"),

                // Flags
                CuraVeneno = ini.GetBool(section, "CuraVeneno"),
                Envenena = ini.GetBool(section, "Envenena"),
                Refuerzo = ini.GetInt(section, "Refuerzo"),
                Newbie = ini.GetBool(section, "Newbie"),
                Paraliza = ini.GetBool(section, "Paraliza"),

                StaffPower = ini.GetInt(section, "StaffPower"),
                StaffDamageBonus = ini.GetInt(section, "StaffDamageBonus"),
                DefensaMagicaMin = ini.GetInt(section, "DefensaMagicaMin"),
                DefensaMagicaMax = ini.GetInt(section, "DefensaMagicaMax"),
                MinSkill = ini.GetInt(section, "MinSkill"),
                CreaAura = ini.GetInt(section, "CreaAura"),
                Upgrade = ini.GetInt(section, "Upgrade"),
                ForoId = ini.GetString(section, "ForoId"),
                MochilaType = ini.GetInt(section, "MochilaType"),
            };

            // Restricted classes (CP1..CP8)
            for (int c = 1; c <= 8; c++)
            {
                string className = ini.GetString(section, $"CP{c}");
                if (!string.IsNullOrEmpty(className))
                    obj.ClaseProhibida.Add(className);
            }

            db.Objects.Add(obj);
        }

        return db;
    }

    public void Save(string datDir)
    {
        var ini = new IniFile();
        ini.Set("INIT", "NumOBJs", Objects.Count);

        for (int i = 0; i < Objects.Count; i++)
        {
            var obj = Objects[i];
            int sectionNum = i + 1;
            string section = $"OBJ{sectionNum}";

            ini.Set(section, "Name", obj.Name);
            ini.Set(section, "ObjType", obj.ObjType);
            ini.Set(section, "GrhIndex", obj.GrhIndex);
            ini.Set(section, "Valor", obj.Valor);
            ini.Set(section, "Agarrable", obj.Agarrable ? 1 : 0);

            // Weapon
            ini.Set(section, "MinHIT", obj.MinHIT);
            ini.Set(section, "MaxHIT", obj.MaxHIT);
            ini.Set(section, "Anim", obj.WeaponAnim);
            ini.Set(section, "DosManos", obj.DosManos ? 1 : 0);
            ini.Set(section, "Proyectil", obj.Proyectil);
            ini.Set(section, "Municion", obj.Municion);

            // Armor
            ini.Set(section, "MinDef", obj.MinDef);
            ini.Set(section, "MaxDef", obj.MaxDef);
            ini.Set(section, "ShieldAnim", obj.ShieldAnim);
            ini.Set(section, "CascoAnim", obj.CascoAnim);
            ini.Set(section, "NumRopaje", obj.NumRopaje);

            // Potion
            ini.Set(section, "TipoPocion", obj.TipoPocion);
            ini.Set(section, "MinModificador", obj.MinModificador);
            ini.Set(section, "MaxModificador", obj.MaxModificador);
            ini.Set(section, "DuracionEfecto", obj.DuracionEfecto);
            ini.Set(section, "MinHam", obj.MinHam);
            ini.Set(section, "MinAgua", obj.MinAgua);

            // Door
            ini.Set(section, "Llave", obj.Llave);
            ini.Set(section, "Cerrada", obj.Cerrada);
            ini.Set(section, "IndexAbierta", obj.IndexAbierta);
            ini.Set(section, "IndexCerrada", obj.IndexCerrada);

            // Crafting
            ini.Set(section, "SkHerreria", obj.SkHerreria);
            ini.Set(section, "SkCarpinteria", obj.SkCarpinteria);
            ini.Set(section, "LingH", obj.LingH);
            ini.Set(section, "LingO", obj.LingO);
            ini.Set(section, "LingP", obj.LingP);
            ini.Set(section, "Madera", obj.Madera);

            // Restrictions
            ini.Set(section, "Real", obj.Real ? 1 : 0);
            ini.Set(section, "Caos", obj.Caos ? 1 : 0);
            ini.Set(section, "Lvl", obj.Lvl);

            for (int c = 0; c < obj.ClaseProhibida.Count && c < 8; c++)
                ini.Set(section, $"CP{c + 1}", obj.ClaseProhibida[c]);

            // Spell
            ini.Set(section, "HechIndex", obj.HechIndex);

            // Sound
            ini.Set(section, "SND1", obj.SND1);

            // Flags
            ini.Set(section, "CuraVeneno", obj.CuraVeneno);
            ini.Set(section, "Envenena", obj.Envenena);
            ini.Set(section, "Refuerzo", obj.Refuerzo);
            ini.Set(section, "Newbie", obj.Newbie);
            ini.Set(section, "Paraliza", obj.Paraliza);

            ini.Set(section, "StaffPower", obj.StaffPower);
            ini.Set(section, "StaffDamageBonus", obj.StaffDamageBonus);
            ini.Set(section, "DefensaMagicaMin", obj.DefensaMagicaMin);
            ini.Set(section, "DefensaMagicaMax", obj.DefensaMagicaMax);
            ini.Set(section, "MinSkill", obj.MinSkill);
            ini.Set(section, "CreaAura", obj.CreaAura);
            ini.Set(section, "Upgrade", obj.Upgrade);
            ini.Set(section, "ForoId", obj.ForoId);
            ini.Set(section, "MochilaType", obj.MochilaType);
        }

        ini.Save(Path.Combine(datDir, "Obj.dat"));
    }
}

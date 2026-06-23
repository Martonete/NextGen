#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace AODateador.Data;

public class NpcInvItem
{
    public int ObjIndex { get; set; }
    public int Amount { get; set; }
    public int DropProb { get; set; }
}

public class NpcData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int NpcType { get; set; }
    public int Head { get; set; }
    public int Body { get; set; }
    public int Heading { get; set; }
    public int WeaponAnim { get; set; }
    public int ShieldAnim { get; set; }
    public int CascoAnim { get; set; }
    public int Movement { get; set; }
    public bool Attackable { get; set; }
    public bool Hostile { get; set; }
    public bool Respawn { get; set; }
    public int Domable { get; set; }
    public bool Comercia { get; set; }
    public int MinHP { get; set; }
    public int MaxHP { get; set; }
    public int MinHit { get; set; }
    public int MaxHit { get; set; }
    public int Def { get; set; }
    public int DefM { get; set; }
    public int PoderAtaque { get; set; }
    public int PoderEvasion { get; set; }
    public int GiveEXP { get; set; }
    public int GiveGLD { get; set; }
    public int GiveGLDMin { get; set; }
    public int GiveGLDMax { get; set; }
    public int Inflacion { get; set; }
    public int TipoItems { get; set; }
    public bool InvRespawn { get; set; }
    public int Alineacion { get; set; }
    public bool AguaValida { get; set; }
    public bool TierraInvalida { get; set; }
    public bool Veneno { get; set; }
    public int Aura { get; set; }
    public bool AtacaDoble { get; set; }
    public int SND1 { get; set; }
    public int SND2 { get; set; }
    public int SND3 { get; set; }
    public List<NpcInvItem> Items { get; set; } = new();
    public List<int> Spells { get; set; } = new();
}

public class NpcDatabase
{
    public List<NpcData> Npcs { get; set; } = new();

    public static NpcDatabase Load(string datDir)
    {
        var db = new NpcDatabase();
        db.Npcs.AddRange(LoadFromFile(Path.Combine(datDir, "NPCs.dat"), baseIndex: 0));
        db.Npcs.AddRange(LoadFromFile(Path.Combine(datDir, "NPCs-HOSTILES.dat"), baseIndex: 500));
        return db;
    }

    private static List<NpcData> LoadFromFile(string path, int baseIndex)
    {
        var result = new List<NpcData>();
        if (!File.Exists(path)) return result;

        var ini = IniFile.Load(path);
        int count = ini.GetInt("INIT", "NumNPCs", 0);

        for (int i = 1; i <= count; i++)
        {
            string section = $"NPC{i}";
            if (!ini.HasSection(section)) continue;

            var npc = new NpcData
            {
                Index = baseIndex + i,
                Name = ini.GetString(section, "Name"),
                Desc = ini.GetString(section, "Desc"),
                NpcType = ini.GetInt(section, "NpcType"),
                Head = ini.GetInt(section, "Head"),
                Body = ini.GetInt(section, "Body"),
                Heading = ini.GetInt(section, "Heading"),
                WeaponAnim = ini.GetInt(section, "WeaponAnim"),
                ShieldAnim = ini.GetInt(section, "ShieldAnim"),
                CascoAnim = ini.GetInt(section, "CascoAnim"),
                Movement = ini.GetInt(section, "Movement"),
                Attackable = ini.GetBool(section, "Attackable"),
                Hostile = ini.GetBool(section, "Hostile"),
                Respawn = ini.GetBool(section, "Respawn"),
                Domable = ini.GetInt(section, "Domable"),
                Comercia = ini.GetBool(section, "Comercia"),
                MinHP = ini.GetInt(section, "MinHP"),
                MaxHP = ini.GetInt(section, "MaxHP"),
                MinHit = ini.GetInt(section, "MinHit"),
                MaxHit = ini.GetInt(section, "MaxHit"),
                Def = ini.GetInt(section, "Def"),
                DefM = ini.GetInt(section, "DefM"),
                PoderAtaque = ini.GetInt(section, "PoderAtaque"),
                PoderEvasion = ini.GetInt(section, "PoderEvasion"),
                GiveEXP = ini.GetInt(section, "GiveEXP"),
                GiveGLD = ini.GetInt(section, "GiveGLD"),
                GiveGLDMin = ini.GetInt(section, "GiveGLDMin"),
                GiveGLDMax = ini.GetInt(section, "GiveGLDMax"),
                Inflacion = ini.GetInt(section, "Inflacion"),
                TipoItems = ini.GetInt(section, "TipoItems"),
                InvRespawn = ini.GetBool(section, "InvRespawn"),
                Alineacion = ini.GetInt(section, "Alineacion"),
                AguaValida = ini.GetBool(section, "AguaValida"),
                TierraInvalida = ini.GetBool(section, "TierraInvalida"),
                Veneno = ini.GetBool(section, "Veneno"),
                Aura = ini.GetInt(section, "Aura"),
                AtacaDoble = ini.GetBool(section, "AtacaDoble"),
                SND1 = ini.GetInt(section, "SND1"),
                SND2 = ini.GetInt(section, "SND2"),
                SND3 = ini.GetInt(section, "SND3"),
            };

            // Items
            int itemCount = ini.GetInt(section, "NROITEMS", 0);
            for (int j = 1; j <= itemCount; j++)
            {
                string raw = ini.GetString(section, $"Obj{j}");
                var parts = raw.Split('-');
                if (parts.Length >= 3
                    && int.TryParse(parts[0], out int objIdx)
                    && int.TryParse(parts[1], out int amount)
                    && int.TryParse(parts[2], out int dropProb))
                {
                    npc.Items.Add(new NpcInvItem { ObjIndex = objIdx, Amount = amount, DropProb = dropProb });
                }
            }

            // Spells
            int spellCount = ini.GetInt(section, "LanzaSpells", 0);
            for (int j = 1; j <= spellCount; j++)
            {
                int spellId = ini.GetInt(section, $"Sp{j}");
                npc.Spells.Add(spellId);
            }

            result.Add(npc);
        }

        return result;
    }

    public void Save(string datDir)
    {
        SaveToFile(datDir, "NPCs.dat", baseIndex: 0, maxBaseIndex: 499);
        SaveToFile(datDir, "NPCs-HOSTILES.dat", baseIndex: 500, maxBaseIndex: int.MaxValue);
    }

    private void SaveToFile(string datDir, string fileName, int baseIndex, int maxBaseIndex)
    {
        var filtered = new List<NpcData>();
        foreach (var npc in Npcs)
        {
            if (npc.Index > baseIndex && npc.Index <= maxBaseIndex)
                filtered.Add(npc);
        }

        var ini = new IniFile();
        ini.Set("INIT", "NumNPCs", filtered.Count);

        for (int i = 0; i < filtered.Count; i++)
        {
            var npc = filtered[i];
            int sectionNum = i + 1;
            string section = $"NPC{sectionNum}";

            ini.Set(section, "Name", npc.Name);
            ini.Set(section, "Desc", npc.Desc);
            ini.Set(section, "NpcType", npc.NpcType);
            ini.Set(section, "Head", npc.Head);
            ini.Set(section, "Body", npc.Body);
            ini.Set(section, "Heading", npc.Heading);
            ini.Set(section, "WeaponAnim", npc.WeaponAnim);
            ini.Set(section, "ShieldAnim", npc.ShieldAnim);
            ini.Set(section, "CascoAnim", npc.CascoAnim);
            ini.Set(section, "Movement", npc.Movement);
            ini.Set(section, "Attackable", npc.Attackable);
            ini.Set(section, "Hostile", npc.Hostile);
            ini.Set(section, "Respawn", npc.Respawn);
            ini.Set(section, "Domable", npc.Domable);
            ini.Set(section, "Comercia", npc.Comercia);
            ini.Set(section, "MinHP", npc.MinHP);
            ini.Set(section, "MaxHP", npc.MaxHP);
            ini.Set(section, "MinHit", npc.MinHit);
            ini.Set(section, "MaxHit", npc.MaxHit);
            ini.Set(section, "Def", npc.Def);
            ini.Set(section, "DefM", npc.DefM);
            ini.Set(section, "PoderAtaque", npc.PoderAtaque);
            ini.Set(section, "PoderEvasion", npc.PoderEvasion);
            ini.Set(section, "GiveEXP", npc.GiveEXP);
            ini.Set(section, "GiveGLD", npc.GiveGLD);
            ini.Set(section, "GiveGLDMin", npc.GiveGLDMin);
            ini.Set(section, "GiveGLDMax", npc.GiveGLDMax);
            ini.Set(section, "Inflacion", npc.Inflacion);
            ini.Set(section, "TipoItems", npc.TipoItems);
            ini.Set(section, "InvRespawn", npc.InvRespawn);
            ini.Set(section, "Alineacion", npc.Alineacion);
            ini.Set(section, "AguaValida", npc.AguaValida);
            ini.Set(section, "TierraInvalida", npc.TierraInvalida);
            ini.Set(section, "Veneno", npc.Veneno);
            ini.Set(section, "Aura", npc.Aura);
            ini.Set(section, "AtacaDoble", npc.AtacaDoble);
            ini.Set(section, "SND1", npc.SND1);
            ini.Set(section, "SND2", npc.SND2);
            ini.Set(section, "SND3", npc.SND3);

            ini.Set(section, "NROITEMS", npc.Items.Count);
            for (int j = 0; j < npc.Items.Count; j++)
            {
                var item = npc.Items[j];
                ini.Set(section, $"Obj{j + 1}", $"{item.ObjIndex}-{item.Amount}-{item.DropProb}");
            }

            ini.Set(section, "LanzaSpells", npc.Spells.Count);
            for (int j = 0; j < npc.Spells.Count; j++)
            {
                ini.Set(section, $"Sp{j + 1}", npc.Spells[j]);
            }
        }

        ini.Save(Path.Combine(datDir, fileName));
    }
}

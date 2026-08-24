#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AODateador.Data;

public enum FieldKind { Int, Bool, Text, Grh, ObjTypeSelect }

/// <summary>
/// One editable key of obj.dat.
///
/// <see cref="Key"/> is the spelling as it appears in the file, which is not
/// consistent: <c>MINDEF</c> is upper case, <c>abierta</c> and <c>llave</c> are
/// lower, and <c>ObjType</c> also occurs as <c>Objtype</c>. Matching is
/// case-insensitive, but a key inserted for the first time uses this spelling.
/// </summary>
public sealed record FieldDef(
    string Key,
    string Label,
    FieldKind Kind,
    int Min = 0,
    int Max = 99999,
    string Default = "0",
    string? Hint = null);

/// <summary>
/// Which keys each ObjType offers, derived from what the file actually contains
/// and cross-checked against what the server reads
/// (<c>server/source/data/objects.rs</c>).
///
/// The list is deliberately not exhaustive: obj.dat carries roughly a hundred
/// distinct keys, about forty of which no parser in the repo reads. Those are
/// left out of the UI on purpose — <see cref="ObjDatWriter"/> never touches a
/// key it is not told about, so they survive editing untouched rather than
/// being surfaced as fields nobody should change.
/// </summary>
public static class FieldDefs
{
    /// <summary>ObjType values that matter for field selection.</summary>
    public const int Food = 1, Weapon = 2, Armour = 3, Tree = 4, Money = 5, Door = 6,
                     Container = 7, Sign = 8, Key = 9, Forum = 10, Potion = 11,
                     Book = 12, Drink = 13, Wood = 14, Campfire = 15, Shield = 16,
                     Helmet = 17, Tool = 18, Teleport = 19, Furniture = 20,
                     Deposit = 22, Scroll = 24, Aura = 25, Instrument = 26,
                     Ship = 31, Arrow = 32, Mount = 36;

    /// <summary>Display names, indexed by ObjType.</summary>
    public static readonly IReadOnlyDictionary<int, string> TypeNames = new Dictionary<int, string>
    {
        [1] = "Comida", [2] = "Arma", [3] = "Armadura", [4] = "Árbol", [5] = "Dinero",
        [6] = "Puerta", [7] = "Contenedor", [8] = "Cartel", [9] = "Llave", [10] = "Foro",
        [11] = "Poción", [12] = "Libro", [13] = "Bebida", [14] = "Leña", [15] = "Fogata",
        [16] = "Escudo", [17] = "Casco", [18] = "Herramienta", [19] = "Teleport",
        [20] = "Mueble", [21] = "Joya", [22] = "Yacimiento", [23] = "Metal",
        [24] = "Pergamino", [25] = "Aura", [26] = "Instrumento", [27] = "Yunque",
        [28] = "Fragua", [29] = "Gema", [30] = "Flor", [31] = "Barco", [32] = "Flecha",
        [33] = "Botella vacía", [34] = "Botella llena", [35] = "Mancha", [36] = "Montura",
        [38] = "Mapa del tesoro",
    };

    public static string TypeName(int objType)
        => TypeNames.TryGetValue(objType, out var name) ? name : $"Tipo {objType}";

    /// <summary>Fields every object has, whatever its type.</summary>
    public static readonly FieldDef[] Common =
    {
        new("Name",      "Nombre",    FieldKind.Text, Default: ""),
        new("ObjType",   "Tipo",      FieldKind.ObjTypeSelect),
        new("GrhIndex",  "Gráfico",   FieldKind.Grh, Max: int.MaxValue),
        new("Valor",     "Valor",     FieldKind.Int, Max: 9999999),
        new("Agarrable", "Agarrable", FieldKind.Bool,
            Hint: "0 = se puede agarrar del piso"),
        new("Crucial",   "Crucial",   FieldKind.Bool),
        new("NoSeCae",   "No se cae", FieldKind.Bool,
            Hint: "no se pierde al morir"),
        new("Intransferible", "Intransferible", FieldKind.Bool),
        new("newbie",    "Newbie",    FieldKind.Bool),
    };

    /// <summary>Type-specific fields, keyed by ObjType.</summary>
    private static readonly Dictionary<int, FieldDef[]> ByType = new()
    {
        [Food] = new[]
        {
            new FieldDef("MinHAM", "Hambre que quita", FieldKind.Int, Max: 999),
            new FieldDef("MinHP",  "HP que cura",      FieldKind.Int, Max: 9999),
        },
        [Drink] = new[]
        {
            // The file spells it MinAgu; the server reads MinAGU, so the
            // case-insensitive match is what makes this work at all.
            new FieldDef("MinAgu", "Sed que quita", FieldKind.Int, Max: 999),
            new FieldDef("MinST",  "Stamina",       FieldKind.Int, Max: 999),
        },
        [Weapon] = new[]
        {
            new FieldDef("MinHIT",  "Daño mínimo", FieldKind.Int, Max: 9999),
            new FieldDef("MaxHIT",  "Daño máximo", FieldKind.Int, Max: 9999),
            new FieldDef("Anim",    "Animación (Armas.dat)", FieldKind.Int, Max: 9999),
            new FieldDef("DosManos","Dos manos",   FieldKind.Bool),
            new FieldDef("Apuñala", "Apuñala",     FieldKind.Bool),
            new FieldDef("Envenena","Envenena",    FieldKind.Bool),
            new FieldDef("Proyectil","Proyectil",  FieldKind.Bool),
            new FieldDef("Municiones","Munición",  FieldKind.Bool),
            new FieldDef("StaffPower","Poder de báculo", FieldKind.Int, Max: 999),
            new FieldDef("StaffDamageBonus","Bonus de báculo", FieldKind.Int, Max: 999),
            new FieldDef("Refuerzo", "Refuerzo",   FieldKind.Int, Max: 999),
        },
        [Armour] = new[]
        {
            new FieldDef("MINDEF",    "Defensa mínima", FieldKind.Int, Max: 999),
            new FieldDef("MAXDEF",    "Defensa máxima", FieldKind.Int, Max: 999),
            new FieldDef("NumRopaje", "Ropaje (Personajes.ind)", FieldKind.Int, Max: 99999),
            new FieldDef("Real",      "Sólo Armada",  FieldKind.Bool),
            new FieldDef("Caos",      "Sólo Caos",    FieldKind.Bool),
            new FieldDef("RazaEnana", "Sólo enanos",  FieldKind.Bool),
            new FieldDef("Mujer",     "Sólo mujeres", FieldKind.Bool),
            new FieldDef("LVL",       "Nivel mínimo", FieldKind.Int, Max: 99),
        },
        [Shield] = new[]
        {
            new FieldDef("MINDEF", "Defensa mínima", FieldKind.Int, Max: 999),
            new FieldDef("MAXDEF", "Defensa máxima", FieldKind.Int, Max: 999),
            // Shields and helmets use Anim too, not ShieldAnim/CascoAnim: the
            // server picks the table from ObjType (objects.rs:424-433).
            new FieldDef("Anim",   "Animación (Escudos.dat)", FieldKind.Int, Max: 9999),
        },
        [Helmet] = new[]
        {
            new FieldDef("MINDEF", "Defensa mínima", FieldKind.Int, Max: 999),
            new FieldDef("MAXDEF", "Defensa máxima", FieldKind.Int, Max: 999),
            new FieldDef("Anim",   "Animación (Cascos.ind)", FieldKind.Int, Max: 9999),
            new FieldDef("DefensaMagicaMin", "Def. mágica mín.", FieldKind.Int, Max: 999),
            new FieldDef("DefensaMagicaMax", "Def. mágica máx.", FieldKind.Int, Max: 999),
        },
        [Potion] = new[]
        {
            new FieldDef("TipoPocion",     "Tipo de poción", FieldKind.Int, Max: 7,
                Hint: "1 agilidad · 2 fuerza · 3 HP · 4 maná · 5 antídoto · 7 stamina"),
            new FieldDef("MinModificador", "Efecto mínimo",  FieldKind.Int, Max: 9999),
            new FieldDef("MaxModificador", "Efecto máximo",  FieldKind.Int, Max: 9999),
            new FieldDef("DuracionEfecto", "Duración (seg)", FieldKind.Int, Max: 99999),
        },
        [Key] = new[]
        {
            new FieldDef("Clave", "Clave", FieldKind.Int, Max: 999999),
            new FieldDef("Info",  "Info",  FieldKind.Text, Default: ""),
        },
        [Door] = new[]
        {
            // Lower-case in the file, and note `abierta` reads the opposite way
            // round from the `Cerrada` the old model invented.
            new FieldDef("abierta",           "Abierta",        FieldKind.Bool),
            new FieldDef("llave",             "Requiere llave", FieldKind.Bool),
            new FieldDef("clave",             "Clave",          FieldKind.Int, Max: 999999),
            new FieldDef("IndexAbierta",      "Índice abierta", FieldKind.Int, Max: 99999),
            new FieldDef("IndexCerrada",      "Índice cerrada", FieldKind.Int, Max: 99999),
            new FieldDef("IndexCerradaLlave", "Índice con llave", FieldKind.Int, Max: 99999),
        },
        [Sign] = new[]
        {
            new FieldDef("Texto", "Texto", FieldKind.Text, Default: ""),
        },
        [Forum] = new[]
        {
            new FieldDef("ID", "ID del foro", FieldKind.Text, Default: ""),
        },
        [Scroll] = new[]
        {
            new FieldDef("multScroll", "Multiplicador", FieldKind.Int, Max: 999),
            new FieldDef("typeScroll", "Tipo",          FieldKind.Int, Max: 99),
            new FieldDef("timeScroll", "Duración",      FieldKind.Int, Max: 99999),
        },
        [Aura] = new[]
        {
            new FieldDef("CreaAura", "Aura", FieldKind.Int, Max: 999),
        },
        [Instrument] = new[]
        {
            new FieldDef("SND1", "Sonido 1", FieldKind.Int, Max: 9999),
            new FieldDef("SND2", "Sonido 2", FieldKind.Int, Max: 9999),
            new FieldDef("SND3", "Sonido 3", FieldKind.Int, Max: 9999),
        },
        [Deposit] = new[]
        {
            new FieldDef("MineralIndex", "Mineral",  FieldKind.Int, Max: 99999),
            new FieldDef("AntiLimpieza", "Anti limpieza", FieldKind.Bool),
        },
        [Mount] = new[]
        {
            new FieldDef("NumRopaje",  "Ropaje",   FieldKind.Int, Max: 99999),
            new FieldDef("esVoladora", "Voladora", FieldKind.Bool),
        },
    };

    /// <summary>Crafting inputs, offered on anything that can be forged.</summary>
    public static readonly FieldDef[] Crafting =
    {
        new("SkHerreria",    "Skill herrería",   FieldKind.Int, Max: 100),
        new("SkCarpinteria", "Skill carpintería",FieldKind.Int, Max: 100),
        new("LingH",         "Lingotes hierro",  FieldKind.Int, Max: 9999),
        new("LingP",         "Lingotes plata",   FieldKind.Int, Max: 9999),
        new("LingO",         "Lingotes oro",     FieldKind.Int, Max: 9999),
        new("Madera",        "Madera",           FieldKind.Int, Max: 9999),
    };

    private static readonly int[] CraftableTypes = { Weapon, Armour, Shield, Helmet, Arrow };

    /// <summary>Fields to show for an object of this type, in display order.</summary>
    public static IEnumerable<FieldDef> For(int objType)
    {
        foreach (var f in Common) yield return f;

        if (ByType.TryGetValue(objType, out var specific))
            foreach (var f in specific) yield return f;

        if (CraftableTypes.Contains(objType))
            foreach (var f in Crafting) yield return f;
    }

    /// <summary>Section heading a field belongs under.</summary>
    public static string SectionOf(FieldDef field)
    {
        if (Common.Contains(field)) return "General";
        if (Crafting.Contains(field)) return "Fabricación";
        return "Atributos";
    }

    /// <summary>
    /// Class restrictions live in CP1..CP16 as class names, one per key. The
    /// server only reads CP1..CP8 (objects.rs:436-441); the rest are preserved
    /// but have no effect.
    /// </summary>
    public static readonly string[] ClassNames =
    {
        "MAGO", "CLERIGO", "GUERRERO", "ASESINO", "LADRON", "BARDO",
        "DRUIDA", "BANDIDO", "PALADIN", "CAZADOR", "TRABAJADOR", "PIRATA",
    };

    public const int ClassProhibitedSlots = 8;
}

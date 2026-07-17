using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Loads Textos.ao — INI-format message templates indexed by ID.
/// VB6 format: [TEXTOn] Mensaje=... Font=...
/// Console messages (|| packets) reference these by ID.
/// </summary>
public static class TextosLoader
{
    public static TextMessage[] Load(IResourceProvider resources)
    {
        // Read with Latin1 encoding (VB6 strings)
        string[] lines;
        try
        {
            byte[] data = resources.ReadBytes("INIT/Textos.ao");
            string content = Encoding.GetEncoding("iso-8859-1").GetString(data);
            lines = content.Split('\n');
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TEXTOS] Failed to load: {ex.Message}");
            return new TextMessage[1];
        }

        // First pass: find max ID
        int maxId = 0;
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("[TEXTO") && line.EndsWith("]"))
            {
                string numStr = line[6..^1]; // strip [TEXTO and ]
                if (int.TryParse(numStr, out int id) && id > maxId)
                    maxId = id;
            }
        }

        var messages = new TextMessage[maxId + 1];
        for (int i = 0; i < messages.Length; i++)
            messages[i] = new TextMessage { Text = "", FontId = 21 }; // default info font

        // Second pass: populate
        int currentId = 0;
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("[TEXTO") && trimmed.EndsWith("]"))
            {
                string numStr = trimmed[6..^1];
                if (int.TryParse(numStr, out int id))
                    currentId = id;
                continue;
            }

            if (currentId <= 0 || currentId >= messages.Length) continue;

            if (trimmed.StartsWith("Mensaje="))
            {
                messages[currentId].Text = trimmed[8..].TrimEnd();
            }
            else if (trimmed.StartsWith("Font="))
            {
                if (int.TryParse(trimmed[5..].Trim(), out int font))
                    messages[currentId].FontId = font;
            }
        }

        GD.Print($"[TEXTOS] Loaded {maxId} message templates");
        return messages;
    }
}

public class TextMessage
{
    public string Text = "";
    public int FontId;
}

/// <summary>
/// VB6 FontTypes: maps font IDs to RGB colors.
/// From modTextos.bas InitFonts().
/// </summary>
public static class FontTypes
{
    // Pre-built color table from VB6 modTextos.bas (IDs 1-89)
    private static readonly (byte R, byte G, byte B)[] Colors = new (byte, byte, byte)[90]
    {
        (255,255,255), // 0: unused (white fallback)
        (240,240,50),  // 1: GANAR - yellow
        (0,128,128),   // 2: CONSOLA - teal
        (0,128,128),   // 3: GLOBAL - teal
        (255,0,0),     // 4: UDP - red
        (255,255,255), // 5: TALK - white
        (225,249,158), // 6: TORNEIN - light yellow-green
        (225,222,119), // 7: ORO - gold
        (225,222,119), // 8: OROX - gold
        (255,255,255), // 9: TSUBASTA - white
        (255,255,255), // 10: TDSUBASTA - white
        (48,128,255),  // 11: SUBASTA - blue
        (86,87,89),    // 12: NPCS - dark gray
        (255,83,255),  // 13: NPCSX - pink
        (114,0,4),     // 14: ATNPC - dark red
        (145,9,179),   // 15: GANAORO - purple
        (100,0,255),   // 16: DIOSES - blue-purple
        (100,0,255),   // 17: DIOSESI - blue-purple italic
        (100,0,255),   // 18: DIOSESN - blue-purple bold
        (255,0,0),     // 19: FIGHT - red
        (32,51,223),   // 20: WARNING - blue
        (69,190,156),  // 21: INFO - teal-green
        (69,190,156),  // 22: INFOBOLD
        (69,190,156),  // 23: INFOITALIC
        (130,130,130), // 24: EJECUCION - gray
        (255,255,255), // 25: PARTY - white
        (0,255,0),     // 26: VENENO - green
        (255,255,255), // 27: GUILD - white
        (0,185,0),     // 28: SERVER - green
        (177,153,57),  // 29: FORTA - brown-gold
        (255,255,100), // 30: CASTI - light yellow
        (228,199,27),  // 31: GUILDMSG - gold
        (0,64,128),    // 32: CONSEJO - dark blue
        (140,0,0),     // 33: CONSEJOCAOS - dark red
        (0,64,128),    // 34: CONSEJOVesA - dark blue
        (140,0,0),     // 35: CONSEJOCAOSVesA - dark red
        (0,170,0),     // 36: CENTINELA - green
        (128,0,0),     // 37: ADVERTENCIAS - maroon
        (255,255,0),   // 38: AMARILLON - yellow bold
        (236,186,107), // 39: EXPEN - tan
        (130,130,130), // 40: GRISN - gray bold
        (255,255,0),   // 41: DAREXP - yellow bold
        (255,0,0),     // 42: ROJO - red
        (173,170,255), // 43: GLOBALUSUARIO - light purple
        (255,255,0),   // 44: GLOBALNOBLE - yellow
        (0,255,128),   // 45: GLOBALGM - green
        (255,255,255), // 46: BLANCO - white
        (128,0,0),     // 47: BORDO - maroon
        (0,255,0),     // 48: VERDE - green
        (0,0,255),     // 49: AZUL - blue
        (128,0,128),   // 50: VIOLETA - violet
        (255,255,0),   // 51: AMARILLO - yellow
        (128,255,255), // 52: CELESTE - cyan
        (130,130,130), // 53: GRIS - gray
        (255,255,255), // 54-78: styled variants (same base colors)
        (128,0,0),     // 55: BORDON
        (0,255,0),     // 56: VERDEN
        (107,142,35),  // 57: OLIVE
        (255,0,0),     // 58: ROJON
        (0,0,255),     // 59: AZULN
        (128,0,128),   // 60: VIOLETAN
        (128,255,255), // 61: CELESTEN
        (255,0,0),     // 62: DON
        (0,64,128),    // 63: AZULC
        (255,255,255), // 64: BLANCOCN
        (128,0,0),     // 65: BORDOCN
        (0,255,0),     // 66: VERDECN
        (255,0,0),     // 67: ROJOCN
        (0,0,255),     // 68: AZULCN
        (128,0,128),   // 69: VIOLETACN
        (128,255,255), // 70: CELESTECN
        (130,130,130), // 71: GRISCN
        (255,255,255), // 72: BLANCOC
        (128,0,0),     // 73: BORDOC
        (0,255,0),     // 74: VERDEC
        (255,0,0),     // 75: ROJOC
        (128,0,128),   // 76: VIOLETAC
        (128,255,255), // 77: CELESTEC
        (130,130,130), // 78: GRISC
        (0,128,0),     // 79: VERDEL
        (200,255,0),   // 80: CHAT
        (177,153,57),  // 81: REJA
        (255,128,0),   // 82: NARANJA
        (230,180,10),  // 83: CONTEO
        (220,83,14),   // 84: YA
        (250,210,140), // 85: NEWTORNEO
        (255,128,0),   // 86: NARANJAN
        (233,192,0),   // 87: TROFEOS1
        (196,196,196), // 88: TROFEOS2
        (255,128,128), // 89: TROFEOS3
    };

    /// <summary>
    /// Get hex color string for a font ID.
    /// </summary>
    public static string GetHexColor(int fontId)
    {
        if (fontId < 0 || fontId >= Colors.Length)
            fontId = 21; // default INFO
        var c = Colors[fontId];
        return $"{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}

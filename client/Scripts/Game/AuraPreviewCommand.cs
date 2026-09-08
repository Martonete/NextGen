using System;
using ArgentumNextgen.Data;

namespace ArgentumNextgen.Game;

public static class AuraPreviewCommand
{
    public static string Apply(GameState state, GameData data, string argument)
    {
        if (!state.Characters.TryGetValue(state.UserCharIndex, out var ch))
            return "Entrá al juego para previsualizar un aura.";
        string arg = argument.Trim().ToLowerInvariant();
        if (arg is "off" or "0" or "quitar")
        {
            ch.PreviewAuraIndex = 0;
            return "Previsualización quitada. Se muestran las auras de tu equipo.";
        }
        int id;
        if (arg is "" or "siguiente" or "+" or "anterior" or "-")
        {
            int step = arg is "anterior" or "-" ? -1 : 1;
            id = ch.PreviewAuraIndex;
            if (id == 0 && Valid(data, 97)) id = step > 0 ? 96 : 98;
            for (int n = 0; n < data.Auras.Length; n++)
            {
                id = (id + step + data.Auras.Length) % data.Auras.Length;
                if (Valid(data, id)) break;
            }
        }
        else if (!int.TryParse(arg, out id))
            return "Uso: /aura [ID | siguiente | anterior | off]. Sin argumento pasa a la siguiente; comienza en 97.";
        if (!Valid(data, id)) return "Aura inexistente o sin gráfico. Usá /aura 97 para probar Vigilia.";
        ch.PreviewAuraIndex = id;
        return $"Aura {id}: previsualización local (solo vos). /aura: siguiente · /aura anterior · /aura off."
            + (!(state.Config?.ShowAuras ?? true) ? " Activá las auras en Opciones para verla." : "")
            + (ch.Navigating ? " Se verá al bajar del barco." : "");
    }

    private static bool Valid(GameData data, int id) => id > 0 && id < data.Auras.Length
        && (data.Auras[id].ProceduralStyle > 0 || data.Auras[id].GrhIndex > 0);
}

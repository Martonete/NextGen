using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Shared utility methods used across all PacketHandler partial classes.
/// </summary>
public partial class PacketHandler
{
    /// <summary>
    /// Parse an integer from a trimmed string, returning 0 on failure.
    /// </summary>
    internal static int ParseInt(string s)
    {
        return int.TryParse(s.Trim(), out int v) ? v : 0;
    }

    /// <summary>
    /// VB6: head values 500, 501, 511, 512 indicate a dead (ghost/casper) character.
    /// TCP.bas: If charlist(charindex).Head.Walk(3).GrhIndex = 500/501/511/512 Then .Muerto = True
    /// </summary>
    internal static bool IsDeadHead(int head)
    {
        return head == 500 || head == 501 || head == 511 || head == 512;
    }

    /// <summary>
    /// Clear meditation FX from a character (called when they move).
    /// </summary>
    internal static void ClearMeditationFx(Character ch)
    {
        for (int i = 0; i < 3; i++)
        {
            if (ch.ActiveFxSlots[i] > 0 && MeditationFxIds.Contains(ch.ActiveFxSlots[i]))
            {
                ch.ActiveFxSlots[i] = 0;
                ch.FxLoops[i] = 0;
                ch.FxFrameCounter[i] = 0;
            }
        }
    }

    /// <summary>
    /// VB6 Dialogos.CreateDialog: set dialog bubble on a character.
    /// Duration = 5000 + 100 * text.Length ms.  Replaces any previous dialog.
    /// </summary>
    internal void SetCharDialog(int charIndex, string text, string hexColor)
    {
        if (!_state.Characters.TryGetValue(charIndex, out var ch)) return;

        ch.DialogText = text;
        ch.DialogColor = hexColor;
        ch.DialogStartMs = System.Environment.TickCount64;
        ch.DialogDurationMs = 3000 + 50 * text.Length;
        ch.DialogRiseCounter = 18f; // VB6 Sube = 18
        ch.DialogAlpha = 20f;       // VB6 Desvanecimiento = 20
        ch.DialogFading = false;
    }

    /// <summary>VB6: MENSAJE_GOLPE_* — NPC hit text by body part.</summary>
    internal static string GetNpcHitBodyPartText(int bodyPart)
    {
        return bodyPart switch
        {
            1 => "La criatura te ha pegado en la cabeza por ",
            2 => "La criatura te ha pegado el brazo izquierdo por ",
            3 => "La criatura te ha pegado el brazo derecho por ",
            4 => "La criatura te ha pegado la pierna izquierda por ",
            5 => "La criatura te ha pegado la pierna derecha por ",
            6 => "La criatura te ha pegado en el torso por ",
            _ => "La criatura te ha pegado en la cabeza por "
        };
    }

    /// <summary>VB6: MENSAJE_RECIVE_IMPACTO_* — PvP received hit text by body part.</summary>
    internal static string GetPvpReceivedBodyPartText(int bodyPart)
    {
        return bodyPart switch
        {
            1 => " te ha pegado en la cabeza por ",
            2 => " te ha pegado el brazo izquierdo por ",
            3 => " te ha pegado el brazo derecho por ",
            4 => " te ha pegado la pierna izquierda por ",
            5 => " te ha pegado la pierna derecha por ",
            6 => " te ha pegado en el torso por ",
            _ => " te ha pegado en la cabeza por "
        };
    }

    /// <summary>VB6: MENSAJE_PRODUCE_IMPACTO_* — PvP dealt hit text by body part.</summary>
    internal static string GetPvpDealtBodyPartText(int bodyPart)
    {
        return bodyPart switch
        {
            1 => " en la cabeza por ",
            2 => " en el brazo izquierdo por ",
            3 => " en el brazo derecho por ",
            4 => " en la pierna izquierda por ",
            5 => " en la pierna derecha por ",
            6 => " en el torso por ",
            _ => " en la cabeza por "
        };
    }
}

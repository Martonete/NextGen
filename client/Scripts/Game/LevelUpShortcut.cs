namespace ArgentumNextgen.Game;

public static class LevelUpShortcut
{
    public static bool TryCommand(int level, out string command, out string message)
    {
        command = message = "";
        if (level >= 50) { message = "Ya alcanzaste el nivel maximo (50)."; return false; }
        if (level < 1) { message = "Primero ingresa con un personaje."; return false; }
        command = "/SUBIRNIVEL";
        return true;
    }
}

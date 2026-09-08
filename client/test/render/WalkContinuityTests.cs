using System;
using ArgentumNextgen.Game;

static class WalkContinuityTests
{
    public static void Run()
    {
        var crops = new ArgentumNextgen.Rendering.WalkSpriteLayout.Crop[] {
            new(30019, 20, 150, 40, 59), new(30019, 79, 150, 43, 59),
            new(30019, 140, 150, 46, 59), new(30019, 207, 150, 43, 59),
            new(30019, 276, 150, 40, 59), new(30019, 337, 150, 47, 59),
            new(30019, 394, 150, 54, 59), new(30019, 465, 150, 47, 59) };
        var offsets = ArgentumNextgen.Rendering.WalkSpriteLayout.Register(crops)!;
        Require(offsets != null, "Nigromante cropped strip recognized");
        for (int i = 0; i < crops.Length; i++)
        {
            float collar = 32 + i * 64 - crops[i].X - crops[i].Width / 2 + offsets![i].X;
            Require(collar == 0, "Every crop uses the same source-cell neck anchor");
        }
        for (int i = 0; i < 16; i++)
            Require(ArgentumNextgen.Rendering.WalkSpriteLayout.Frame(i, 16, 4) == i / 4,
                "Equipment follows normalized body phase, without repeated fast cycles");
        crops[3] = crops[3] with { File = 999 };
        Require(ArgentumNextgen.Rendering.WalkSpriteLayout.Register(crops) == null,
            "Mixed-sheet animation is not guessed");
        var normal = new ArgentumNextgen.Rendering.WalkSpriteLayout.Crop[] {
            new(1,0,0,32,48), new(1,32,0,32,48), new(1,64,0,32,48), new(1,96,0,32,48) };
        Require(ArgentumNextgen.Rendering.WalkSpriteLayout.Register(normal) == null,
            "Uniform legacy bodies retain their original placement");
        Console.WriteLine("PASS: Cropped armor registration and different equipment frame counts");
        foreach (int fps in new[] { 20, 30, 60, 144, 240 })
        {
            var ch = new Character { WalkFrame = 2.5f, WalkFrameHeading = 2,
                PosX = 50, PosY = 50, MoveOffsetX = -10f };
            ch.UpdateWalkContinuity(true, 1000f / fps);
            ch.UpdateWalkContinuity(false, 1000f / fps);
            Require(ch.WalkPoseActive && ch.WalkFrame == 2.5f, "Tile boundary retains stride");
            Require(!ch.Moving, "Cosmetic continuity never sets gameplay moving flag");
            ch.Heading = 3;
            ch.UpdateWalkContinuity(true, 1000f / fps);
            Require(ch.WalkFrame == 2.5f, "Turning retains phase");
            for (int i = 0; i < fps; i++) ch.UpdateWalkContinuity(false, 1000f / fps);
            Require(!ch.WalkPoseActive && ch.WalkFrame == 0, "Stopping returns to rest");
            Require(ch.PosX == 50 && ch.PosY == 50 && ch.MoveOffsetX == -10f,
                "Presentation cannot move character or alter speed");
            Console.WriteLine($"PASS: Walk continuity, turns, stop and unchanged position at {fps} FPS");
        }
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}

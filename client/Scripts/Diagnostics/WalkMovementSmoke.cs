using System;
using System.Reflection;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Diagnostics;

/// <summary>Exercises the real movement update offline, without a connection or character save.</summary>
public static class WalkMovementSmoke
{
    public static void Run(GameData data)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var main = new Main(); // Deliberately outside the tree: do not run login/UI setup.
        try
        {
            typeof(Main).GetField("_gameData", flags)!.SetValue(main, data);
            var state = (GameState)typeof(Main).GetField("_state", flags)!.GetValue(main)!;
            var update = (Action<float>)typeof(Main).GetMethod("UpdateMovement", flags)!
                .CreateDelegate(typeof(Action<float>), main);
            foreach (int body in new[] { 1, 512, 513 })
            foreach (int fps in new[] { 20, 30, 60, 144, 240 })
            {
                var ch = new Character { CharIndex = 1, Body = body, PosX = 50, PosY = 50 };
                state.Characters.Clear();
                state.Characters[1] = ch;
                state.UserCharIndex = 1;
                state.UserPosX = 50;
                state.UserPosY = 50;
                float delta = 1f / fps;
                float frameMs = delta * 1000f;
                // Reference client: 32 px at 120 px/s, integrated on a 60 Hz tick.
                const float StepMs = 32f / 120f * 1000f;
                const float TickMs = 1000f / 60f;
                int maxFrames = (int)Math.Ceiling((StepMs + TickMs) / frameMs) + 1;
                for (int step = 0; step < 40; step++)
                {
                    // Same start state as TryMove; walk a closed loop, including turns.
                    int heading = step / 10 + 1;
                    int dx = heading == 2 ? 1 : heading == 4 ? -1 : 0;
                    int dy = heading == 3 ? 1 : heading == 1 ? -1 : 0;
                    ch.Heading = heading;
                    ch.PosX += dx; ch.PosY += dy;
                    ch.MoveOffsetX = -32 * dx; ch.MoveOffsetY = -32 * dy;
                    ch.ScrollDirectionX = dx; ch.ScrollDirectionY = dy;
                    ch.Moving = true;
                    state.UserPosX = ch.PosX; state.UserPosY = ch.PosY;
                    state.AddToUserPosX = dx; state.AddToUserPosY = dy;
                    state.ScreenOffsetX = 0; state.ScreenOffsetY = 0;
                    state.UserMoving = true; state.PendingMoves = 1;
                    int frames = 0;
                    do
                    {
                        update(delta);
                        frames++;
                        float relativeX = 32 * (ch.PosX - state.UserPosX + state.AddToUserPosX)
                            + ch.MoveOffsetX - state.ScreenOffsetX;
                        float relativeY = 32 * (ch.PosY - state.UserPosY + state.AddToUserPosY)
                            + ch.MoveOffsetY - state.ScreenOffsetY;
                        Require(Math.Abs(relativeX) < .001f && Math.Abs(relativeY) < .001f,
                            "Character/camera diverged during step");
                        Require(ch.WalkPoseActive, "Idle pose flashed at a tile boundary");
                        Require(frames <= maxFrames, "Movement failed to complete");
                    } while (state.UserMoving);
                    // The tick carries its remainder across steps, so a single step
                    // lands within one frame and one tick of the reference duration.
                    Require(Math.Abs(frames * frameMs - StepMs) <= frameMs + TickMs,
                        "Step duration changed");
                    Require(state.PendingMoves == 0, "Pending movement did not clear");
                }
                Require(ch.PosX == 50 && ch.PosY == 50, "Turn path changed");
                for (int i = 0; i < fps; i++) update(delta);
                Require(!ch.WalkPoseActive && !ch.Moving, "Stop did not restore idle");
                GD.Print($"[WALK-SMOKE] PASS body {body}, {fps} FPS: 40 steps, 266.7 ms each, stable camera, turns and stop");
            }
        }
        finally { main.Free(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System;
using Godot;
using ArgentumNextgen.Data;

namespace ArgentumNextgen.Game;

/// <summary>Snapshot of a server-confirmed hit; survives the victim's lethal removal.</summary>
public sealed class WeaponImpact
{
    public readonly Character Owner;
    public readonly MapData? Map;
    public readonly Vector2 Position;
    public readonly int Kind, Heading;
    public bool Critical;
    public float Age;
    public Vector2 Direction;
    public float Duration => Kind == 202 ? .32f : Kind == 203 ? .18f : .24f;

    public WeaponImpact(Character owner, MapData? map, int kind, int payload)
    {
        Owner = owner; Map = map; Kind = kind; Heading = payload & 7;
        Critical = (payload & 8) != 0;
        Position = new(owner.PosX * 32 + owner.MoveOffsetX + 16,
            owner.PosY * 32 + owner.MoveOffsetY + 4);
    }

    public static bool Valid(int kind, int payload) => kind >= 201 && kind <= 206
        && payload >= 1 && payload <= 12 && (payload & 7) >= 1 && (payload & 7) <= 4;
}

using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace TierrasSagradasAO.Data;

/// <summary>
/// Loads weapon and shield animation data from INI files (Armas.dat, Escudos.dat).
/// These files contain per-class lists of available weapons/shields.
/// The Anim field comes from the object data, not these files.
/// For the client renderer, weapon/shield animations are derived from the CC packet's
/// weaponAnim/shieldAnim fields which index into body-like animation sets.
/// </summary>
public static class WeaponShieldLoader
{
    /// <summary>
    /// Weapon animations use the same format as bodies: 4 direction GRHs.
    /// They're stored in Personajes.ind as body indices, reusing BodyData structure.
    /// The weaponAnim from CC packet indexes directly into the Bodies array.
    /// No separate loading needed — weapons/shields share the body animation system.
    /// </summary>
    public static void LogInfo()
    {
        GD.Print("[WEAPON] Weapon/Shield anims use body animation indices from CC packet");
    }
}

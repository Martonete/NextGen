# Skill References

Actionable quick-reference documents for working with specific AO subsystems. Each skill is self-contained with exact formulas, constants, and code references.

## Server Skills

| Skill | Source Files | Key Content |
|-------|-------------|-------------|
| [ao-combat.md](ao-combat.md) | `handlers/combat.rs`, `constants.rs` | Attack power formulas (4 skill brackets), damage calc, critical 1.8x, backstab, PvP flow, death/resurrection |
| [ao-spells.md](ao-spells.md) | `handlers/spells.rs` | LH→RC cast flow, 7 spell types, damage formula with level/staff/lute scaling, mana modifiers, status effects, Mimetiza |
| [ao-npc-ai.md](ao-npc-ai.md) | `npc.rs`, `handlers/npcs.rs` | 10+ AI types, NpcState (40+ fields), vision range, combat, drops, pet system, Pretoriano clan AI |
| [ao-skills-crafting.md](ao-skills-crafting.md) | `handlers/skills/*.rs`, `constants.rs` | 22 skill IDs, leveling formula, fishing/logging/mining, smithing/carpentry/smelting, taming, stealing, hiding |

## Client Skills

| Skill | Source Files | Key Content |
|-------|-------------|-------------|
| [ao-sprite-indexing.md](ao-sprite-indexing.md) | `Data/GrhLoader.cs` | Graficos.ind format (3 header modes), GrhData struct, 2-pass loading, water GRH 1505-1520, color key transparency |
| [ao-ui-kit.md](ao-ui-kit.md) | `UI/RpgBaseForm.cs`, `UI/RpgTheme.cs` | 4 form styles (v1-v4), Z-index layers, RpgTheme factory, creating new panels |
| [ao-rendering.md](ao-rendering.md) | `Rendering/WorldRenderer.cs`, `CharRenderer.cs` | 6-pass pipeline, GPU lightmap, heading-dependent char draw order, water reflections, roof fade |
| [ao-protocol.md](ao-protocol.md) | `Network/ByteQueue.cs` | ByteQueue types (all LE), binary packet format, key packets (CC/BP/MP), how to add new packets |

## Map Tools

| Skill | Key Content |
|-------|-------------|
| [mapper.md](mapper.md) | .map binary format (273-byte header + tiles), .inf exits/NPCs/objects, .dat metadata, trigger types, Python patching |

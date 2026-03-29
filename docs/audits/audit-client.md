# Argentum Online Client — Full Audit Report
Generated: 2026-03-28 | Scope: /workspace/argentum-nextgen/client/ (~110 .cs files, ~35K lines)

---

## Dead Code

### LoginController — Entire class never instantiated
**File:** `Scripts/UI/LoginController.cs` (144 lines) | **Severity: HIGH**

`LoginController` is defined but never instantiated anywhere in the codebase. The actual login flow uses `UI.LoginForm` directly. This is an orphaned intermediate refactoring artifact. The entire file can be deleted.

### HandleBinCharacterInfo — Reads 10 fields, discards all
**File:** `Scripts/Network/PacketHandler.Movement.cs:748-760` | **Severity: HIGH**

```csharp
// Reads: name, race, class, gender, level, gold, bankGold, reputation, description, guildName
// All assigned to local variables that are never used
```
The handler exits without updating `_state` or any UI. Either this opcode is never sent by the server (dead path) or the implementation is missing. The data read is discarded entirely.

### HandleBinNavigationData — Reads a string, does nothing
**File:** `Scripts/Network/PacketHandler.Movement.cs:800-803` | **Severity: MEDIUM**

```csharp
private void HandleBinNavigationData(ByteQueue bq) {
    string _ = bq.ReadString(); // discarded
}
```
No state update, no UI show. Dead handler.

### HandleBinTimerInfo — Explicitly unimplemented
**File:** `Scripts/Network/PacketHandler.Combat.cs:599-605` | **Severity: MEDIUM**

Reads `id`, `time1`, `time2` then discards them with a comment: *"Scroll timers not yet implemented"*. The read must still happen to advance the byte queue, but the comment acknowledges this is dead behavior.

### ShowSignal opcode (58) — Data read and discarded
**File:** `Scripts/Network/PacketHandler.Binary.cs` (ShowSignal case) | **Severity: MEDIUM**

```csharp
case ServerPacketId.ShowSignal: {
    string text = bq.ReadString();
    int grh = (ushort)bq.ReadInteger();
    // both vars unused — no UI shown, no state set
    break;
}
```

### SpawnList (90), ShowSOSForm (91), ShowMOTDEditionForm (92), UserNameList (94), AddSlots (103) — Data discarded
**File:** `Scripts/Network/PacketHandler.Binary.cs` | **Severity: MEDIUM each**

All five opcodes read data from the packet but assign to `_` or local vars that go unused. The corresponding panels (`_spawnListPanel`, `_sosPanel`, `_motdEditorPanel`) exist in the scene tree (initialized in `Main.Setup.cs`) but these handlers never call `.Show()` or populate them.

### ShowPartyForm (106) — pType discarded
**File:** `Scripts/Network/PacketHandler.Binary.cs` (ShowPartyForm case) | **Severity: LOW**

```csharp
case ServerPacketId.ShowPartyForm: {
    byte pType = bq.ReadByte(); // read but never used
    _state.ShowPartyPanel = true;
    break;
}
```
`pType` is read to advance the queue but the party panel ignores it.

### ShowGMPanelForm (93), ShowGuildAlign (95), MeditateOK (161), BankOK (85), FinishOK (77) — Empty bodies
**File:** `Scripts/Network/PacketHandler.Binary.cs` | **Severity: LOW each**

These opcode cases perform no reads and trigger no UI. They are structurally present but behaviorally inert. If the server sends them they are silently consumed.

### Doubled doc-comment and section-header blocks — Merge artifact
**File:** `Scripts/Network/PacketHandler.Movement.cs` | **Severity: LOW**

The following sections each contain their XML doc block and/or comment header duplicated:
- `// ── Sound / Music ──` at lines 419 AND 422
- `// ── Objects on ground ──` at lines 386 AND 389
- `// ── Mount / Levitate ──` at lines 481 AND 489
- UserMount, Levitate, AnimData, Arrow, NavigateBroadcast — duplicate `<summary>` XML blocks

These are merge artifacts and create reader confusion. Remove duplicates.

### Debug logging left in production — CharRenderer
**File:** `Scripts/Rendering/CharRenderer.cs:105` | **Severity: MEDIUM**

```csharp
if (!ch._debugLogged) {
    GD.Print($"[CHAR] '{ch.Name}' first draw ...");
    ch._debugLogged = true;
}
```
Also: `GD.PrintErr` for helmet (lines 453, 461, 471) and shield (lines 504, 512, 521) guarded by `ch._equipDebugLogged`. These add `_debugLogged` and `_equipDebugLogged` boolean fields to the `Character` class solely to rate-limit debug output. All should be removed before release.

### ItemSafety field — Explicitly tagged unused
**File:** `Scripts/Game/GameState.cs:124` | **Severity: LOW**

```csharp
public bool ItemSafety; // Unused — item drop safety removed (13.3 parity)
```
The comment confirms this is dead. Remove the field and any writers to it.

---

## Giant Files

All files exceeding 800 lines violate the 800-line hard limit. Split suggestions follow.

### RpgTheme.cs — 1730 lines
**File:** `Scripts/UI/RpgTheme.cs` | **Severity: HIGH**

Monolithic static factory class. Suggested split into 5 partial files:
- `RpgTheme.Assets.cs` — asset catalog dictionaries, `GetTex`, `GetScaledTex`, texture caching
- `RpgTheme.Layout.cs` — layout helpers, container builders, NinePatch panels
- `RpgTheme.Controls.cs` — labels, buttons, checkboxes, sliders, input fields
- `RpgTheme.Advanced.cs` — dropdowns, tab bars, scroll areas
- `RpgTheme.Slots.cs` — inventory slot creation and slot-grid builders

### Main.cs — 1285 lines
**File:** `Scripts/Main.cs` | **Severity: HIGH**

Already has partial companions (`Main.Setup.cs`, `Main.Gameplay.cs`) but the base file still contains `RepositionUI()` (~180 lines) and `CenterPanelsInViewport()` (~50 lines). Suggested additions:
- Extract `RepositionUI()` + `CenterPanelsInViewport()` → `Main.Layout.cs`
- Extract signal wiring block → `Main.Signals.cs`

### PacketHandler.Movement.cs — 950 lines
**File:** `Scripts/Network/PacketHandler.Movement.cs` | **Severity: HIGH**

Despite the name, handles: map changes, character lifecycle, objects, sound, FX, particles, lights, auras, navigation, forum, AND movement. Suggested split:
- `PacketHandler.Characters.cs` — CharacterCreate, CharacterRemove, CharacterMove, CharacterChange
- `PacketHandler.Map.cs` — MapInfo, MapObjects, MapParticles, MapLights
- `PacketHandler.FX.cs` — Sound, Music, FXPlay, Aura, AnimData, Arrow
- `PacketHandler.Movement.cs` (retain) — only movement and navigation opcodes

### WorldRenderer.cs — 858 lines
**File:** `Scripts/Rendering/WorldRenderer.cs` | **Severity: HIGH**

Already has `WorldRenderer.Content.cs` and `WorldRenderer.Layers.cs` companions. The remaining 858 lines contain the full `_Draw()` method (~300 lines). Suggested:
- Extract `_Draw()` passes → `WorldRenderer.Draw.cs`
- Extract lightmap management → `WorldRenderer.Lightmap.cs`

### PacketHandler.Binary.cs — 829 lines
**File:** `Scripts/Network/PacketHandler.Binary.cs` | **Severity: HIGH**

Central dispatch switch with 100+ cases. The switch itself is fine as a dispatch table, but the inline handler bodies that are more than 3 lines should be extracted to their handler files. The `BuildKnownOpcodes()` reflection helper and the core `HandleBinaryData` loop can stay.

### Main.Gameplay.cs — 809 lines
**File:** `Scripts/Main.Gameplay.cs` | **Severity: MEDIUM**

Contains screen change, fullscreen toggle, config apply, disconnect, reset, map load, connect, and movement tick. Suggested:
- Extract `ResetGameState()` (~100 lines) → each subsystem gets its own `Reset()` called from a thin orchestrator
- Extract movement tick → `Main.Movement.cs`

### OptionsPanel.cs — 793 lines + OptionsPanel.Clan.cs — 652 lines
**File:** `Scripts/UI/OptionsPanel.cs` | **Severity: MEDIUM**

Together 1445 lines. Already split by clan tab but each partial is still large. The "Video" tab builder alone is ~200 lines and could become `OptionsPanel.Video.cs`.

### PacketHandler.Combat.cs — 759 lines
**File:** `Scripts/Network/PacketHandler.Combat.cs` | **Severity: MEDIUM**

Contains combat, stats, skills, hunger/thirst, buffs, and chat message handling. Suggested:
- Extract stats/skills/attributes → `PacketHandler.Stats.cs`
- Extract buff/debuff handlers → `PacketHandler.Buffs.cs`

### CharRenderer.cs — 726 lines
**File:** `Scripts/Rendering/CharRenderer.cs` | **Severity: MEDIUM**

Static drawing class. Suggested:
- Extract helmet/shield/weapon layer drawing → `CharRenderer.Equipment.cs`
- Extract aura and particle layer drawing → `CharRenderer.FX.cs`

### GameState.cs — 710 lines
**File:** `Scripts/Game/GameState.cs` | **Severity: MEDIUM**

~100+ public fields. Suggested:
- Group into nested records/structs: `PlayerStats`, `MapState`, `UIState`, `InventoryState`, `GuildState`
- Or at minimum split into `GameState.Player.cs`, `GameState.Map.cs`, `GameState.UI.cs`

### SoundManager.cs — 690 lines
**File:** `Scripts/Game/SoundManager.cs` | **Severity: LOW**

Near the limit. Suggested extraction of MIDI/music subsystem → `MusicManager.cs` since music and SFX have separate lifecycles.

---

## Correctness

### Duplicate ResolutionManager.ApplyResolution call
**File:** `Scripts/Main.cs:477,486` | **Severity: HIGH**

```csharp
ResolutionManager.ApplyResolution(_config.ResolutionWidth, _config.ResolutionHeight, ...); // line 477
// ... some code ...
ResolutionManager.ApplyResolution(_config.ResolutionWidth, _config.ResolutionHeight, ...); // line 486
```
Identical arguments, called twice within the same `_Ready` method. The second call is redundant and may cause a double-resize flash on startup.

### First PacketHandler instance orphaned
**File:** `Scripts/Main.cs:524,567` | **Severity: HIGH**

```csharp
// Line 524 — created but only OnMapLoad is wired:
_packetHandler = new PacketHandler(_state, _tcp);
_packetHandler.OnMapLoad += ...;

// Line 567 — fully recreated inside login lambda, first instance abandoned:
_loginForm.OnLoginRequest += (user, pass) => {
    _packetHandler = new PacketHandler(_state, _tcp); // overwrites line-524 instance
    // wires all events here
};
```
The first `PacketHandler` at line 524 is never used for game traffic; it only holds the `OnMapLoad` handler that gets lost. Fix: create `PacketHandler` once, wire all events before the login lambda runs, or defer construction entirely to login time.

### HandleBinSendSkills reads 20 skills, state has 22
**File:** `Scripts/Network/PacketHandler.Combat.cs` | **Severity: HIGH**

```csharp
for (int i = 0; i < 20; i++) { // BUG: should be _state.Skills.Length (22)
    _state.Skills[i] = ...;
}
```
Skills at indices 20 and 21 are never populated from the server, leaving them at their default-initialized values. If the server sends 22 skills the last 2 are silently dropped.

### HandleBinHungerThirst — Identical implementation for two opcodes
**File:** `Scripts/Network/PacketHandler.Combat.cs` | **Severity: MEDIUM**

Opcode 60 (`HandleBinHungerThirst`) and opcode 128 (`HandleBinHungerThirst128`) are byte-for-byte identical: both read MaxAgua, MinAgua, MaxHam, MinHam in the same order and assign to the same state fields. Either they should be merged to a single handler or one of them reads a different field order (a protocol bug waiting to happen).

### _clanTab field never assigned
**File:** `Scripts/UI/OptionsPanel.cs:29` | **Severity: MEDIUM**

```csharp
private Control? _clanTab; // declared, never assigned
```
The clan content node is built in `OptionsPanel.Clan.cs` but the assignment back to `_clanTab` is missing. Any code path that checks `_clanTab != null` will always see null, and the clan tab content will never be shown. The tab button for it also appears to be absent (only "Juego" and "Video" tabs are wired).

### Panels initialized with null TCP connection
**File:** `Scripts/Main.Setup.cs` | **Severity: MEDIUM**

The following panels are initialized before a TCP connection exists:
- `_gmPanel.Init(_state, null)`
- `_spawnListPanel.Init(_state, null)`
- `_sosPanel.Init(_state, null)`
- `_motdEditorPanel.Init(_state, null)`
- `_guildAlignmentPanel.Init(_state, null)`
- `_peaceProposalPanel.Init(_state, null)`
- `_guildMemberPanel.Init(_state, null)`

TCP is wired via `SetTcp()` later in `HandleScreenChange(Screen.Game)`. Until then, any panel that tries to send a packet during `_Ready` or an early signal will throw a NullReferenceException. Verify all these panels are safe with null TCP until `SetTcp()` is called.

### Duplicate safe mode chat messages
**File:** `Scripts/Network/PacketHandler.Binary.cs` + `PacketHandler.Combat.cs` | **Severity: LOW**

"SEGURO ACTIVADO" and "SEGURO DESACTIVADO" messages are enqueued from both:
1. Direct opcodes 130/131 (`SafeOn`/`SafeOff`)
2. `MultiMessage` subtypes 5/6

If the server sends both, the player sees the message twice. Verify server intent and deduplicate.

---

## Optimization

### WorldRenderer._Process — QueueRedraw every frame unconditionally
**File:** `Scripts/Rendering/WorldRenderer.cs:372` | **Severity: HIGH**

```csharp
public override void _Process(double delta) {
    QueueRedraw(); // called every frame regardless of state change
}
```
Then inside `_Draw()` (lines 783-791), `QueueRedraw()` is called on all 9 child draw layers:
```csharp
_reflectedAuraLayer.QueueRedraw();
_reflectionBodyLayer.QueueRedraw();
// ... 7 more
```
That is **10 QueueRedraw calls per frame**. Since Godot's CanvasItem already redraws on the next frame when QueueRedraw is called, calling it in `_Draw` has no additional effect for that frame — but calling it unconditionally from `_Process` forces a redraw every single frame even when nothing has changed (e.g., paused game, no character movement, no map animation). Add a dirty flag set only when map data, character positions, or light state changes.

### WorldRenderer.Content.cs — LINQ allocation in render loop
**File:** `Scripts/Rendering/WorldRenderer.Content.cs:48` | **Severity: HIGH**

```csharp
charsHere.OrderBy(i => _state.Characters[i].Y).ToList()
```
This runs every frame for every tile that contains 2+ characters at the same position. `OrderBy().ToList()` allocates:
1. An `IOrderedEnumerable` wrapper
2. A new `List<int>`
3. The enumerator object

Fix: use a pre-allocated sort buffer (similar to the `BuildCharPositionIndex` pool already used for the position index). Since tile co-occupation is rare, a small `static readonly List<int> _sortBuffer = new(8)` suffices.

### CharRenderer — Vector2 allocation on every call
**File:** `Scripts/Rendering/CharRenderer.cs:81` | **Severity: MEDIUM**

```csharp
Vector2 headOffset = new Vector2(0, -30); // allocated every Draw call
```
Fix:
```csharp
private static readonly Vector2 DefaultHeadOffset = new(0, -30);
```
This is called per-character per-frame.

### RpgTheme.GetScaledTex — Image allocation per unique (filename, size)
**File:** `Scripts/UI/RpgTheme.cs` (GetScaledTex method) | **Severity: MEDIUM**

`GetScaledTex` calls `new Image()` and `img.Resize(...)` to produce a scaled texture. This is cached per (filename, size) key so it only runs once per unique combination — which is acceptable. However the cache is a `Dictionary<string, Texture2D>` keyed on a string concatenation. Verify the key includes the size component, otherwise two different sizes of the same texture will collide.

### Per-frame panels that could use dirty flags
**File:** Various | **Severity: MEDIUM**

Several UI panels call `QueueRedraw()` from `_Process` unconditionally. Candidates for dirty-flag conversion:
- `StatsPanel` — redraws every frame; stats only change on packet receipt
- `InventoryPanel` — item grid redraws every frame; items only change on packet receipt
- `SpellsPanel` — spell cooldowns could tick with a timer node instead of `_Process`
- `BuffPanel` — buff duration bars tick but at ~1s granularity; a 0.1s timer is sufficient

### SoundManager.Init() — Unnecessary test load
**File:** `Scripts/Game/SoundManager.cs:175` | **Severity: LOW**

```csharp
LoadWav(2); // "test sound" — result discarded, GD.Print kept
```
This loads a WAV file at startup that is never used. Remove the test load.

---

## Unused Systems

### BattleTeamScores — Stored, never displayed
**File:** `Scripts/Game/GameState.cs:302` | **Severity: MEDIUM**

`BattleTeamScores` is set by its packet handler and stored as a formatted string, but no UI panel reads or displays it. The battle team scoreboard panel appears to be unimplemented.

### AmbientColorR/G/B — Redundant storage
**File:** `Scripts/Game/GameState.cs:303-304` | **Severity: LOW**

`HandleBinAmbientColor` copies received color into BOTH `AmbientColorR/G/B` AND `MapColorR/G/B`. Only `MapColorR/G/B` are read by `WorldRenderer`. `AmbientColorR/G/B` are written but never read. Remove the `AmbientColor*` fields and write only to `MapColor*`.

### Intelligence, Constitution, Charisma — Set but never displayed
**File:** `Scripts/Game/GameState.cs` | **Severity: MEDIUM**

`HandleBinAtributes` populates `Intelligence`, `Constitution`, `Charisma` alongside `Strength` and `Agility`. Only `Strength` and `Agility` are displayed in the stats bottom bar. The other three attributes are stored in state but no panel renders them. Either the UI is incomplete or these attributes are not used in client logic.

### ShowGuildAlign opcode (95) — Complete no-op
**File:** `Scripts/Network/PacketHandler.Binary.cs` | **Severity: MEDIUM**

```csharp
case ServerPacketId.ShowGuildAlign: {
    // nothing
    break;
}
```
No reads, no state update, no panel shown. The `_guildAlignmentPanel` is initialized in `Main.Setup.cs` with null TCP and never shown. Entire guild alignment feature appears unimplemented client-side.

### GM Panel, SOS Panel, MOTD Editor — Initialized but handler bodies empty
**File:** `Scripts/Network/PacketHandler.Binary.cs` + `Scripts/Main.Setup.cs` | **Severity: MEDIUM**

These panels are allocated, added to the scene tree, and initialized — but their show-triggers in the packet handler are empty bodies or discard the read data. The panels take memory and scene tree slots but are never made visible during gameplay.

### DeltaFq / Doppler — Constant defined, feature not implemented
**File:** `Scripts/Game/SoundManager.cs` | **Severity: LOW**

```csharp
private const float DeltaFq = 75f; // Doppler frequency delta
```
Comment acknowledges Doppler is not implemented. Spatial audio uses Godot's built-in distance attenuation only. Remove the constant or implement the feature.

---

## Code Quality

### RepositionUI() — 30+ repetitive null checks
**File:** `Scripts/Main.cs` (RepositionUI method, ~180 lines) | **Severity: MEDIUM**

The method is a sequence of:
```csharp
if (_inventoryPanel != null) _inventoryPanel.Position = ...;
if (_spellsPanel != null) _spellsPanel.Position = ...;
// ... 28+ more identical patterns
```
Extract a helper:
```csharp
private static void SetPosition(Control? ctrl, Vector2 pos) => ctrl?.Position = pos;
// ... or collect all repositionable panels in a Dictionary<Control?, Func<Vector2>>
```
This reduces the method from ~180 lines to ~40.

### Unnecessary `as` cast for known type
**File:** `Scripts/Main.cs` | **Severity: LOW**

```csharp
_mapaButton = mapaButton as TextureButton;
```
`CreateRpgButton` already returns `TextureButton`. The `as` cast is redundant and introduces a nullable that requires a downstream null check. Use a direct cast or change the assignment to the concrete return type.

### ResetGameState() — 100 lines of manual field clearing
**File:** `Scripts/Main.Gameplay.cs` | **Severity: MEDIUM**

`ResetGameState()` manually zeros/nulls each field of `_state` individually. This is fragile — adding a new field to `GameState` requires remembering to also clear it here. Fix: add a `Reset()` method to `GameState` (or use `new GameState()` and reassign), or split state into subsystem objects each with their own `Reset()`.

### HandleBinHungerThirst — Duplicate method bodies
**File:** `Scripts/Network/PacketHandler.Combat.cs` | **Severity: MEDIUM**

(Also listed under Correctness.) The two methods are identical. Extract to a private helper:
```csharp
private void ApplyHungerThirst(ByteQueue bq) {
    _state.MaxAgua = bq.ReadByte();
    _state.MinAgua = bq.ReadByte();
    _state.MaxHam  = bq.ReadByte();
    _state.MinHam  = bq.ReadByte();
}
```
Call from both opcode 60 and 128 handlers.

### Doubled section comment headers — Merge artifact
**File:** `Scripts/Network/PacketHandler.Movement.cs` | **Severity: LOW**

See Dead Code section. Duplicated section headers (`// ── Sound / Music ──` etc.) and XML doc blocks should be deduplicated in a cleanup pass.

### WorldRenderer._Draw() — ~300-line method
**File:** `Scripts/Rendering/WorldRenderer.cs` | **Severity: MEDIUM**

The `_Draw()` override is approximately 300 lines handling PASS 1 (water tiles), reflections, character body layers, auras, content, particles, and roof. Each pass should be a private method:
```csharp
private void DrawWaterPass() { ... }
private void DrawReflections() { ... }
private void DrawCharacterLayers() { ... }
private void DrawParticles() { ... }
private void DrawRoof() { ... }
```
This makes the pass order explicit and each method independently readable.

### Static class CharRenderer using instance-state workaround
**File:** `Scripts/Rendering/CharRenderer.cs` | **Severity: LOW**

`CharRenderer` is a static class that draws characters using data from `Character` objects. The `_debugLogged` and `_equipDebugLogged` flags (see Dead Code) were added to `Character` solely to support rate-limited debug output from this static class. Once those debug flags are removed (as recommended), verify no other state is being pushed onto `Character` objects to serve `CharRenderer`'s internal needs.

### Inconsistent UI panel null-TCP pattern
**File:** `Scripts/Main.Setup.cs` | **Severity: LOW**

Seven panels accept `null` for TCP in `Init()`. If any of these panels ever attempts to send a packet during initialization (e.g., in a `_Ready` override triggered by `AddChild`), it will NullReferenceException silently. Consider a two-phase init: `InitState(state)` at setup time, `InitNetwork(tcp)` at game-screen entry — or guard all TCP uses with `_tcp?.Send(...)` null-conditionals in the panel base class.

---

## Summary Table

| Category | Count | Critical | High | Medium | Low |
|----------|-------|----------|------|--------|-----|
| Dead Code | 14 | 0 | 3 | 7 | 4 |
| Giant Files | 11 | 0 | 5 | 4 | 2 |
| Correctness | 7 | 0 | 3 | 3 | 1 |
| Optimization | 6 | 0 | 2 | 3 | 1 |
| Unused Systems | 7 | 0 | 0 | 4 | 3 |
| Code Quality | 8 | 0 | 0 | 5 | 3 |
| **Total** | **53** | **0** | **13** | **26** | **14** |

### Top Priority Fixes (do first)

1. **Delete `LoginController.cs`** — zero-risk dead file removal
2. **Fix `HandleBinSendSkills` loop bound** — off-by-2 data loss (20 vs 22)
3. **Fix orphaned `PacketHandler` in `Main.cs`** — first instance loses its `OnMapLoad` handler
4. **Remove duplicate `ApplyResolution` call** — prevents startup double-resize
5. **Remove debug `GD.Print` from `CharRenderer`** — spams production logs
6. **Fix `WorldRenderer` QueueRedraw** — 10x redraws per frame, largest perf win
7. **Fix `OrderBy().ToList()` in `WorldRenderer.Content.cs`** — per-frame heap allocation in render path
8. **Merge `HandleBinHungerThirst` duplicate** — prevents future protocol divergence
9. **Assign `_clanTab` in `OptionsPanel`** — clan tab content is silently broken
10. **Purge `AmbientColorR/G/B` from `GameState`** — redundant fields written but never read

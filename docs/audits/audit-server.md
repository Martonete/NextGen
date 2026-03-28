# Argentum NextGen — Server Rust Audit Report

**Date:** 2026-03-28  
**Scope:** `/workspace/argentum-nextgen/server/source/` — 81 `.rs` files, ~40K lines  
**Method:** Static analysis (cargo not available in sandbox); cross-referenced VB6 original docs in `/workspace/argentum-nextgen/docs/skills/`

---

## Dead Code

### [CRITICAL] `#[allow(dead_code, unused_variables, unused_imports)]` suppressor on entire handlers module

**File:** `source/game/mod.rs:14`

```rust
#[allow(dead_code, unused_variables, unused_imports)]
pub mod handlers;
```

This single attribute silences every dead-code, unused-variable, and unused-import warning across every file in the `handlers/` subtree — roughly 30+ files and ~25K lines. It makes the compiler blind to all genuine dead code in the largest part of the codebase. **This must be removed and all resulting warnings addressed individually.** Until it is removed, none of the dead-code findings below can be caught by `cargo check` or `clippy`.

---

### [HIGH] Dead first `temp` binding in `poder_ataque_arma`

**File:** `source/game/handlers/combat.rs:39–47`

Lines 39–47 compute a `temp` value using integer-cast arithmetic. Lines 50–58 immediately rebind `temp` using `f64` arithmetic (the correct VB6-parity approach), making the first block entirely dead. The comment on line 48 even explains why the first approach was abandoned. The dead block should be deleted.

```rust
// Lines 39-47 — DEAD, immediately shadowed by lines 50-58
let temp = if skill < 31 {
    skill as i64 * class_mod as i64
} ...
// Lines 50-58 — this is the live computation
let temp = if skill < 31 {
    (skill as f64 * class_mod as f64) as i64
} ...
```

---

### [HIGH] `class_damage_modifier` — defined but never called

**File:** `source/game/handlers/combat.rs:1119`

The function is documented with "no longer used in PvP but kept for reference." It is imported in `npcs.rs:19` and `handlers/skills/combat.rs:14` but those imports also go unused (both files only reference it in their `use` statements, never in function bodies). The function and both `use` entries are dead.

---

### [HIGH] `calc_defense_power_with_balance` — defined but never called

**File:** `source/game/handlers/combat.rs:1028`

No call site exists anywhere in the codebase (confirmed by exhaustive grep). Should be removed or promoted to active use; leaving it masked by the module-level `#[allow]` is a maintenance hazard.

---

### [HIGH] Four unused imports in `npcs.rs`

**File:** `source/game/handlers/npcs.rs:18–19`

```rust
calc_attack_power, calc_defense_power, calc_armor_absorption_with_penetration,
class_damage_modifier,
```

None of these four symbols appear anywhere in the `npcs.rs` function bodies. The file has its own inline NPC armor calculation that does **not** use `calc_armor_absorption_with_penetration` (see Correctness section). These imports are dead and would be caught by clippy if the module-level `#[allow]` were removed.

---

### [MEDIUM] `CharPreview::to_addpj_data` — defined but not called

**File:** `source/db/charfile.rs:22`

`CharPreview::to_addpj_data()` returns a formatted string, but the actual character-preview send logic in `auth.rs` uses `binary_packets::write_add_pj` directly. No caller of `to_addpj_data` exists outside `charfile.rs`. The method is dead.

---

### [MEDIUM] Dead `map_w` / `map_h` variables in `decode_coords`

**File:** `source/game/handlers/mod.rs:110–112`

```rust
let (map, map_w, map_h) = state.users.get(&conn_id)
    .map(|u| (u.pos_map, 0i32, 0i32))
    .unwrap_or((0, 0, 0));
```

`map_w` and `map_h` are always set to `0` and never used — the actual dimensions are fetched on the next line via `state.grid_dimensions(map)`. These two bindings are dead. Under the module `#[allow]` they produce no warning.

---

### [MEDIUM] Entire integration test file disabled

**File:** `source/game/handlers/tests.rs`

The entire file is commented out with `// Integration tests disabled — require PostgreSQL. TODO: add DB-backed tests.` The file contributes nothing. Either implement the tests (use `sqlx::test` with a test DB, or mock the pool) or delete the file entirely to remove the confusion.

---

### [LOW] `#[allow(unused_imports)]` appears twice in protocol module

**File:** `source/protocol/mod.rs:12,14`

Duplicate `#[allow(unused_imports)]` attributes. One is sufficient; the second is redundant and suggests the underlying import situation was never cleaned up.

---

## Giant Files (>800 Lines) — Split Suggestions

All files below exceed the 800-line project maximum. Each suggestion retains the existing public API while extracting cohesive subsets into child modules.

| File | Lines | Priority |
|------|-------|----------|
| `source/game/types.rs` | 1655 | HIGH |
| `source/game/handlers/spells.rs` | 1553 | HIGH |
| `source/game/handlers/combat.rs` | 1450 | HIGH |
| `source/game/handlers/auth.rs` | 1291 | HIGH |
| `source/game/handlers/npcs.rs` | 1286 | HIGH |
| `source/game/handlers/guilds_handler.rs` | 1204 | HIGH |
| `source/game/handlers/common.rs` | 1167 | MEDIUM |
| `source/game/handlers/commerce.rs` | 1085 | MEDIUM |
| `source/db/charfile.rs` | 1033 | MEDIUM |
| `source/game/handlers/ticks/npc_ai.rs` | 1118 | HIGH |
| `source/game/handlers/inventory/use_item.rs` | 862 | MEDIUM |
| `source/game/handlers/gm_query.rs` | 876 | MEDIUM |

---

### `source/game/types.rs` (1655 lines)

Split into:
- `types/user_state.rs` — `UserState` struct + all its `impl` methods (~600 lines)
- `types/game_state.rs` — `GameState` struct + `send_data_bytes`, `spawn_npc`, `kill_npc`, respawn helpers (~500 lines)
- `types/enums.rs` — `SendTarget`, `InventorySlot`, `EquipSlots`, `PlayerClass`, `Race`, `SpellType` enums (~200 lines)
- `types/settings.rs` — `IntervalSettings`, `ServerConfig`, `ForumData`, `PartyState` (~200 lines)
- `types/mod.rs` — re-exports only

---

### `source/game/handlers/combat.rs` (1450 lines)

Split into:
- `combat/formulas.rs` — `poder_ataque_arma`, `poder_ataque_proyectil`, `calc_attack_power`, `calc_defense_power`, `calc_armor_absorption*`, `class_damage_modifier_from_balance` (~200 lines)
- `combat/melee.rs` — `handle_melee_attack`, `do_desequipar`, armor-break logic (~400 lines)
- `combat/death.rs` — `handle_player_death`, `handle_npc_death`, exp award, reputation tracking (~300 lines)
- `combat/pvp.rs` — criminal flag logic, safe-zone enforcement, PvP cooldown (~200 lines)
- `combat/mod.rs` — packet dispatch + re-exports (~150 lines)

---

### `source/game/handlers/spells.rs` (1553 lines)

Split into:
- `spells/damage_spells.rs` — `handle_spell_damage`, `subtract_magic_defense`, spell targeting (~400 lines)
- `spells/buff_spells.rs` — healing, shields, bless, inmunidad (~300 lines)
- `spells/summon_spells.rs` — NPC invocation, creature management (~200 lines)
- `spells/formulas.rs` — mana cost, damage formula, Druid Flauta discounts (~200 lines)
- `spells/mod.rs` — packet dispatch (~150 lines)

---

### `source/game/handlers/auth.rs` (1291 lines)

Split into:
- `auth/login.rs` — `handle_account_login`, rate-limit logic (~200 lines)
- `auth/register.rs` — `handle_create_account` (~150 lines)
- `auth/connect.rs` — `connect_user` (currently ~700 lines; this is itself a candidate for further extraction: charfile load, NPC visibility send, area broadcast are distinct phases)
- `auth/character.rs` — `handle_create_character`, `handle_delete_character`, `handle_roll_dice` (~200 lines)
- `auth/account.rs` — `handle_change_password`, `handle_account_recovery` (~100 lines)
- `auth/mod.rs` — dispatch

---

### `source/game/handlers/npcs.rs` (1286 lines)

Split into:
- `npcs/attack.rs` — `npc_attack_user`, damage calculation, drop logic (~400 lines)
- `npcs/spawn.rs` — NPC spawn helpers, map scan, respawn timer (~250 lines)
- `npcs/interaction.rs` — NPC dialogue, trade, quest hooks (~250 lines)
- `npcs/visibility.rs` — NPC visibility packets, area enter/leave (~200 lines)
- `npcs/mod.rs` — dispatch

---

### `source/game/handlers/ticks/npc_ai.rs` (1118 lines)

Split into:
- `ticks/npc_ai/hostile.rs` — `AI_HOSTILE_CHASE` branch: spell cast + melee attack logic (~350 lines)
- `ticks/npc_ai/movement.rs` — pathfinding, wander, `AI_PATHFINDING` branch (~300 lines)
- `ticks/npc_ai/passive.rs` — `AI_DEFENSE`, guard AI, `AI_PEACEFUL` branch (~250 lines)
- `ticks/npc_ai/mod.rs` — `tick_npc_ai` entry point + `update_map_user_counts` (~150 lines)

---

### `source/game/handlers/guilds_handler.rs` (1204 lines)

Split into:
- `guilds/info.rs` — `handle_guild_info`, member listing, applicant listing (~300 lines)
- `guilds/diplomacy.rs` — war/alliance declaration, peace handling (~250 lines)
- `guilds/management.rs` — create, disband, promote, kick, accept applicant (~350 lines)
- `guilds/bank.rs` — guild bank deposit/withdraw (~200 lines)
- `guilds/mod.rs` — dispatch

---

### `source/game/handlers/common.rs` (1167 lines)

Split into:
- `common/zone_checks.rs` — all five `is_*_blocked_at` helpers + parameterize (see Code Quality) (~100 lines)
- `common/position.rs` — `find_free_pos`, `is_pos_passable`, `move_user_to` (~200 lines)
- `common/visibility.rs` — `send_area_chars`, `send_area_npcs`, CC/CD broadcast helpers (~300 lines)
- `common/packets.rs` — `build_cd_binary`, `build_cc_binary` (~150 lines)
- `common/mod.rs` — re-exports + remaining utilities (~150 lines)

---

## Correctness

### [HIGH] PvP kill experience ignores `multiplicador_exp`

**File:** `source/game/handlers/combat.rs:1377`

```rust
let exp_gain = victim_level as i64;
```

The VB6 original (`MatarPersonaje` in `MuertePJ.bas`) multiplies the base experience by the server's `MultiplicadorExp` configuration value before awarding it. The Rust port omits this multiplication entirely. On a server configured with `multiplicador_exp > 1` (common for boosted rates), PvP kills award far less experience than intended. The fix:

```rust
let exp_gain = (victim_level as i64) * state.intervals.multiplicador_exp as i64;
```

---

### [HIGH] NPC melee armor absorption ignores weapon penetration (`refuerzo`)

**File:** `source/game/handlers/npcs.rs` (NPC melee attack function, approx lines 200–320)

The file imports `calc_armor_absorption_with_penetration` but never calls it. Instead, `npc_attack_user` uses an inline armor reduction that reads `user.defense` directly without applying any weapon-penetration modifier. The VB6 original (`AtacarPersonaje` in `CombatePJ.bas`) applies `refuerzo` (penetration) from the NPC's equipped weapon. The fix is to replace the inline calculation with the already-imported `calc_armor_absorption_with_penetration`.

---

### [MEDIUM] `poder_ataque_arma` dead block computes numerically different result

**File:** `source/game/handlers/combat.rs:39–47`

As noted under Dead Code, the first `temp` block uses integer-cast multiplication (`skill as i64 * class_mod as i64`), which truncates `class_mod` before multiplying. The live second block uses `f64` arithmetic. For `class_mod` values with fractional parts (e.g., `1.5`), the two formulas produce different results. The dead block was left in place with a misleading comment suggesting it might be the correct VB6 approach. It should be deleted to avoid future confusion when someone re-reads this code.

---

## Optimization

### [HIGH] `inventory.clone()` inside spell hot path

**File:** `source/game/handlers/spells.rs:213`

```rust
.map(|u| (u.class, u.equip.weapon, u.inventory.clone()))
```

This clones all 30 inventory slots on every spell cast to check whether the caster has a staff equipped. Only `equip.weapon` is needed; the inventory clone is unnecessary. Fix: examine `equip.weapon` directly without touching `inventory`:

```rust
.map(|u| (u.class, u.equip.weapon))
```

---

### [HIGH] Per-NPC `Vec<SpellId>` and `String` clones in every AI tick

**File:** `source/game/handlers/ticks/npc_ai.rs:72`

```rust
n.lanza_spells, n.spells.clone(), n.npc_type, n.attacked_by.clone(),
```

`n.spells` is a `Vec<i32>` and `n.attacked_by` is a `String`. Both are cloned for every active NPC on every AI tick (~100 ms). With hundreds of NPCs on a map, this is a significant per-tick allocation. Fix: pass references and thread them through the downstream functions, or restructure so the spell-list lookup and `attacked_by` check use references within the same borrow scope.

---

### [HIGH] `update_map_user_counts` rebuilds entire HashMap every AI tick

**File:** `source/game/handlers/ticks/npc_ai.rs:41`

```rust
update_map_user_counts(state);
```

This function iterates all logged-in users and reconstructs a `HashMap<i32, u32>` from scratch every 100 ms. It is called unconditionally at the top of every `tick_npc_ai` invocation. The map is used only to decide whether any players are present on an NPC's map (to skip ticking NPCs on empty maps). Fix: maintain an incremental `map_player_count: HashMap<i32, u32>` on `GameState` that is updated on connect/disconnect/map-change rather than rebuilt every tick.

---

### [MEDIUM] `send_data_bytes` allocates `Vec<ConnectionId>` for every broadcast

**File:** `source/game/types.rs:1100–1168`

Every `ToAll`, `ToMap`, `ToMapButIndex`, `ToGuildMembers`, and `ToAdmins` variant collects user IDs into a `Vec<ConnectionId>` before iterating, because `send_bytes` requires `&mut self`. With a typical server population this is a minor but constant allocation on all broadcast paths. Consider pre-allocating a reusable scratch buffer on `GameState` (e.g., `broadcast_scratch: Vec<ConnectionId>`) and clearing it between calls to eliminate the per-broadcast heap allocation.

---

### [MEDIUM] `connect_user` is a single ~700-line async function

**File:** `source/game/handlers/auth.rs:373`

`connect_user` loads the charfile, applies world state, sends all area NPCs/users, and broadcasts the new player's arrival — all in one function. Because it holds mutable references across multiple `await` points and conditional branches, the compiler generates a large state-machine future. Breaking it into sequenced sub-functions (`load_user_state`, `broadcast_user_arrival`, `send_visible_entities`) would reduce the future size and make the code easier to profile.

---

### [LOW] Repeated `grid_dimensions` call in `find_free_pos`

**File:** `source/game/handlers/common.rs` (approx lines 366 and 388)

`grid_dimensions(map)` is called twice within the same function with the same `map` argument, with no map mutation between calls. Cache the result in a local binding.

---

## Unused Systems

### [CRITICAL] Integration test suite entirely disabled

**File:** `source/game/handlers/tests.rs`

460 lines of integration tests are fully commented out. The test suite is the primary correctness safety net for a rewrite of this scope. The TODO note acknowledges the gap but no timeline exists. Recommended immediate action: use `sqlx::test` (which spins up an ephemeral Postgres instance per test) to re-enable at minimum the auth, charfile load, and combat formula tests.

---

### [HIGH] `CharPreview::to_addpj_data` — dead DB helper

**File:** `source/db/charfile.rs:22`

See Dead Code section. The method was apparently superseded by `binary_packets::write_add_pj`. It represents a diverged code path that could mislead future contributors. Remove it.

---

### [MEDIUM] `calc_defense_power_with_balance` — dead combat formula

**File:** `source/game/handlers/combat.rs:1028`

No caller. If this formula represents a future "balance mode" feature, it should be gated behind a feature flag and tracked in a GitHub issue, not left as unreachable dead code.

---

### [LOW] `do_desequipar` declared `async` without await points

**File:** `source/game/handlers/combat.rs:250`

`async fn do_desequipar` contains no `.await` expressions. Marking it `async` forces callers to `.await` it and wraps its return in a `Future`, adding overhead for no benefit. Change to a plain synchronous `fn`.

---

## Code Quality

### [CRITICAL] `.unwrap()` on `state.users.get()` inside tick loops — panic risk

Multiple tick functions collect `ConnectionId` values in phase 1 and then call `state.users.get(&id).unwrap()` in phase 2. If a user disconnects between the two phases (possible in async code at any `.await` point), the `unwrap()` panics and crashes the server process.

| File | Lines |
|------|-------|
| `source/game/handlers/ticks/world.rs` | 73, 75, 113, 114 |
| `source/game/handlers/gm_query.rs` | 30, 423, 435, 508, 519, 530, 579, 591 |
| `source/game/handlers/gm_server.rs` | 640 |
| `source/game/handlers/inventory/use_item.rs` | 282, 560 |

Fix: replace `.unwrap()` with `if let Some(user) = state.users.get(&id)` or `get_mut`, silently skipping the operation if the user has already disconnected.

---

### [CRITICAL] All guild DB write operations silently discard errors

**File:** `source/db/guilds.rs:210,242,246,264,281,297,301,349,371,418,423,476,487`

Every INSERT/UPDATE/DELETE in `guilds.rs` uses:

```rust
let _ = sqlx::query("...").execute(pool).await;
```

This discards `sqlx::Error` without logging or propagating it. A database connectivity failure, constraint violation, or serialization error would be silently swallowed, leaving guild state inconsistent between memory and the database. Fix: propagate errors via `?` or at minimum `tracing::error!` on failure:

```rust
sqlx::query("...").execute(pool).await
    .map_err(|e| { tracing::error!("guild save failed: {e}"); e })?;
```

---

### [HIGH] Duplicated rate-limit logic in `auth.rs`

**File:** `source/game/handlers/auth.rs:59–83` and `193–218`

An identical `RateLimitState` enum pattern and associated match block is copy-pasted verbatim into both `handle_account_login` and `handle_create_account`. Extract to a single:

```rust
fn check_rate_limit(
    failures: &mut HashMap<String, (u32, Instant)>,
    key: &str,
    max_attempts: u32,
    lockout: Duration,
) -> RateLimitResult { ... }
```

---

### [HIGH] Five zone-blocking helpers are identical modulo field names

**File:** `source/game/handlers/common.rs:117–184`

`is_magic_blocked_at`, `is_invi_blocked_at`, `is_invocar_blocked_at`, `is_ocultar_blocked_at`, and `is_resu_blocked_at` are structurally identical — each reads a different boolean field from `ZoneInfo` and a different boolean field from `MapInfo`. Extract a single parameterized helper:

```rust
fn is_action_blocked_at<F, G>(
    state: &GameState,
    map: i32, x: i32, y: i32,
    zone_field: F,
    map_field: G,
) -> bool
where
    F: Fn(&ZoneInfo) -> bool,
    G: Fn(&MapInfo) -> bool,
{ ... }
```

This reduces 68 lines to ~15 lines and eliminates 5 copy-paste maintenance points.

---

### [HIGH] `handle_guild_info` issues 3+ sequential DB queries

**File:** `source/game/handlers/guilds_handler.rs` (approx `handle_guild_info` function)

`load_guild`, `load_members`, `list_guilds`, and `load_applicants` are called sequentially with `.await` in between. All four are independent reads. Use `tokio::join!` or `futures::join!` to execute them concurrently:

```rust
let (guild, members, guilds, applicants) = tokio::join!(
    guilds::load_guild(&state.pool, guild_id),
    guilds::load_members(&state.pool, guild_id),
    guilds::list_guilds(&state.pool),
    guilds::load_applicants(&state.pool, guild_id),
);
```

---

### [MEDIUM] Excessive `info!()` logging at commerce entry points

**File:** `source/game/handlers/commerce.rs:138,152,169,177` (and ~10 more sites)

`info!()` calls at the entry of every commerce packet handler (buy, sell, equip, etc.) log every player interaction at INFO level. In production these generate log noise that buries genuine warnings. Downgrade to `tracing::debug!()` or `tracing::trace!()`.

---

### [MEDIUM] `connect_user` holds a ~700-line scope with multiple `await` points

**File:** `source/game/handlers/auth.rs:373`

Beyond the performance concern (noted in Optimization), `connect_user` mixes charfile DB I/O, state mutation, packet construction, and area broadcast in a single function body. This makes it extremely difficult to test any phase in isolation and means a panic in the area-broadcast phase leaves the user partially connected. The charfile load, state application, and broadcast phases should each be separate functions with clear ownership semantics.

---

### [LOW] NPC inventory fetched twice in the same commerce operation

**File:** `source/game/handlers/commerce.rs:60–66,173–178`

For buy/sell operations, the NPC's `npc_data` is fetched from `state.game_data.npcs` twice in the same handler call — once to validate the interaction and once to perform the transaction. Cache in a local binding at the top of the function and reuse.

---

## Summary Table

| Severity | Count | Top Items |
|----------|-------|-----------|
| CRITICAL | 5 | Module-level `#[allow]` suppressor; `.unwrap()` panics in tick loops; silent guild DB error discard; disabled test suite |
| HIGH | 17 | PvP EXP missing multiplier; NPC armor ignores penetration; dead functions; giant files; duplicated logic; sequential DB queries |
| MEDIUM | 11 | Zone-blocking helpers duplication; excessive commerce logging; `decode_coords` dead vars; `find_free_pos` double call; commerce double-fetch |
| LOW | 4 | `do_desequipar` async with no await; duplicate `#[allow]`; `update_map_user_counts` double compute avoided by caching |

**Verdict: BLOCKED — CRITICAL and HIGH issues must be resolved before merge.**

The two most impactful actions are:
1. Remove the `#[allow(dead_code, unused_variables, unused_imports)]` on `mod.rs:14` and address every resulting compiler warning. This alone will surface further dead code.
2. Replace all `state.users.get(...).unwrap()` in the tick and GM handler paths with defensive `if let Some(...)` guards to eliminate crash-on-disconnect panics.

# Autonomous Fight Resolution — Technical Design

> Status: design draft (Phase 0). Target content: latest AAC savage tier
> (M05S–M08S, with **M08S Howling Blade** as the worked example).

## 1. Goal

Give every party member the plugin, have everyone enter the same savage/ultimate
encounter, and have the plugin **autonomously perform the movement required to
clear** — not just draw an overlay. The resolution must be:

- **Consistent** — every client, given the same inputs, produces the same plan,
  so the 8 players don't collide or pick conflicting spots.
- **User-configurable** — the raid can choose *the strat* (who goes where) before
  the pull.
- **Reactive** — because mechanics are randomized (random targets, tethers,
  debuffs, safe-tile RNG), the concrete per-player movement must be computed
  *live* from the actual fight state, not pre-scripted.

This document is the design for that system. It is written against the existing
architecture so that the work is an **extension of the current engine, not a
rewrite**.

## 2. What already exists (the engine is done)

BossmodReborn already has a complete autonomous control loop. None of this needs
to be built:

| Capability | Where | Notes |
|---|---|---|
| Decide destination each frame | `BossMod/AI/AIBehaviour.cs` (`Execute`, `BuildNavigationDecision`) | Async pathfinding build to avoid frame stalls |
| Path around danger | `BossMod/Pathfinding/NavigationDecision.cs:33` (`Build`) + `ThetaStar.cs` | Rasterizes forbidden/goal zones, runs Theta* |
| Actually move the character | `BossMod/AI/AIController.cs:50-60` → `BossMod/Framework/MovementOverride.cs` | Converts destination to `hints.ForcedMovement`, injects movement input |
| Target + execute rotation | `AIBehaviour.SelectPrimaryTarget`, `BossMod/Autorotation/*` | |
| Stop/force movement for special mechanics | `AIHints.ImminentSpecialMode` (Pyretic/Freezing/gaze/knockback) | Already wired |

The engine consumes a single per-frame data structure, **`AIHints`**
(`BossMod/BossModule/AIHints.cs`). The two fields that drive positioning are:

- `ForbiddenZones` (`AIHints.cs:111`) — shapes the player must leave / avoid.
  Added via `AddForbiddenZone(...)` (`AIHints.cs:263`).
- `GoalZones` (`AIHints.cs:117`) — a list of `Func<WPos, float>` weight functions;
  the pathfinder is **attracted** toward higher-weight tiles. Helpers:
  `GoalSingleTarget`, `GoalProximity`, `GoalDonut`, `GoalRectangle`, etc.
  (`AIHints.cs:389-567`).

Boss modules populate `AIHints` every frame: each component overrides
`AddAIHints(slot, actor, assignment, hints)` (`BossMod/BossModule/BossComponent.cs:42`).
For example, `Components.GenericAOEs.AddAIHints` (`Components/GenericAOEs.cs:40`)
auto-adds a forbidden zone for every active AOE — this is *why* the AI dodges.

**Conclusion:** to make the AI walk to an assigned spot, a component only has to
push the right `GoalZones`/`ForbiddenZones` into `hints`. No engine change is
required.

## 3. The actual gap

Today, per-player *strat-dependent* positioning already exists in some modules —
but it is **advisory only**. Concrete example, M08S Wolves' Reign
(`Modules/Dawntrail/Savage/M08SHowlingBlade/WolvesReign.cs`):

- `WolvesReignConeCircle.SafeSpots(slot, actor)` (line 70) computes the **exact
  per-player safe spot** from:
  - live cast data (`jumpLoc`, `isCone`, set in `OnCastStarted`),
  - the player's role/assignment (`PartyRolesConfig`, line 75),
  - **a user-selected strat** (`M08SHowlingBladeConfig.ReignStrategy`: `Standard`
    / `Inverse` / `Any` / `Disabled`, line 76 + config at
    `M08SHowlingBladeConfig.cs`).
- But that result is fed **only** to `DrawArenaForeground` (line 50, a circle on
  the radar) and `AddMovementHints` (line 60, an arrow). It is **never pushed to
  `AddAIHints`/`GoalZones`.**

So the AI dodges the AOEs (forbidden zones are complete), but it is *not actively
driven to the assigned strat spot*. Where a mechanic has multiple safe spots and
the strat decides which one *this* player takes (light-party splits, clock
spots, tower soaks, tether stretches), the autonomous AI has no signal — it's
left to follow-the-leader heuristics.

There are two structural problems:

1. **No bridge** from "resolved per-player target position" → `GoalZones`.
2. **No reusable strat abstraction.** Every fight re-invents its own strategy
   enum + role→spot mapping (`ReignStrategy`, `GetLightparty`, hand-tuned angles).
   There is no shared model for "the raid's chosen strat" or for sharing it
   between players.

The assignment layer this document proposes fills exactly these two gaps.

## 4. Proposed architecture

Three new reusable pieces in a new namespace `BossMod.Assignments`, plus a thin
base component. Nothing below changes the AI engine; it only produces richer
`AIHints`.

```
              live fight state (casts, tethers, debuffs, actor positions)
                                   │
                                   ▼
   PartyRolesConfig ──►  IMechanicResolver.Resolve(state, assignment, strat)
   StrategyPreset   ──►        │  returns PlayerPlan { TargetPos?, Facing?,
                               │                       Action?, Soft/Hard }
                               ▼
                     AssignmentHintBridge.Apply(plan, hints)
                               │   GoalZones += strong attractor at TargetPos
                               │   ForbiddenZones unchanged (safety preserved)
                               ▼
                            AIHints  ──►  existing AIBehaviour / pathfinder
```

### 4.1 Player model — reuse `PartyRolesConfig`

`PartyRolesConfig` (`BossMod/Config/PartyRolesConfig.cs`) already provides the
canonical 8-slot identity model: `Assignment { MT, OT, H1, H2, M1, M2, R1, R2 }`,
plus `AssignmentsPerSlot` / `SlotsPerAssignment` / `EffectiveRolePerSlot`. This
is the shared key that makes resolution deterministic across clients: as long as
every player has filled in the same role assignment, every client computes the
same plan for everyone. **No new identity model is needed.**

### 4.2 `StrategyPreset` — generalize the per-fight strat enum

A reusable container for "the raid's chosen strat", replacing ad-hoc enums like
`ReignStrategy`.

```csharp
// BossMod/Assignments/StrategyPreset.cs  (sketch)
public sealed class StrategyPreset
{
    public string Name = "";
    public uint EncounterModuleType;            // ties preset to one module
    // mechanic-key -> chosen option (string keeps it serializable & extensible)
    public Dictionary<string, string> Choices = [];

    public string Choice(string mechanic, string fallback)
        => Choices.GetValueOrDefault(mechanic, fallback);
}
```

- Serialized to JSON, stored next to cooldown plans (`Autorotation/PlanDatabase`
  is the model to mirror) so a raid lead can author one and **share the file**;
  every player imports the same preset → identical assignments.
- Per-fight modules expose their legal choices (a small descriptor) so a future
  UI can render dropdowns. Existing per-fight config enums (`ReignStrategy`,
  etc.) become *the option set* for one mechanic key rather than bespoke code.

### 4.3 `IMechanicResolver` — reactive per-player resolution

The reactive core. One resolver per mechanic; it reads **live** state and returns
what *this* player should do.

```csharp
// BossMod/Assignments/IMechanicResolver.cs (sketch)
public readonly struct PlayerPlan
{
    public WPos? TargetPos;     // where to stand (null = no movement opinion)
    public Angle? Facing;       // for gazes / directional uptime
    public ActionID? Action;    // optional: action to weave (e.g. mitigation)
    public bool Hard;           // true = override uptime, must reach exactly
}

public interface IMechanicResolver
{
    // called every frame while the mechanic is active
    PlayerPlan Resolve(int slot, Actor actor,
                       PartyRolesConfig.Assignment assignment,
                       StrategyPreset strat);
    bool Active { get; }        // gated by live cast/state, set in OnCastStarted etc.
}
```

A resolver is essentially the existing `SafeSpots(...)` logic (WolvesReign:70),
but (a) returns a *single* chosen target (strat disambiguates the multiple
safe spots) and (b) reads its option from `StrategyPreset` instead of a bespoke
enum. Randomness is handled naturally: `TargetPos` is recomputed each frame from
the live `jumpLoc`/tether/debuff, so re-rolls and re-targets just flow through.

### 4.4 `AssignmentHintBridge` — turn a plan into movement

The missing bridge. Converts a `PlayerPlan` into `AIHints` so the **AI actually
walks there**.

```csharp
// BossMod/Assignments/AssignmentHintBridge.cs (sketch)
public static void Apply(in PlayerPlan plan, AIHints hints)
{
    if (plan.TargetPos is { } pos)
    {
        // strong, tight attractor at the assigned spot.
        // weight tuned high so it beats default uptime goal zones,
        // but forbidden zones still win (pathfinder won't cross danger).
        var weight = plan.Hard ? 10f : 2f;
        hints.GoalZones.Add(AIHints.GoalSingleTarget(pos, /*radius*/ 1f, weight));
    }
    if (plan.Facing is { } f)
        hints.ForbiddenDirections.Add((/* derive a thin allowed arc around f */));
    if (plan.Action is { } a)
        hints.ActionsToExecute.Push(/* a, target */);
}
```

Key property: **safety is preserved**. The bridge only *adds attraction*; the
forbidden zones produced by the AOE components are untouched, and the pathfinder
already refuses to cross them (`NavigationDecision.Build` rasterizes forbidden
zones before goals). Worst case of a bad assignment is suboptimal positioning,
not standing in an AOE.

### 4.5 Base component — `AssignedMechanic`

A thin `BossComponent` subclass that wires the three pieces, so per-fight code is
tiny:

```csharp
public abstract class AssignedMechanic(BossModule module, IMechanicResolver resolver)
    : BossComponent(module)
{
    public override void AddAIHints(int slot, Actor actor,
        PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!resolver.Active) return;
        var preset = AssignmentManager.PresetFor(Module);   // loaded strat
        var plan = resolver.Resolve(slot, actor, assignment, preset);
        AssignmentHintBridge.Apply(plan, hints);
    }
    // DrawArenaForeground / AddMovementHints reuse the same resolver,
    // so overlay and autonomous movement can never disagree.
}
```

This last point matters: today the overlay (`SafeSpots`) and any future AI
movement would be two code paths that could drift. Sharing one resolver
guarantees the human-readable hint and the autonomous action are identical.

## 5. Consistency model

Determinism across clients comes from **shared inputs, not coordination**:

1. Every player fills in the **same `PartyRolesConfig`** (already required for
   existing role hints).
2. Every player imports the **same `StrategyPreset`** file (shared by the raid
   lead).
3. Resolvers are **pure functions of (live world state, assignment, preset)**.
   World state is identical on all clients (it's the game), so every client
   computes the same 8 plans and each player executes their own slot's plan.

No client-to-client messaging is needed for correctness. (The existing
`BossMod/AI/Broadcast.cs` IPC could later be used to *verify* presets match or to
auto-distribute one, but it is not required for v1.)

## 6. Reactivity to randomness

Randomized mechanics are handled by keeping resolution per-frame and state-driven:

- **Random target / tether / debuff** → resolver reads the live actor data
  (`OnCastStarted`, `OnTethered`, `OnStatusGain`) and recomputes `TargetPos`. The
  bridge re-emits the goal zone each frame, so the AI re-paths as the situation
  updates. This is exactly how `WolvesReignConeCircle` already tracks `jumpLoc`.
- **Random safe tile (e.g. exploding floor patterns)** → resolver picks the
  surviving tile that matches the player's assigned region under the chosen
  strat; forbidden zones from the AOE component still guarantee safety.
- **Phase/positional uptime** → resolvers can return `Hard = false` so normal
  uptime goal zones blend in, or `Hard = true` to dominate when survival
  requires an exact spot.

## 7. Worked example — converting M08S Wolves' Reign

Minimal, concrete migration showing the payoff:

1. Extract `WolvesReignConeCircle.SafeSpots` body into a
   `WolvesReignResolver : IMechanicResolver`. It already reads `jumpLoc`/`isCone`
   (live) and role (`PartyRolesConfig`).
2. Replace the `ReignStrategy` enum read with
   `strat.Choice("wolves_reign", "standard")` and map `Standard/Inverse/Any` the
   same way `GetLightparty` does today. The `M08SHowlingBladeConfig.ReignStrategy`
   field becomes the default that seeds the preset.
3. Resolver returns the **one** spot for the player's light-party side (today it
   returns up to two for "Any"; for autonomy `Any` should collapse to a default
   side, or stay advisory-only).
4. The component extends `AssignedMechanic`, so `AddAIHints` now emits a
   `GoalSingleTarget` at that spot. The AI walks there; `DrawArenaForeground`
   still shows the same point because it calls the same resolver.

Net change for the fight: a few dozen lines, and Wolves' Reign goes from
"overlay that hopes you walk to the right side" to "AI takes the assigned side."

## 8. Risks & limitations (be honest)

- **Content is the cost, not the framework.** Each mechanic needs its own
  resolver, hand-authored and tuned. A full unattended savage clear is many
  resolvers; an ultimate is many more. This design makes each one small and
  uniform, but does not remove the per-mechanic labor.
- **Multi-body mechanics** (defamation chains, dynamic stacks where positions
  depend on *others'* live positions) need resolvers that read the whole party's
  plans — solvable, but more complex than independent per-player spots.
- **Reliability/edge cases.** Mis-set role assignments or mismatched presets
  across the raid silently break consistency. v1 should add a pre-pull
  validation hint ("assignments invalid / preset mismatch").
- **ToS.** Fully unattended clearing is automation of the whole encounter and is
  against the game's ToS / is bannable, distinct from the assist/overlay framing
  of the plugin. Flagged for awareness; product decision, not a technical one.

## 9. Phased plan

- **Phase 1 (prototype — next step):** ship `StrategyPreset`,
  `IMechanicResolver` + `PlayerPlan`, `AssignmentHintBridge`, and
  `AssignedMechanic`, plus a single end-to-end resolver (M08S Wolves' Reign) as
  proof that "each person gets their own movement" drives the AI. No UI yet;
  preset loaded from a JSON file.
- **Phase 2:** preset authoring/sharing UI (mirror the cooldown-planner DB), and
  pre-pull validation.
- **Phase 3:** convert the remaining M08S mechanics, then template across the
  tier (M05S–M07S).
- **Phase 4:** multi-body resolvers; optional IPC preset distribution; tackle an
  ultimate.

## 10. File map (proposed for Phase 1)

```
BossMod/Assignments/StrategyPreset.cs          // strat container + JSON
BossMod/Assignments/IMechanicResolver.cs       // interface + PlayerPlan
BossMod/Assignments/AssignmentHintBridge.cs    // plan -> AIHints
BossMod/Assignments/AssignedMechanic.cs        // base BossComponent
BossMod/Assignments/AssignmentManager.cs       // preset load/lookup per module
Modules/Dawntrail/Savage/M08SHowlingBlade/
    WolvesReignResolver.cs                      // worked-example resolver
```

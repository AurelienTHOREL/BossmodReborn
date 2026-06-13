# Multibox Positional Teleport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in BMR-only feature that auto-TPs melee DPS alts to the boss's flank or rear for positional GCDs, snapshots the action, and returns them to main — skipping the TP cycle if the target spot is unsafe or True North is up.

**Architecture:** A single new class `MultiboxPositionalTp` owned by `Plugin`, ticked once per frame from the alt-side multibox update loop. Purely reactive: reads `Hints.RecommendedPositional`, the boss `PrimaryActor`, main's position from the sync state, and `ActionDefinitions.IsDashDangerous` for safety. No new IPC, no main-side coordination, no RSR changes. Uses the same `_amex.TeleportTo` primitive as the existing ctrl+click TP-pulse.

**Tech Stack:** C# 12 / .NET 10 (preview LangVersion), Dalamud plugin, existing BMR primitives. No test framework — verification is `dotnet build` plus an in-game manual test pass.

**Spec:** [`docs/superpowers/specs/2026-05-14-multibox-positional-tp-design.md`](../specs/2026-05-14-multibox-positional-tp-design.md)

---

## File Structure

**New file:**
- `BossMod/Multibox/MultiboxPositionalTp.cs` — the handler. State enum, per-frame `Update` method, target computation, all logic. ~150 lines.

**Modified files:**
- `BossMod/Multibox/MultiboxConfig.cs` — 3 new properties (enable toggle, return delay, cooldown).
- `BossMod/Framework/Plugin.cs` — field declaration, instantiation in `InitOnFrameworkThread`, per-frame call slotted into the existing alt-update path.

**No tests** — BossModReborn has no unit test infrastructure. Verification per task is `dotnet build` (with `DALAMUD_HOME` set or default Dalamud install path); end-to-end verification is the manual test pass in the final task.

---

## Build Verification Note

After every code-modifying step, run:

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded` with 0 errors. Warnings should be 0 added by your change (existing repo warnings are fine).

If the build fails on a step, fix the error before proceeding. Do not commit broken builds.

---

## Task 1: Add Config Properties

**Files:**
- Modify: `BossMod/Multibox/MultiboxConfig.cs` (append 3 new properties at end of class body, after `EnableDiveEndClick`)

- [ ] **Step 1.1: Add the three config properties**

Open `BossMod/Multibox/MultiboxConfig.cs`. Add these properties after the existing `EnableDiveEndClick` property (currently the last one), just before the closing `}`:

```csharp
    [PropertyDisplay("Auto-TP melee DPS alt to positional (requires BMR class preset on alt)")]
    public bool EnablePositionalTp;

    [PropertyDisplay("Return delay after positional TP (seconds, 0.05-1.0)")]
    public float PositionalTpReturnDelay = 0.15f;

    [PropertyDisplay("Cooldown between positional TPs (seconds, 0.1-5.0)")]
    public float PositionalTpCooldown = 1f;
```

`EnablePositionalTp` is left to its default `false` (opt-in). The two float defaults match the spec's "Configuration" section.

- [ ] **Step 1.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`. Config schema additions don't trigger any warnings.

- [ ] **Step 1.3: Commit**

```bash
git add BossMod/Multibox/MultiboxConfig.cs
git commit -m "feat(multibox): add positional TP config properties"
```

---

## Task 2: Create `MultiboxPositionalTp` Skeleton

**Files:**
- Create: `BossMod/Multibox/MultiboxPositionalTp.cs`

- [ ] **Step 2.1: Create the file with skeleton**

Create `BossMod/Multibox/MultiboxPositionalTp.cs` with this initial content:

```csharp
namespace BossMod;

// Per-frame handler that TPs a melee DPS alt to the boss's flank or rear for a positional GCD,
// then returns to main. Spec: docs/superpowers/specs/2026-05-14-multibox-positional-tp-design.md
//
// Requirement: this only works when the alt has a BMR class autorotation preset active, because
// Hints.RecommendedPositional is populated by class modules (Basexan / AkechiTools / GoToPositional).
// Without a preset, the hint stays default and this handler stays Idle silently.
sealed class MultiboxPositionalTp(BossModuleManager bossmod, WorldState ws, AIHints hints, ActionManagerEx amex, MultiboxConfig config)
{
    private enum State { Idle, AtPositional, Cooldown }

    private State _state = State.Idle;
    private Vector3 _homePos;
    private DateTime _atPositionalSince;
    private DateTime _cooldownUntil;
    private long _lastSeenFrameSequence;
    private DateTime _lastFrameSequenceChange;

    public void Reset()
    {
        _state = State.Idle;
        _homePos = default;
        _atPositionalSince = default;
        _cooldownUntil = default;
        _lastSeenFrameSequence = 0;
        _lastFrameSequenceChange = default;
    }

    // Called once per frame from Plugin's alt-update path.
    // 'state' is the latest received main state. 'clickTpHandled' is true if a click-TP pulse
    // was consumed this frame — we defer one frame in that case so user-explicit TPs win.
    public void Update(ref readonly MultiboxSyncState state, bool clickTpHandled)
    {
        // TODO: pre-checks + state machine — implemented in subsequent tasks.
    }
}
```

Notes:
- `sealed class` with primary constructor matches the repo's style (see `MultiboxPositionEditor` which also uses one).
- `internal` (default class accessibility) matches other Multibox files like `MultiboxConfig`.
- `Vector3` is `System.Numerics.Vector3`; the repo has global usings for this.

- [ ] **Step 2.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`. The file currently has unused parameters but is intentional; we'll consume them in later tasks.

- [ ] **Step 2.3: Commit**

```bash
git add BossMod/Multibox/MultiboxPositionalTp.cs
git commit -m "feat(multibox): add MultiboxPositionalTp skeleton"
```

---

## Task 3: Wire Handler Into `Plugin`

**Files:**
- Modify: `BossMod/Framework/Plugin.cs` (add field declaration, instantiate in `InitOnFrameworkThread`, call from update loop, reset on zone change)

- [ ] **Step 3.1: Add the field declaration**

In `BossMod/Framework/Plugin.cs`, locate the existing multibox field block (around lines 42–63). Add this new field on a new line directly **after** the `private MultiboxPositionEditor _mboxPosEditor = null!;` line (currently line 50):

```csharp
    private MultiboxPositionalTp _mboxPositionalTp = null!;
```

- [ ] **Step 3.2: Instantiate in `InitOnFrameworkThread`**

In the same file, locate `InitOnFrameworkThread` (starts around line 121). Find the block where `_mboxPosEditor = new(...)` is instantiated (around lines 154–165). Add the new instantiation directly **after** that block (after the closing `});` of the `_mboxPosEditor` constructor):

```csharp
        _mboxPositionalTp = new(_bossmod, _ws, _hints, _amex, _mboxConfig);
```

- [ ] **Step 3.3: Call from the alt-update path**

In the same file, locate `UpdateMultiboxSyncAlt` (starts around line 815). This is the method that processes the received sync state on alt clients. We want to call our handler just before the method returns.

First, find where the click-TP pulse is consumed. Look for the line that reads `_mboxPrevCommandFlags = state.CommandFlags;` (around line 902). We need to know whether a click-TP fired this frame, so capture that before the assignment. Replace the line:

```csharp
        _mboxPrevCommandFlags = state.CommandFlags;
```

with:

```csharp
        var clickTpHandledThisFrame = (state.CommandFlags & 0x20) != 0 && (_mboxPrevCommandFlags & 0x20) == 0;
        _mboxPrevCommandFlags = state.CommandFlags;
```

Then find the end of `UpdateMultiboxSyncAlt` (the closing brace of the method, around line 945). Just **before** the closing brace, add:

```csharp
        if (_mboxConfig.EnablePositionalTp)
            _mboxPositionalTp.Update(in state, clickTpHandledThisFrame);
        else
            _mboxPositionalTp.Reset();
```

Calling `Reset()` when the toggle is off ensures we don't strand the alt at the boss if the toggle was flipped mid-cycle — the next frame after disabling, state goes back to Idle. (The spec's "feature toggled off mid-cycle: complete the return TP" is handled naturally: the toggle takes effect next frame, so any in-flight return has already fired this frame.)

- [ ] **Step 3.4: Reset on zone change**

In the same file, locate the existing zone-change subscriptions. Search for `_ws.Actors.InCombatChanged.Subscribe(OnCombatChangedMbox);` (around line 153). On the next line, add:

```csharp
        _ws.CurrentZoneChanged.Subscribe(_ => _mboxPositionalTp?.Reset());
```

If `CurrentZoneChanged` doesn't exist or has a different name, grep for the canonical zone-change signal — repo searches: `grep -rn "CurrentZone" BossMod/WorldState.cs BossMod/Data/WorldState.cs` and adapt.

- [ ] **Step 3.5: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`. The handler is instantiated and ticked every frame but does nothing yet (its `Update` body is the `// TODO` from Task 2).

- [ ] **Step 3.6: Commit**

```bash
git add BossMod/Framework/Plugin.cs
git commit -m "feat(multibox): wire MultiboxPositionalTp into Plugin update loop"
```

---

## Task 4: Implement Universal Pre-Checks

**Files:**
- Modify: `BossMod/Multibox/MultiboxPositionalTp.cs` (fill in the early-return checks at the top of `Update`)

- [ ] **Step 4.1: Replace the `Update` body with pre-checks**

Open `BossMod/Multibox/MultiboxPositionalTp.cs`. Replace the entire `Update` method body (currently just the `// TODO` comment) with:

```csharp
    public void Update(ref readonly MultiboxSyncState state, bool clickTpHandled)
    {
        // Universal pre-checks — apply regardless of state.

        // 1. Defer one frame if user just fired a click-TP — don't collide with explicit TPs.
        if (clickTpHandled)
            return;

        // 2. Player must exist and be alive.
        var player = ws.Party.Player();
        if (player == null || player.IsDead)
        {
            Reset();
            return;
        }

        // 3. Track sync-state freshness. If FrameSequence stops advancing for >2s, home is stale.
        if (state.FrameSequence != _lastSeenFrameSequence)
        {
            _lastSeenFrameSequence = state.FrameSequence;
            _lastFrameSequenceChange = ws.CurrentTime;
        }
        var syncStale = _lastFrameSequenceChange != default
            && (ws.CurrentTime - _lastFrameSequenceChange).TotalSeconds > 2.0;
        if (syncStale)
        {
            Reset();
            return;
        }

        // State machine — implemented in subsequent tasks.
        switch (_state)
        {
            case State.Idle:
                UpdateIdle(player, in state);
                break;
            case State.AtPositional:
                UpdateAtPositional();
                break;
            case State.Cooldown:
                UpdateCooldown();
                break;
        }
    }

    private void UpdateIdle(Actor player, ref readonly MultiboxSyncState state)
    {
        // Implemented in Task 6.
    }

    private void UpdateAtPositional()
    {
        // Implemented in Task 7.
    }

    private void UpdateCooldown()
    {
        // Implemented in Task 8.
    }
```

- [ ] **Step 4.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`. Unused private methods generate no warning since they're called from the same class.

- [ ] **Step 4.3: Commit**

```bash
git add BossMod/Multibox/MultiboxPositionalTp.cs
git commit -m "feat(multibox): implement positional TP universal pre-checks"
```

---

## Task 5: Implement Target Computation

**Files:**
- Modify: `BossMod/Multibox/MultiboxPositionalTp.cs` (add `TryComputeTarget` helper)

- [ ] **Step 5.1: Add target computation helper**

In `BossMod/Multibox/MultiboxPositionalTp.cs`, add this method to the class (placement: just before the closing `}` of the class):

```csharp
    // Standoff = boss hitbox + this offset (yalms). Keeps the alt inside melee range (3y default).
    private const float StandoffOffset = 1.0f;

    // Computes the desired teleport target for a Flank/Rear positional. Returns false if the
    // target is unsafe (in ForbiddenZone / out of bounds / hits a temp obstacle) — caller skips.
    // Rear: single point directly behind boss.
    // Flank: pick the closer of left/right flank to the alt; no fallback to the other side.
    private bool TryComputeTarget(Actor player, Actor boss, Positional pos, out Vector3 target)
    {
        target = default;

        var bossPos = boss.Position;
        var bossFacing = boss.Rotation;
        var standoff = boss.HitboxRadius + StandoffOffset;

        WPos candidate;
        switch (pos)
        {
            case Positional.Rear:
                candidate = bossPos - bossFacing.ToDirection() * standoff;
                break;
            case Positional.Flank:
                var leftFlank = bossPos + (bossFacing + 90.Degrees()).ToDirection() * standoff;
                var rightFlank = bossPos + (bossFacing - 90.Degrees()).ToDirection() * standoff;
                var playerPos = player.Position;
                candidate = (leftFlank - playerPos).LengthSq() <= (rightFlank - playerPos).LengthSq()
                    ? leftFlank
                    : rightFlank;
                break;
            default:
                return false; // Any / Front are filtered upstream
        }

        // Safety check — same primitive as Hints.IsPositionSafe / IsDashSafe IPCs.
        if (ActionDefinitions.IsDashDangerous(player.Position, candidate, hints))
            return false;

        // Preserve player's current Y (height). Boss Y may differ on multi-level arenas;
        // alt should land on the floor the player is currently standing on.
        target = new Vector3(candidate.X, player.PosRot.Y, candidate.Z);
        return true;
    }
```

Notes on math (FFXIV convention — see `BossMod/Util/Angle.cs:4`): rotation 0 points south, +π/2 points east. `Angle.ToDirection()` returns the unit `WDir` for that facing. So `boss.Rotation.ToDirection()` points "where the boss is facing," and subtracting that vector goes behind. `+90°` is the boss's left flank; `-90°` is its right.

- [ ] **Step 5.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`. If you get an error about `Positional` being unresolved, add `using` at the top of the file or fully qualify — the enum lives in `BossMod` namespace per `BossMod/Data/ActionID.cs:32`.

- [ ] **Step 5.3: Commit**

```bash
git add BossMod/Multibox/MultiboxPositionalTp.cs
git commit -m "feat(multibox): add positional TP target computation"
```

---

## Task 6: Implement Idle State (Trigger + TP-Out)

**Files:**
- Modify: `BossMod/Multibox/MultiboxPositionalTp.cs` (fill in `UpdateIdle`)

- [ ] **Step 6.1: Replace `UpdateIdle` with the trigger logic**

In `BossMod/Multibox/MultiboxPositionalTp.cs`, replace the empty `UpdateIdle` method body with:

```csharp
    private void UpdateIdle(Actor player, ref readonly MultiboxSyncState state)
    {
        // 1. Read the positional hint. Must be Flank or Rear, imminent, and we must be wrong.
        var rec = hints.RecommendedPositional;
        if (rec.Pos != Positional.Flank && rec.Pos != Positional.Rear)
            return;
        if (!rec.Imminent || rec.Correct)
            return;

        // 2. True North bypasses positional requirements — skip TP.
        if (player.FindStatus(ClassShared.SID.TrueNorth) != null)
            return;

        // 3. Need a boss with a meaningful hitbox.
        var boss = bossmod.ActiveModule?.PrimaryActor;
        if (boss == null || boss.IsDead || boss.HitboxRadius <= 0f)
            return;

        // 4. Compute target; skip if unsafe.
        if (!TryComputeTarget(player, boss, rec.Pos, out var target))
            return;

        // 5. Transition Idle → AtPositional. Snapshot main's position as home.
        _homePos = new Vector3(state.MainX, state.MainY, state.MainZ);
        _atPositionalSince = ws.CurrentTime;
        _state = State.AtPositional;

        amex.TeleportTo(target);
        ClearStaleMovementHints();

        Service.Log($"[MultiboxSync] PositionalTP: out Pos={rec.Pos} dest=({target.X:F2},{target.Y:F2},{target.Z:F2}) home=({_homePos.X:F2},{_homePos.Y:F2},{_homePos.Z:F2})");
    }

    // After a teleport, the AI's per-frame goal/forced-movement/navi target were computed with
    // pre-TP positions. Clearing them prevents the alt from immediately walking one frame toward
    // a stale destination (visible 1–2y drift on short TPs). Same pattern as the existing
    // click-TP pulse handler in Plugin.cs (around line 889).
    private void ClearStaleMovementHints()
    {
        hints.GoalZones.Clear();
        hints.ForcedMovement = null;
        if (AI.AIManager.Instance != null)
            AI.AIManager.Instance.Controller.NaviTargetPos = null;
    }
```

- [ ] **Step 6.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`. If `ClassShared.SID.TrueNorth` doesn't resolve, confirm the type name — see `BossMod/ActionQueue/ClassShared.cs:127` (`TrueNorth = 1250`).

- [ ] **Step 6.3: Commit**

```bash
git add BossMod/Multibox/MultiboxPositionalTp.cs
git commit -m "feat(multibox): implement positional TP Idle state (trigger + TP-out)"
```

---

## Task 7: Implement AtPositional State (Return Delay + TP-Back)

**Files:**
- Modify: `BossMod/Multibox/MultiboxPositionalTp.cs` (fill in `UpdateAtPositional`)

- [ ] **Step 7.1: Replace `UpdateAtPositional` with the return logic**

In `BossMod/Multibox/MultiboxPositionalTp.cs`, replace the empty `UpdateAtPositional` method body with:

```csharp
    private void UpdateAtPositional()
    {
        var returnDelay = Math.Clamp(config.PositionalTpReturnDelay, 0.05f, 1.0f);
        if ((ws.CurrentTime - _atPositionalSince).TotalSeconds < returnDelay)
            return;

        // Delay elapsed — TP back to main and enter Cooldown.
        amex.TeleportTo(_homePos);
        ClearStaleMovementHints();

        var cooldown = Math.Clamp(config.PositionalTpCooldown, 0.1f, 5.0f);
        _cooldownUntil = ws.CurrentTime.AddSeconds(cooldown);
        _state = State.Cooldown;

        Service.Log($"[MultiboxSync] PositionalTP: back dest=({_homePos.X:F2},{_homePos.Y:F2},{_homePos.Z:F2})");
    }
```

The `Math.Clamp` bounds match the property hints in the config display labels and prevent absurd values (e.g., a 0s delay that re-TPs the same frame, or a 60s cooldown that strands the feature).

- [ ] **Step 7.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`.

- [ ] **Step 7.3: Commit**

```bash
git add BossMod/Multibox/MultiboxPositionalTp.cs
git commit -m "feat(multibox): implement positional TP AtPositional state (return)"
```

---

## Task 8: Implement Cooldown State

**Files:**
- Modify: `BossMod/Multibox/MultiboxPositionalTp.cs` (fill in `UpdateCooldown`)

- [ ] **Step 8.1: Replace `UpdateCooldown` with the unlock logic**

In `BossMod/Multibox/MultiboxPositionalTp.cs`, replace the empty `UpdateCooldown` method body with:

```csharp
    private void UpdateCooldown()
    {
        if (ws.CurrentTime >= _cooldownUntil)
            _state = State.Idle;
        // No TP fires here — the return TP already happened at AtPositional → Cooldown.
        // Alt is at main throughout this state; we just gate the next trigger.
    }
```

The comment is load-bearing — this is the exact behavior question that came up during brainstorming. Future readers will see this and understand the cooldown is purely a debounce.

- [ ] **Step 8.2: Build**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded`.

- [ ] **Step 8.3: Commit**

```bash
git add BossMod/Multibox/MultiboxPositionalTp.cs
git commit -m "feat(multibox): implement positional TP Cooldown unlock"
```

---

## Task 9: Verify Build, Run Manual Test Pass

**Files:**
- No code changes. This is the validation task.

- [ ] **Step 9.1: Full build verification**

```bash
dotnet build BossModReborn.sln -c Debug
```

Expected: `Build succeeded` with 0 errors. Note the warning count; it should match the warning count before any of this work (no new warnings added).

- [ ] **Step 9.2: Smoke test — feature disabled (default)**

Install the freshly-built plugin via Dalamud's dev plugin path (or however the user's normal dev install works). With the multibox config left at defaults (`EnablePositionalTp == false`):

- Engage any boss with a melee DPS character (DRG/MNK/NIN/SAM/RPR/VPR).
- Confirm via `/xllog` that **no** `[MultiboxSync] PositionalTP:` lines appear.

Expected: zero TPs, zero log entries from the new feature.

- [ ] **Step 9.3: Manual test pass (feature enabled)**

Enable `EnablePositionalTp = true` on at least one alt. Walk through the 10-row table from the spec's "Testing" section:

| # | Setup | Action | Expected |
|---|---|---|---|
| 1 | Alt = DRG + BMR DRG preset, training dummy | Engage dummy | No TPs (omnidirectional). |
| 2 | Alt = DRG, real boss | Engage | TPs to rear on Disembowel, flank on Full Thrust, returns to main. Log shows `out` + `back` pairs. |
| 3 | Alt = MNK with True North up | Engage | No TPs while True North active; resumes after fall-off. |
| 4 | Alt = NIN, AoE telegraphed at rear | Cast during AoE | Skips TP that GCD; alt stays with main. |
| 5 | Alt = SAM, feature OFF | Engage | Zero TPs. |
| 6 | Alt = SAM, toggle OFF mid-cycle | Toggle | Return TP completes (or `Reset()` clears), then stops. |
| 7 | Alt = RPR, main sprinting | Engage during kite | Alt returns to home position from TP-out time; AI walks the small remaining gap. |
| 8 | Alt = VPR, ctrl+click TP same frame as positional | Trigger both | Click-TP wins; handler defers (no `out` log line that frame). |
| 9 | Alt = DRG without BMR preset | Engage | Feature dormant; no log lines. |
| 10 | Alt dies mid-cycle | Wipe | No TP on corpse; handler resets cleanly. |

Open `/xllog` and filter on `PositionalTP` to follow state transitions. Each TP-out should be followed within ~0.15s by a TP-back.

- [ ] **Step 9.4: If any case fails**

Identify the case, log the symptom, then either:
- Fix in the relevant file (most likely `MultiboxPositionalTp.cs`), build, test, commit with `fix(multibox): ...`.
- Or, if the failure reveals a design gap (not a code bug), stop and update the spec first.

Do not declare the feature done if any of cases 1–10 fail.

- [ ] **Step 9.5: Final summary commit (if any fixes were needed)**

If Step 9.4 produced fixes, commit them. If everything passed first try, no commit is needed for this task.

---

## Summary of Files Touched

By end of plan:

```
docs/superpowers/specs/2026-05-14-multibox-positional-tp-design.md  (already exists)
docs/superpowers/plans/2026-05-14-multibox-positional-tp.md         (this file)
BossMod/Multibox/MultiboxConfig.cs                                  (modified)
BossMod/Multibox/MultiboxPositionalTp.cs                            (created)
BossMod/Framework/Plugin.cs                                         (modified)
```

Per-task commit log expectation:

```
feat(multibox): add positional TP config properties
feat(multibox): add MultiboxPositionalTp skeleton
feat(multibox): wire MultiboxPositionalTp into Plugin update loop
feat(multibox): implement positional TP universal pre-checks
feat(multibox): add positional TP target computation
feat(multibox): implement positional TP Idle state (trigger + TP-out)
feat(multibox): implement positional TP AtPositional state (return)
feat(multibox): implement positional TP Cooldown unlock
(optional fix commits from Task 9 if manual tests reveal issues)
```

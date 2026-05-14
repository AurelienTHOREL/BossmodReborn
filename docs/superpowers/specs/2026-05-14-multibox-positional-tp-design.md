# Multibox Positional Teleport for Melee DPS Alts

**Status:** Design approved, ready for implementation planning.
**Date:** 2026-05-14
**Branch:** `feat/multibox-sync`

## Goal

In a multibox setup, melee DPS alts stack on top of the main and end up in front of the boss, missing rear/flank positional bonuses on their GCDs. This feature lets a melee DPS alt briefly teleport to the correct positional, snapshot the action, and return to main — without ever sitting in a danger zone.

## Non-Goals

- No support for ranged/caster/tank/healer alts (no positional skills).
- No changes to RotationSolverReborn (RSR). The feature lives entirely in BossModReborn (BMR).
- No new IPC surface.
- No coordination from the main client. Each alt decides independently.
- No attempt to handle bosses where rear/flank are permanently unsafe — skip the TP cycle for that GCD.

## Architecture

All logic runs on the **alt** client. The main client is unchanged.

A single new class, `MultiboxPositionalTp`, is owned by `Plugin` and ticked once per frame from the existing `Plugin.Update` multibox loop (when role is `Alt` and the feature toggle is on).

The handler is purely reactive — it observes existing state and never produces messages on the wire.

### Detection signal

BMR already exposes `AIHints.RecommendedPositional`, a tuple:

```csharp
public (Actor? Target, Positional Pos, bool Imminent, bool Correct) RecommendedPositional;
```

Populated by BMR's class autorotation modules (`Basexan`, `AkechiTools`, `GoToPositional`). This already tells us:

- **What** positional is needed (`Pos ∈ {Any, Flank, Rear, Front}`).
- **When** to act (`Imminent == true` means the positional GCD is next).
- **Whether** we're already in the right place (`Correct`).

**Requirement (documented, not enforced in code):** the alt must have a BMR autorotation preset active for its class for this hint to be populated. Strategies can be left on `Automatic` so the preset doesn't compete with RSR — only the side-effect of populating hints is needed. Without a preset, the feature is silently dormant.

### True North handling

True North (status ID 1250) grants positional bonuses from any angle. The handler checks `player.FindStatus(ClassShared.SID.TrueNorth)` each frame and skips the trigger when active. No IPC needed.

### State machine

Three states per alt:

```
   ┌──────┐  trigger conditions met   ┌───────────────┐
   │ Idle │ ───────────────────────►  │ AtPositional  │
   └──┬───┘     TP-out fires           └───────┬───────┘
      ▲                                        │ return delay elapsed
      │                                        │ TP-back fires
      │                                        ▼
      │       cooldown expires           ┌──────────┐
      └────────────────────────────────  │ Cooldown │
                                         └──────────┘
```

State data:
- `_state`: `Idle | AtPositional | Cooldown`
- `_homePos`: `Vector3` — main's position snapshotted at TP-out
- `_atPositionalSince`: `DateTime` — TP-out timestamp
- `_cooldownUntil`: `DateTime` — earliest time the next cycle may begin
- `_lastSeenFrameSequence`: `long` — for sync-stale detection

## Per-Frame Flow

Called from `Plugin.Update` after existing multibox sync handling, only when `_mboxConfig.Role == MultiboxRole.Alt && _mboxConfig.EnablePositionalTp`.

### Universal pre-checks (any state)
1. Multibox role still `Alt` and feature toggle on. If not → reset to `Idle`, return.
2. A click-TP pulse is being handled this frame (`CommandFlags & 0x20` just consumed). If yes → defer one frame.
3. Player exists (`_ws.Party.Player() != null`) and `!Actor.IsDead`. If not → reset to `Idle`, return.

### State = Idle
1. Read `Hints.RecommendedPositional`. Require `Pos ∈ {Flank, Rear} && Imminent && !Correct`.
2. Reject if `player.FindStatus(ClassShared.SID.TrueNorth) != null`.
3. Reject if `bossmod.ActiveModule?.PrimaryActor == null`.
4. Reject if sync state is stale: `state.FrameSequence == _lastSeenFrameSequence` for more than 2.0 s (track first-seen-stale wall time, treat home as invalid past the threshold).
5. Compute target world position (see Target Computation).
6. `ActionDefinitions.IsDashDangerous(player.Position, target, hints)` must be `false`. Otherwise skip (no fallback to other positional, per design).
7. **Transition Idle → AtPositional:**
   - `_homePos = new Vector3(state.MainX, state.MainY, state.MainZ)`
   - `_atPositionalSince = ws.CurrentTime`
   - `_amex.TeleportTo(target)`
   - Clear stale per-frame hints (mirrors the existing TP-pulse handler at `Plugin.cs:889-892`):
     ```csharp
     _hints.GoalZones.Clear();
     _hints.ForcedMovement = null;
     if (AI.AIManager.Instance != null)
         AI.AIManager.Instance.Controller.NaviTargetPos = null;
     ```
   - Log `[MultiboxSync] PositionalTP: out Pos={pos} dest={target} home={home}`.

### State = AtPositional
1. If `ws.CurrentTime - _atPositionalSince >= ReturnDelay`:
   - `_amex.TeleportTo(_homePos)`
   - Clear the same per-frame hints.
   - `_cooldownUntil = ws.CurrentTime + Cooldown`
   - **Transition AtPositional → Cooldown.**
   - Log `[MultiboxSync] PositionalTP: back dest={home}`.
2. Otherwise stay.

### State = Cooldown
1. If `ws.CurrentTime >= _cooldownUntil` → transition to `Idle`.
2. Otherwise stay.

## Target Computation

Given:
- Boss `o = primaryActor.Position`, `θ = primaryActor.Rotation`, `r = primaryActor.HitboxRadius`.
- Standoff distance `d = r + 1.0f` (close enough to be inside melee range, comfortably).

For `Pos == Rear`:
```
target = o + (-θ.ToDirection()) * d
```

For `Pos == Flank`:
- Compute `leftFlank = o + (θ + 90°).ToDirection() * d`
- Compute `rightFlank = o + (θ - 90°).ToDirection() * d`
- Pick whichever has the shorter distance from `player.Position`. **No left↔right fallback** — per the design's "skip if unsafe" rule, we don't try the other flank if our chosen one is unsafe.
- If the chosen flank fails `IsDashDangerous` → skip (stay Idle).

`Pos == Any` and `Pos == Front` never reach target computation — filtered out at step 1 of the Idle handler.

## Configuration

Added to `MultiboxConfig.cs`:

```csharp
[PropertyDisplay("Auto-TP melee DPS alt to positional (requires BMR class preset on alt)")]
public bool EnablePositionalTp = false;

[PropertyDisplay("Return delay after positional TP (seconds)")]
public float PositionalTpReturnDelay = 0.15f;

[PropertyDisplay("Cooldown between positional TPs (seconds)")]
public float PositionalTpCooldown = 1.0f;
```

Defaults are conservative; users tune up the return delay if their latency is high enough that 150 ms misses the snapshot, or tune down the cooldown if the boss has back-to-back positionals.

## Edge Cases

| Case | Behavior |
|---|---|
| No `ActiveModule` (out of combat / town) | Idle. `RecommendedPositional` is `default`, step 1 fails. |
| `PrimaryActor == null` | Idle. |
| Boss is omnidirectional | Idle. Autorotation sets `Positional.Any`, step 1 fails. |
| Alt dead (`Actor.IsDead`) | Idle. Skip universally. |
| Alt rooted / stunned / heavy / bound | No explicit check. During hard CC the player can't fire skills, so `Imminent` never goes true and the trigger naturally doesn't fire. If a TP cycle is mid-flight when CC lands, the return TP still completes (cached `_homePos`). |
| Sync state stale (main not pulsing) | Idle. `FrameSequence` stagnation > 2 s. |
| Click-TP pulse same frame | Defer one frame. User-explicit TP wins. |
| Multiple positional GCDs back-to-back | Each fires after cooldown. Tune down `PositionalTpCooldown` if needed. |
| True North applied mid-cycle | Cycle completes normally; subsequent triggers gated by True North check. |
| Boss dies during cycle | Return TP still fires (uses cached `_homePos`). Resets to `Idle`. |
| Zone change / wipe mid-cycle | Reset to `Idle` (subscribe to `WorldState.CurrentZoneClear`). |
| Feature toggled off mid-cycle | Complete the return TP, then stop. Never strand the alt at the boss. |

## Integration Points

**New files:**
- `BossMod/Multibox/MultiboxPositionalTp.cs`

**Modified files:**
- `BossMod/Multibox/MultiboxConfig.cs` — three new properties.
- `BossMod/Framework/Plugin.cs` — field, construction, per-frame call. No changes to existing multibox sync code.

**Reused primitives:**
- `ActionManagerEx.TeleportTo(Vector3)` — same primitive as the existing click-TP pulse.
- `ActionDefinitions.IsDashDangerous(WPos, WPos, AIHints)` — same safety check as `Hints.IsPositionSafe` IPC.
- `Actor.FindStatus(uint)` for True North check.
- `ClassShared.SID.TrueNorth` for the status ID.

## Testing

No unit-test infrastructure exists in BMR. Manual test plan, run on a real multibox setup with at least one main + one melee DPS alt:

| # | Setup | Action | Expected |
|---|---|---|---|
| 1 | Alt = DRG + BMR preset, training dummy | Engage dummy | No TPs (omnidirectional). |
| 2 | Alt = DRG, real boss | Engage | TPs rear on Disembowel, flank on Full Thrust, returns to main. |
| 3 | Alt = MNK with True North up | Engage | No TPs while True North active; resumes after fall-off. |
| 4 | Alt = NIN, AoE telegraphed at rear | Cast during AoE | Skips TP that GCD; alt stays with main. |
| 5 | Alt = SAM, feature OFF | Engage | Zero TPs. |
| 6 | Alt = SAM, toggle OFF mid-cycle | Toggle | Return TP completes, then stops. |
| 7 | Alt = RPR, main sprinting | Engage during kite | Alt returns to home position from TP-out time; AI walks remaining gap. |
| 8 | Alt = VPR, ctrl+click TP same frame as positional | Trigger both | Click-TP wins; handler defers. |
| 9 | Alt = DRG without BMR preset | Engage | Feature dormant; no errors. |
| 10 | Alt dies mid-cycle | Wipe | No TP on corpse; handler resets. |

Logging-driven verification: each state transition logs `[MultiboxSync] PositionalTP: <event>` to Dalamud log (`/xllog`). Same pattern as existing multibox sync.

## Risks & Open Questions

- **Snapshot timing on high-latency clients.** 150 ms may not be enough on bad connections; user-tunable via `PositionalTpReturnDelay`. Worst case: alt visually TPs but the server cancels the cast because the player wasn't there long enough. No way to test pre-merge; reliance on field tuning.
- **`TeleportTo` mechanic risk.** This primitive is already used by the existing TP-pulse and works in practice; reusing it adds no new failure surface. If Dalamud ever rate-limits or sandboxes it, both features break together.
- **BMR preset requirement is non-obvious.** The config label flags it ("requires BMR class preset on alt"), but if a user enables the toggle without a preset, the feature is silently dormant. Considered acceptable: no exceptions, no error, just no triggers. Documented behavior.

## Decisions Locked In

From brainstorming session 2026-05-14:

1. **Trigger origin:** BMR-only auto-detection. No RSR IPC.
2. **Detection signal:** `Hints.RecommendedPositional` tuple (`Pos != Any && !Correct && Imminent`).
3. **TP-back timing:** Fixed delay 0.1–0.2 s after TP-out (default 0.15 s).
4. **Safety fallback:** Strict skip. For Flank, deterministically pick the closer of left/right to the alt's current position; if that one is unsafe, skip. No retry on the other flank, no rear-as-fallback-for-flank, no arc scanning.
5. **True North:** Check via `FindStatus(ClassShared.SID.TrueNorth)`. Skip when active.
6. **Hints availability:** Require BMR class preset on alt. Document; don't enforce.

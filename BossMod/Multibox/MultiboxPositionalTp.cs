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

    private void UpdateCooldown()
    {
        if (ws.CurrentTime >= _cooldownUntil)
            _state = State.Idle;
        // No TP fires here — the return TP already happened at AtPositional → Cooldown.
        // Alt is at main throughout this state; we just gate the next trigger.
    }

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
}

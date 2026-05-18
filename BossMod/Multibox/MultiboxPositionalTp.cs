namespace BossMod;

// Per-frame handler that TPs a melee DPS alt to the boss's flank or rear for a positional GCD,
// then returns to main. Spec: docs/superpowers/specs/2026-05-14-multibox-positional-tp-design.md
//
// Requirement: this only works when the alt has a BMR class autorotation preset active, because
// Hints.RecommendedPositional is populated by class modules (Basexan / AkechiTools / GoToPositional).
// Without a preset, the hint stays default and this handler stays Idle silently.
sealed class MultiboxPositionalTp(WorldState ws, AIHints hints, ActionManagerEx amex, MultiboxConfig config)
{
    private enum State { Idle, AtPositional, Cooldown }

    private State _state = State.Idle;
    private Vector3 _homePos;
    private DateTime _atPositionalSince;
    private DateTime _cooldownUntil;
    private long _lastSeenFrameSequence;
    private DateTime _lastFrameSequenceChange;
    private DateTime _lastGateLog; // DIAGNOSTIC: throttle gate-rejection logs to once/sec

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
                UpdateAtPositional(player);
                break;
            case State.Cooldown:
                UpdateCooldown();
                break;
        }
    }

    private void UpdateIdle(Actor player, ref readonly MultiboxSyncState state)
    {
        // 0. Feature toggle: if disabled, don't trigger new cycles. In-flight AtPositional /
        //    Cooldown states still progress so the alt isn't stranded at the boss.
        if (!config.EnablePositionalTp)
            return;

        // 1. Read the positional hint. Must be Flank or Rear, imminent, and we must be wrong.
        var rec = hints.RecommendedPositional;
        if (rec.Pos != Positional.Flank && rec.Pos != Positional.Rear)
        {
            LogGateReject($"Pos={rec.Pos} (need Flank/Rear)");
            return;
        }
        if (!rec.Imminent)
        {
            LogGateReject($"not imminent (Pos={rec.Pos}, Correct={rec.Correct})");
            return;
        }
        if (rec.Correct)
        {
            LogGateReject($"already correct (Pos={rec.Pos})");
            return;
        }

        // 2. True North bypasses positional requirements — skip TP.
        if (player.FindStatus(ClassShared.SID.TrueNorth) != null)
        {
            LogGateReject("True North active");
            return;
        }

        // 3. Need a targeted enemy with a meaningful hitbox. Use the autorotation's target
        //    (rec.Target) so this works against any enemy with positional bonuses — dummies,
        //    dungeon trash, raid bosses — not only encounters with a BMR boss module.
        var enemy = rec.Target;
        if (enemy == null || enemy.IsDead || enemy.HitboxRadius <= 0f)
        {
            LogGateReject($"no target (rec.Target={enemy?.Name ?? "null"}, hitbox={enemy?.HitboxRadius:F1})");
            return;
        }

        // 4. GCD must be about to fire. Strategy: while we're inside this short window, TP every
        //    frame to the positional (handled in UpdateAtPositional). This ensures the snapshot
        //    lands at the correct angle regardless of client-side movement drift.
        var gcdRemaining = amex.GCD();
        if (gcdRemaining > 0.25f)
        {
            LogGateReject($"GCD too far ({gcdRemaining:F2}s remaining, need ≤0.25s)");
            return;
        }

        // 5. Compute teleport destination; skip if unsafe.
        if (!TryComputeTarget(player, enemy, rec.Pos, out var target))
        {
            LogGateReject($"target unsafe for Pos={rec.Pos} (in ForbiddenZone or out of bounds)");
            return;
        }

        // 6. Transition Idle → AtPositional. Snapshot main's position as home, record entry time.
        _homePos = new Vector3(state.MainX, state.MainY, state.MainZ);
        _atPositionalSince = ws.CurrentTime;
        _state = State.AtPositional;

        amex.TeleportTo(target);
        ClearStaleMovementHints();

        Service.Log($"[MultiboxSync] PositionalTP: enter holding Pos={rec.Pos} GCD={gcdRemaining:F2}s dest=({target.X:F2},{target.Y:F2},{target.Z:F2}) home=({_homePos.X:F2},{_homePos.Y:F2},{_homePos.Z:F2})");
    }

    // DIAGNOSTIC: log why the Idle trigger rejected this frame, throttled to 1Hz.
    // Remove before merge.
    private void LogGateReject(string reason)
    {
        var now = ws.CurrentTime;
        if ((now - _lastGateLog).TotalSeconds < 1.0)
            return;
        _lastGateLog = now;
        Service.Log($"[MultiboxSync] PositionalTP: gate reject — {reason}");
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

    private void UpdateAtPositional(Actor player)
    {
        // Hold-and-TP-every-frame strategy: while we're in this state, the alt is locked at the
        // boss's positional spot. We exit when (a) the GCD has fired (timer reset to a high value),
        // (b) the autorotation no longer has an imminent positional queued, or (c) we hit the
        // safety timeout. PositionalTpReturnDelay doubles as the safety-timeout knob.
        var heldFor = (ws.CurrentTime - _atPositionalSince).TotalSeconds;
        var gcdNow = amex.GCD();
        var rec = hints.RecommendedPositional;

        var gcdFired = gcdNow > 0.5f;                // GCD timer reset → positional snapshot landed
        var imminentCleared = !rec.Imminent;         // autorotation moved past the positional
        var safetyTimeout = heldFor > Math.Clamp(config.PositionalTpReturnDelay, 0.05f, 1.0f);
        var targetLost = rec.Target == null || rec.Target.IsDead || rec.Target.HitboxRadius <= 0f;

        if (gcdFired || imminentCleared || safetyTimeout || targetLost)
        {
            amex.TeleportTo(_homePos);
            ClearStaleMovementHints();

            var cooldown = Math.Clamp(config.PositionalTpCooldown, 0.1f, 5.0f);
            _cooldownUntil = ws.CurrentTime.AddSeconds(cooldown);
            _state = State.Cooldown;

            var reason = gcdFired ? "GCD fired" : imminentCleared ? "imminent cleared" : safetyTimeout ? "timeout" : "target lost";
            Service.Log($"[MultiboxSync] PositionalTP: return ({reason}) heldFor={heldFor:F2}s gcd={gcdNow:F2}s dest=({_homePos.X:F2},{_homePos.Y:F2},{_homePos.Z:F2})");
            return;
        }

        // Still holding — re-TP every frame to keep the alt pinned at the positional. The enemy
        // may have rotated since entry, so recompute. If the spot has become unsafe this frame,
        // stop re-teleporting but stay in this state so we exit cleanly via the conditions above.
        if (TryComputeTarget(player, rec.Target!, rec.Pos, out var target))
        {
            amex.TeleportTo(target);
            ClearStaleMovementHints();
        }
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
    private bool TryComputeTarget(Actor player, Actor enemy, Positional pos, out Vector3 target)
    {
        target = default;

        var bossPos = enemy.Position;
        var bossFacing = enemy.Rotation;
        var standoff = enemy.HitboxRadius + StandoffOffset;

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

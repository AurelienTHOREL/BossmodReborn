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
}

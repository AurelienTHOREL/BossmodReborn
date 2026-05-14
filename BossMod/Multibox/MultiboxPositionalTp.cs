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

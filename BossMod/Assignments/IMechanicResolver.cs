namespace BossMod.Assignments;

// The reactive result of resolving a mechanic for one specific player.
// Every field is optional: a resolver only expresses opinions it actually has.
public readonly struct PlayerPlan
{
    // where this player should stand (null = no positioning opinion this frame)
    public readonly WPos? TargetPos;
    // acceptance radius around TargetPos (used as the "you must be inside" circle for hard plans)
    public readonly float Radius;
    // when the position must be reached; default = immediately. For hard plans this lets the
    // AI keep uptime until it actually has to move (forbidden zones are time-aware).
    public readonly DateTime Activation;
    // direction this player should face (e.g. gaze) (null = no facing opinion)
    public readonly Angle? Facing;
    // true  = must reach the spot even at the cost of uptime; implemented as an inverted
    //         forbidden zone (everywhere outside the acceptance circle is forbidden), which
    //         composes with AOE forbidden zones so the AI can never path through danger to get here.
    // false = soft preference, implemented as an attraction gradient that blends with uptime.
    public readonly bool Hard;
    // optional explicit soft-attractor weight; when null the bridge picks a default
    public readonly float? Weight;

    public PlayerPlan(WPos? targetPos = null, float radius = 2f, DateTime activation = default,
        Angle? facing = null, bool hard = false, float? weight = null)
    {
        TargetPos = targetPos;
        Radius = radius;
        Activation = activation;
        Facing = facing;
        Hard = hard;
        Weight = weight;
    }

    public static readonly PlayerPlan None = new();
    public bool HasOpinion => TargetPos != null || Facing != null;
}

// A resolver computes, every frame, what a single player should do for one mechanic.
// Implementations MUST be pure functions of (live world state, assignment, strat):
// this is what guarantees every client computes identical plans for the whole party,
// so players don't collide and no client-to-client coordination is required.
public interface IMechanicResolver
{
    // whether the mechanic is currently active (gated by the implementation from
    // OnCastStarted / OnTethered / OnStatusGain / etc.)
    bool Active { get; }

    // resolve this player's plan; called every frame while Active
    PlayerPlan Resolve(int slot, Actor actor, PartyRolesConfig.Assignment assignment, StrategyPreset strat);
}

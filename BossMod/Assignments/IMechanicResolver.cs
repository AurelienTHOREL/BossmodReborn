namespace BossMod.Assignments;

// The reactive result of resolving a mechanic for one specific player.
// Every field is optional: a resolver only expresses opinions it actually has.
public readonly struct PlayerPlan
{
    // where this player should stand (null = no positioning opinion this frame)
    public readonly WPos? TargetPos;
    // direction this player should face (e.g. gaze) (null = no facing opinion)
    public readonly Angle? Facing;
    // if true, this position must be reached even at the cost of uptime (strong attractor);
    // if false, it blends with normal uptime goal zones
    public readonly bool Hard;
    // optional explicit attractor weight; when null the bridge picks a default based on Hard
    public readonly float? Weight;

    public PlayerPlan(WPos? targetPos = null, Angle? facing = null, bool hard = false, float? weight = null)
    {
        TargetPos = targetPos;
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

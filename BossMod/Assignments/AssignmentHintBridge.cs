namespace BossMod.Assignments;

// The missing link: turns a resolved per-player PlayerPlan into AIHints so the
// existing AI engine actually moves the character to the assigned spot.
//
// This only ADDS attraction (goal zones) and orientation restrictions. It never
// removes or weakens the forbidden zones produced by AOE components, so a bad
// assignment can at worst cost uptime/positioning - it can never path the player
// into a danger zone (NavigationDecision.Build rasterizes forbidden zones before
// goals, and the pathfinder refuses to cross them).
public static class AssignmentHintBridge
{
    // default attractor weights. "Hard" must beat normal rotation uptime goal
    // zones (which return ~1-4); "soft" only nudges and blends with uptime.
    public const float DefaultHardWeight = 20f;
    public const float DefaultSoftWeight = 2f;

    // how far away the linear attraction gradient reaches (arena-spanning)
    public const float AttractionRange = 50f;

    public static void Apply(in PlayerPlan plan, AIHints hints)
    {
        if (plan.TargetPos is { } pos)
        {
            var weight = plan.Weight ?? (plan.Hard ? DefaultHardWeight : DefaultSoftWeight);
            // distance gradient pulls the player toward the spot from anywhere on the arena...
            hints.GoalZones.Add(AIHints.GoalProximity(pos, AttractionRange, weight));
            // ...plus a sharp bonus for actually being on the spot, so the pathfinder settles there.
            hints.GoalZones.Add(AIHints.GoalSingleTarget(pos, 1f, weight));
        }

        if (plan.Facing is { } face)
        {
            // force the player to face 'face' by forbidding the opposite arc, leaving a
            // thin allowed window around the desired direction. activation == default
            // means "already active", which is what we want for a per-frame facing opinion.
            var opposite = (face.Rad + MathF.PI).Radians();
            hints.ForbiddenDirections.Add((opposite, 170f.Degrees(), default));
        }
    }
}

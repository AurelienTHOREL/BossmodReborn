namespace BossMod.Assignments;

// The missing link: turns a resolved per-player PlayerPlan into AIHints so the
// existing AI engine actually moves the character to the assigned spot.
//
// Two strategies, both safety-preserving:
//   * Hard - emit an INVERTED forbidden zone (everywhere outside an acceptance circle
//     is forbidden). This is the same idiom GenericTowers.AddAIHints uses to make the
//     AI soak a tower: it composes with the AOE forbidden zones via the pathfinder, so
//     the AI settles on a point that is simultaneously on-assignment AND safe, and can
//     never cross a danger zone to get there.
//   * Soft - emit an attraction gradient (goal zone) that merely biases positioning and
//     blends with normal rotation/uptime goals.
//
// In neither case do we touch the AOE forbidden zones, so a bad assignment can at worst
// cost uptime/positioning - it can never path the player into danger.
public static class AssignmentHintBridge
{
    // default soft-attractor weight; small so it nudges without fighting uptime goals (~1-4).
    public const float DefaultSoftWeight = 2f;

    // how far away the linear attraction gradient reaches (arena-spanning)
    public const float AttractionRange = 50f;

    public static void Apply(in PlayerPlan plan, AIHints hints)
    {
        if (plan.TargetPos is { } pos)
        {
            if (plan.Hard)
            {
                // "you must stand here": forbid everything outside the acceptance circle.
                hints.AddForbiddenZone(new SDInvertedCircle(pos, plan.Radius), plan.Activation);
            }
            else
            {
                var weight = plan.Weight ?? DefaultSoftWeight;
                hints.GoalZones.Add(AIHints.GoalProximity(pos, AttractionRange, weight));
            }
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

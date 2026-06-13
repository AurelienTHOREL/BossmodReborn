namespace BossMod.Assignments;

// Compile-ready reference resolver: assigns each of the 8 standard roles a fixed
// clock spot on a circle around a center point. It exists to exercise the
// assignment->AIHints bridge end-to-end without first rewriting a live fight.
//
// Usage from a module component:
//   var resolver = new FixedRoleSpotsResolver(() => Arena.Center, radius: 18f);
//   // ... in OnCastStarted: resolver.Active = true; (and false when resolved)
//   // host component extends AssignedMechanic(module, resolver)
//
// The "spread" mechanic key flips the layout 45 degrees when the chosen strat is
// "rotated", demonstrating how a StrategyPreset choice changes every player's spot
// deterministically.
public sealed class FixedRoleSpotsResolver(Func<WPos> center, float radius) : IMechanicResolver
{
    public bool Active { get; set; }

    public const string MechanicKey = "fixed_spots";

    // role -> clock angle in degrees (0 = North, increasing clockwise)
    private static readonly (PartyRolesConfig.Assignment role, float deg)[] _layout =
    [
        (PartyRolesConfig.Assignment.MT, 0f),    // N
        (PartyRolesConfig.Assignment.M1, 45f),   // NE
        (PartyRolesConfig.Assignment.R1, 90f),   // E
        (PartyRolesConfig.Assignment.H1, 135f),  // SE
        (PartyRolesConfig.Assignment.OT, 180f),  // S
        (PartyRolesConfig.Assignment.M2, 225f),  // SW
        (PartyRolesConfig.Assignment.R2, 270f),  // W
        (PartyRolesConfig.Assignment.H2, 315f),  // NW
    ];

    public PlayerPlan Resolve(int slot, Actor actor, PartyRolesConfig.Assignment assignment, StrategyPreset strat)
    {
        var deg = AngleFor(assignment);
        if (deg is not { } baseDeg)
            return PlayerPlan.None; // unassigned -> no opinion, falls back to forbidden-zone avoidance

        // strat choice deterministically shifts the whole layout, so all clients agree
        var offset = strat.Choice(MechanicKey, "standard") == "rotated" ? 45f : 0f;
        var dir = (baseDeg + offset).Degrees().ToDirection();
        var pos = center() + radius * dir;
        return new PlayerPlan(targetPos: pos, hard: true);
    }

    private static float? AngleFor(PartyRolesConfig.Assignment assignment)
    {
        foreach (var (role, deg) in _layout)
            if (role == assignment)
                return deg;
        return null;
    }
}

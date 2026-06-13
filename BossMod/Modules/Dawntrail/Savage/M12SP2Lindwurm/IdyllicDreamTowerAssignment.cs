using BossMod.Assignments;

namespace BossMod.Dawntrail.Savage.M12S2Lindwurm;

// [Experimental] Bridges the Idyllic Dream tower soak into the AI as a *specific* per-player
// tower assignment, layered on top of GenericTowers' built-in "soak any valid tower" safety.
// GenericTowers already drives the AI into some non-forbidden tower; this picks exactly which
// one, deterministically across all clients (shared party-role assignment + shared strat), so
// players in the same light-vuln group don't pile into the same tower.
//
// It is purely additive and config-gated (off by default): when disabled the resolver reports
// inactive and the fight behaves exactly as before. This is the first live wiring of the
// BossMod.Assignments layer and still needs a build pass + in-game tuning before it could be
// enabled by default.
sealed class IdyllicDreamTowerAssignment(BossModule module)
    : AssignedMechanic(module, new DelegateResolver(() => IsActive(module), (slot, _, _, strat) => Resolve(module, slot, strat)))
{
    public const string MechanicKey = "idyllic_towers"; // strat option: "standard" | "inverse"

    // canonical role order used to pair eligible players with ordered towers
    private static readonly PartyRolesConfig.Assignment[] _order =
    [
        PartyRolesConfig.Assignment.MT, PartyRolesConfig.Assignment.OT,
        PartyRolesConfig.Assignment.H1, PartyRolesConfig.Assignment.H2,
        PartyRolesConfig.Assignment.M1, PartyRolesConfig.Assignment.M2,
        PartyRolesConfig.Assignment.R1, PartyRolesConfig.Assignment.R2,
    ];

    private static bool IsActive(BossModule module)
    {
        if (!Service.Config.Get<M12S2LindwurmConfig>().ExperimentalTowerAssignment)
            return false;
        var meteor = module.FindComponent<IdyllicDreamElementalMeteor>();
        return meteor != null && meteor.Towers.Count > 0;
    }

    private static PlayerPlan Resolve(BossModule module, int slot, StrategyPreset strat)
    {
        var meteor = module.FindComponent<IdyllicDreamElementalMeteor>();
        if (meteor == null || meteor.Towers.Count == 0)
            return PlayerPlan.None;

        var towers = meteor.Towers;

        // towers this player is allowed to soak (light-vuln grouping is encoded in ForbiddenSoakers)
        var allowed = new List<Components.GenericTowers.Tower>(towers.Count);
        for (var i = 0; i < towers.Count; ++i)
            if (!towers[i].ForbiddenSoakers[slot])
                allowed.Add(towers[i]);
        if (allowed.Count == 0)
            return PlayerPlan.None;

        // deterministic tower order (clock angle around arena center), optionally mirrored by strat
        var center = module.Arena.Center;
        allowed.Sort((a, b) => AngleRad(center, a.Position).CompareTo(AngleRad(center, b.Position)));
        if (strat.Choice(MechanicKey, "standard") == "inverse")
            allowed.Reverse();

        // players sharing this player's group, in canonical role order; pair by index
        var party = Service.Config.Get<PartyRolesConfig>();
        var members = module.WorldState.Party.Members;
        var groupMask = allowed[0].ForbiddenSoakers; // forbidden set is identical within a group
        var eligible = new List<(int slot, PartyRolesConfig.Assignment role)>(8);
        for (var s = 0; s < members.Length && s < 8; ++s)
        {
            if (groupMask[s]) // forbidden in the allowed towers => belongs to the other group
                continue;
            eligible.Add((s, party[members[s].ContentId]));
        }
        eligible.Sort((a, b) => RoleRank(a.role).CompareTo(RoleRank(b.role)));

        var myIndex = eligible.FindIndex(e => e.slot == slot);
        if (myIndex < 0)
            return PlayerPlan.None;
        if (myIndex >= allowed.Count)
            myIndex = allowed.Count - 1; // clamp if player/tower counts disagree

        var tower = allowed[myIndex];
        return new PlayerPlan(targetPos: tower.Position, radius: 2f, activation: tower.Activation, hard: true);
    }

    private static float AngleRad(WPos center, WPos p) => (p - center).ToAngle().Rad;

    private static int RoleRank(PartyRolesConfig.Assignment a)
    {
        for (var i = 0; i < _order.Length; ++i)
            if (_order[i] == a)
                return i;
        return _order.Length; // unassigned sorts last
    }
}

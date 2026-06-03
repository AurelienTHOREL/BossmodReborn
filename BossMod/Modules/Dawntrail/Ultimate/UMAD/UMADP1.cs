namespace BossMod.Dawntrail.Ultimate.UMAD;

// Revolting Ruin III — 120-degree cone tankbuster (range 100), two hits. Draw the cone for the party; AI-nudge
// the baited main tank just north of the boss so the cone faces north.
sealed class RevoltingRuin(BossModule module) : Components.BaitAwayCast(module, (uint)AID._Ability_RevoltingRuinIII, new AOEShapeCone(100f, 60f.Degrees()), tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (CurrentBaits.Count == 0 || CurrentBaits[0].Target != actor)
            return;
        var cast = CurrentBaits[0].Source.CastInfo;
        if (cast == null)
            return;
        var boss = Module.PrimaryActor;
        var northSpot = boss.Position + new WDir(default, -(boss.HitboxRadius + 2f));
        hints.AddForbiddenZone(new AOEShapeCircle(2f, invertForbiddenZone: true), northSpot, activation: Module.CastFinishAt(cast));
    }
}

sealed class GravenImageCast(BossModule module) : Components.CastHint(module, (uint)AID._Ability_GravenImage, "Graven Image (tethers / markers)");
sealed class MysteryMagic(BossModule module) : Components.CastHint(module, (uint)AID._Ability_MysteryMagic, "Mystery Magic — read the markers");

// Blizzard III Blowout — only 47768 & 47774 are REAL 90-degree cones (range 40); 47771 is a visual fake, so we
// don't draw it. SimpleAOEGroups reads each cast's location+rotation => the two real cones / two quadrants.
sealed class BlizzardBlowout(BossModule module) : Components.SimpleAOEGroups(module, [
    (uint)AID._Ability_BlizzardIIIBlowout2, (uint)AID._Ability_BlizzardIIIBlowout1], new AOEShapeCone(40f, 45f.Degrees()));

// Thrumming Thunder III — only 47775 & 47777 are REAL rects (range 40, width 10); 47776 is fake.
sealed class ThrummingThunder(BossModule module) : Components.SimpleAOEGroups(module, [
    (uint)AID._Ability_ThrummingThunderIII2, (uint)AID._Ability_ThrummingThunderIII], new AOEShapeRect(40f, 5f));

// Flagrant Fire III — spread (icon 127 -> 47778, r5) and stack (icon 128 -> 47779, r6). Activation ~5s; tune.
sealed class FlagrantFireSpread(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID._Gen_Icon_m0462trg_a0c, (uint)AID._Ability_FlagrantFireIII1, 5f, 5.1d);
sealed class FlagrantFireStack(BossModule module) : Components.StackWithIcon(module, (uint)IconID._Gen_Icon_m0462trg_b0c, (uint)AID._Ability_FlagrantFireIII, 6f, 5.1d);

// Explosion — 4y circle (47786, 3s cast). UnmitigatedExplosion (47787) is the failure/raidwide.
sealed class Explosion(BossModule module) : Components.SimpleAOEs(module, (uint)AID._Ability_Explosion, new AOEShapeCircle(4f));

sealed class LightOfJudgment(BossModule module) : Components.RaidwideCast(module, (uint)AID._Ability_LightOfJudgment);

// Double-Trouble Trap (status 5078): the holder's buff becomes a ROLE STACK on expiry (resolution 47783, r6)
// and knocks back the stackers (everyone except the holder). Flagged for now; status-based stack+knockback TBD.
sealed class DoubleTroubleTrap(BossModule module) : Components.CastHint(module, (uint)AID._Ability_DoubleTroubleTrap, "Double-Trouble Trap: role stack + knockback");

// No-cast / event-object mechanics (resolve via EObjAnim or instant hits) — visible named warnings for now.
sealed class WaveCannon(BossModule module) : Components.CastHint(module, (uint)AID._Ability_PulseWave, "Wave Cannon (line beam)");
sealed class GravityPuddleRock(BossModule module) : Components.CastHints(module, [(uint)AID._Ability_Gravitas, (uint)AID._Ability_Vitrophyre], "Gravity puddle / rock (drop away)");
sealed class Hyperdrive(BossModule module) : Components.CastHint(module, (uint)AID._Ability_Hyperdrive, "Hyperdrive (spread)");
sealed class GravityCleaves(BossModule module) : Components.CastHints(module, [(uint)AID._Ability_GravitationalWave, (uint)AID._Ability_IntemperateWill], "Gravity cleave (magic circle)");

// First Graven Image tether set: knocks the tethered player back (away from the add). 2nd set is puddle/rock.
sealed class GravenImageTetherKnockback(BossModule module) : Components.GenericKnockback(module)
{
    private readonly List<(ulong target, WPos origin, DateTime activation)> _active = [];
    private readonly Components.GenericKnockback.Knockback[] _one = new Components.GenericKnockback.Knockback[1];

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID._Gen_Tether_chn_elem0f)
            _active.Add((tether.Target, source.Position, WorldState.FutureTime(5d)));
    }

    public override void OnUntethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID != (uint)TetherID._Gen_Tether_chn_elem0f)
            return;
        var target = tether.Target;
        _active.RemoveAll(t => t.target == target);
    }

    public override ReadOnlySpan<Components.GenericKnockback.Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        foreach (var t in _active)
        {
            if (t.target == actor.InstanceID)
            {
                _one[0] = new(t.origin, 15f, t.activation);
                return _one;
            }
        }
        return [];
    }
}

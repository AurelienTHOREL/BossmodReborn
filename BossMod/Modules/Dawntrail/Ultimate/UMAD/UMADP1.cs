namespace BossMod.Dawntrail.Ultimate.UMAD;

// Revolting Ruin III — 120-degree cone tankbuster (range 100), two hits. Draw the cone for the party; AI-nudge
// the baited main tank just north of the boss so the cone faces north.
sealed class RevoltingRuin(BossModule module) : Components.BaitAwayCast(module, (uint)AID._Ability_RevoltingRuinIII, RevoltingRuin.Cone, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    public static readonly AOEShapeCone Cone = new(100f, 60f.Degrees());

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

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        // It hits TWICE: the second hit (50401) is a self-cast cone in the boss's facing direction. Draw it too.
        if (CurrentBaits.Count > 0)
            Cone.Draw(Arena, Module.PrimaryActor.Position, Module.PrimaryActor.Rotation, Colors.AOE);
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

// Double-Trouble Trap (status 5078): the holder's buff becomes a ROLE STACK (r6) on expiry. Stack drawn from the
// status. (Role filtering by job role TBD; for now anyone can stack.)
sealed class DoubleTroubleTrap(BossModule module) : Components.GenericStackSpread(module)
{
    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID._Gen_DoubleTroubleTrap)
            Stacks.Add(new(actor, 6f, 2, int.MaxValue, status.ExpireAt));
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID._Gen_DoubleTroubleTrap)
            Stacks.RemoveAll(s => s.Target == actor);
    }
}

// ...and the stackers (everyone except the holder) get knocked back from the holder when it resolves.
sealed class DoubleTroubleTrapKnockback(BossModule module) : Components.GenericKnockback(module)
{
    private readonly List<(ulong holder, WPos pos, DateTime activation)> _holders = [];
    private readonly Components.GenericKnockback.Knockback[] _one = new Components.GenericKnockback.Knockback[1];

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID._Gen_DoubleTroubleTrap)
            _holders.Add((actor.InstanceID, actor.Position, status.ExpireAt));
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        if (status.ID != (uint)SID._Gen_DoubleTroubleTrap)
            return;
        var id = actor.InstanceID;
        _holders.RemoveAll(h => h.holder == id);
    }

    public override ReadOnlySpan<Components.GenericKnockback.Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        foreach (var h in _holders)
        {
            if (h.holder != actor.InstanceID)
            {
                _one[0] = new(h.pos, 10f, h.activation);
                return _one;
            }
        }
        return [];
    }
}

// Gravitas — blue puddles the 2nd-set tethered players drop; drawn from the GravitasP1 voidzone actors (r5).
sealed class GravitasPuddle(BossModule module) : Components.Voidzone(module, 5f, m => m.Enemies((uint)OID.GravitasP1));

// No-cast / event-object mechanics (resolve via EObjAnim or instant hits) — visible named warnings for now.
sealed class WaveCannon(BossModule module) : Components.CastHint(module, (uint)AID._Ability_PulseWave, "Wave Cannon (line beam)");
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
                _one[0] = new(t.origin, 10f, t.activation);
                return _one;
            }
        }
        return [];
    }
}

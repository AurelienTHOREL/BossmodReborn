namespace BossMod.Dawntrail.Ultimate.DMU;

// 制裁之光 — the actual raidwide.
sealed class LightOfJudgment(BossModule module) : Components.RaidwideCast(module, (uint)AID.LightOfJudgment);

// 恶狠狠毁荡 — tankbuster CONE that also hits the rest of the party if they're in front. Draw the cone for the
// party to dodge; AI-nudge the baited main tank to point it NORTH (stand just north of the boss).
// Cone size is a first guess — tune in-game.
sealed class ViciousDevastation(BossModule module) : Components.BaitAwayCast(module, (uint)AID.ViciousDevastation1, new AOEShapeCone(40f, 35f.Degrees()), tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        // Only the current bait target (the main tank) is steered. North in world coords is -Z; park the tank
        // just north of the boss so the cone faces north, away from the party.
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

sealed class Hyperdrive(BossModule module) : Components.CastHint(module, (uint)AID.Hyperdrive, "Death sentence (x2)");
sealed class GravenImageCast(BossModule module) : Components.CastHint(module, (uint)AID.GravenImageCast, "Graven Image (tethers / markers)");

// 扩大大冰封 "Blizzard III Blowout". The REAL damaging cone is 0xBA9B (BlizzardCone3): it fires as TWO opposite
// 90-degree cones whose direction is set by the ? markers. The other variants (BA94/95/98/9E) are decoy
// telegraphs that deal no damage (replay: BA9B hit players 39x vs 0-1 for the rest) — drawing them all lit the
// whole arena, so we draw only the real one => two cones / two quadrants.
sealed class BlizzardCones(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BlizzardCone3, new AOEShapeCone(20f, 45f.Degrees()));

// 劈啪啪暴雷 — the second set adds LINES (rectangles across the arena), not cones (may be real or fake).
// FIRST-GUESS length/width; tune in-game.
sealed class ThunderLines(BossModule module) : Components.SimpleAOEGroups(module, [
    (uint)AID.CrackleThunder1, (uint)AID.CrackleThunder2, (uint)AID.CrackleThunder3],
    new AOEShapeRect(50f, 4f));

// 呼啦啦爆炎 "Flare" — first-guess circle.
sealed class Flare(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.Flare1, (uint)AID.Flare2], new AOEShapeCircle(6f));

// 连环环陷阱 — soak towers, spawned where the 波动炮 beams hit players. SpreadFromCastTargets keeps players apart
// so the towers/beams don't overlap. Radii are first-guesses; tune in-game.
sealed class ChainTrap1(BossModule module) : Components.CastTowers(module, (uint)AID.ChainTrap1, 4f);
sealed class ChainTrap2(BossModule module) : Components.CastTowers(module, (uint)AID.ChainTrap2, 4f);

// 波动炮 — spread laser ("分散激光"); spreading avoids overlapping beams (and the towers they spawn).
sealed class WaveCannon(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.WaveCannon, 6f);

// 重力弹 — stack ("分摊").
sealed class GravityBullet(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.GravityBullet, 6f);

sealed class Explosions(BossModule module) : Components.CastHints(module, [(uint)AID.Explosion, (uint)AID.BigExplosion, (uint)AID.GravityBurst], "Explosion — shape TBD");
sealed class Teleports(BossModule module) : Components.CastHints(module, [(uint)AID.Teleport, (uint)AID.MagicCircle], "Magic circle placement");
sealed class EnrageBlowout(BossModule module) : Components.CastHint(module, (uint)AID.EnrageBlowout, "Enrage: Blizzard III Blowout");

sealed class GravenImageAttacks(BossModule module) : Components.CastHints(module, [
    (uint)AID.RockBullet, (uint)AID.GravityWave1, (uint)AID.GravityWave2,
    (uint)AID.AveMaria, (uint)AID.HolyAura, (uint)AID.SleepAura], "Graven Image attack");

// After the first Graven Image, each Graven Image tethers a player and KNOCKS THEM BACK (away from the add).
// We track the tethers and expose a knockback away from the tether source. Distance/timing are first guesses.
sealed class GravenImageTetherKnockback(BossModule module) : Components.GenericKnockback(module)
{
    private readonly List<(ulong target, WPos origin, DateTime activation)> _active = [];
    private readonly Components.GenericKnockback.Knockback[] _one = new Components.GenericKnockback.Knockback[1];

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.GravenImage)
            _active.Add((tether.Target, source.Position, WorldState.FutureTime(5d)));
    }

    public override void OnUntethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID != (uint)TetherID.GravenImage)
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

// Tracks active Graven Image tethers (resolver TBD beyond the knockback above).
sealed class GravenImageAdds(BossModule module) : BossComponent(module)
{
    public readonly List<(Actor source, ulong targetId)> Tethers = [];

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.GravenImage)
            Tethers.Add((source, tether.Target));
    }

    public override void OnUntethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.GravenImage)
            Tethers.RemoveAll(t => t.source == source);
    }
}

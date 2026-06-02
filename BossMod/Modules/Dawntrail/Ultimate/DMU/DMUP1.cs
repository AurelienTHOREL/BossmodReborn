namespace BossMod.Dawntrail.Ultimate.DMU;

// Periodic whole-arena damage (self-cast). RaidwideCast adds predicted-damage AI hints automatically.
sealed class Raidwide1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide1);
sealed class Raidwide2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide2);

// 4.7s cast on a player (+self). Tankbuster or stack — target rule/shape unknown.
// TODO(position-pass): resolve as Components.TankbusterCast or StackWithCastTargets once confirmed.
sealed class ProximityOrTB(BossModule module) : Components.CastCounter(module, (uint)AID.ProximityOrTB);

// Signature multi-AOE pattern: 8 cast variants fire in simultaneous groups of 2-5.
// TODO(position-pass): needs cast Location/Rotation + resolution-hit positions to infer AOE shapes/sectors,
// then becomes Components.SimpleAOEs / SimpleAOEGroups keyed by the per-variant shape.
sealed class PatternBurst(BossModule module) : Components.CastCounterMulti(module, [
    (uint)AID.Pattern1, (uint)AID.Pattern2, (uint)AID.Pattern3, (uint)AID.Pattern4,
    (uint)AID.Pattern5, (uint)AID.Pattern6, (uint)AID.Pattern7, (uint)AID.Pattern8]);

// Shorter 2.7s multi-cast pattern (fires in bursts).
// TODO(position-pass): same as PatternBurst — infer shapes, then convert to SimpleAOEs/Groups.
sealed class ShortPattern(BossModule module) : Components.CastCounterMulti(module, [
    (uint)AID.ShortPattern1, (uint)AID.ShortPattern2]);

// Graven Image adds tether (TetherID.GravenImage = 0x2D) to players and apply GravenVuln (gaze/vuln).
// Skeleton: tracks active tethers so a future resolver can act on them.
// TODO(position-pass): determine the player requirement (face away / kill / break LOS) and add hints.
// Signatures verified against BossComponent: OnTethered(Actor, in ActorTetherInfo);
// ActorTetherInfo = (uint ID, ulong Target). Target is an instance id -> WorldState.Actors.Find.
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

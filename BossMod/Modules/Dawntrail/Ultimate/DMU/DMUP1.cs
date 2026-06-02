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

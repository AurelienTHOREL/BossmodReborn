namespace BossMod.Dawntrail.Ultimate.DMU;

// Periodic whole-arena damage (self-cast). RaidwideCast adds predicted-damage AI hints automatically.
sealed class Raidwide1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide1);
sealed class Raidwide2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide2);

namespace BossMod.Dawntrail.Ultimate.DMU;

// Periodic whole-arena damage (self-cast). Shows as a raidwide warning + predicted-damage AI hint.
sealed class Raidwide1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide1);
sealed class Raidwide2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide2);

// Periodic 4.7s cast onto a player. Likely tankbuster or stack — shown as a tankbuster cast warning.
// TODO(in-game): confirm whether it's a tank cleave / stack and give it a real AOE shape.
sealed class ProximityOrTB(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ProximityOrTB, "Tankbuster/stack?");

// Signature multi-AOE pattern: 8 cast variants fire in simultaneous groups from the arena center (and a few
// from ring positions), each rotated to a different direction. SimpleAOEGroups draws the shape at each cast's
// own location+rotation, so the DIRECTIONS come straight from the replay.
// FIRST-GUESS shape: 45deg-wide cone (halfAngle 22.5deg), radius = arena. The size/angle are a starting point —
// TUNE IN-GAME (could be wider cones, rectangles, or donuts).
sealed class PatternBurst(BossModule module) : Components.SimpleAOEGroups(module, [
    (uint)AID.Pattern1, (uint)AID.Pattern2, (uint)AID.Pattern3, (uint)AID.Pattern4,
    (uint)AID.Pattern5, (uint)AID.Pattern6, (uint)AID.Pattern7, (uint)AID.Pattern8],
    new AOEShapeCone(25f, 22.5f.Degrees()));

// Shorter 2.7s pattern. FIRST-GUESS: circle at the cast location. TUNE IN-GAME.
sealed class ShortPattern(BossModule module) : Components.SimpleAOEGroups(module, [
    (uint)AID.ShortPattern1, (uint)AID.ShortPattern2], new AOEShapeCircle(8f));

// Mechanics that only appear in the longer pull; their shapes aren't resolved yet, so show a visible named
// cast warning (no false danger zone) until a position pass + in-game check nails them down.
sealed class LateAOEB9(BossModule module) : Components.CastHint(module, (uint)AID.LateAOEB9, "AOE (B9) - shape TBD");
sealed class LateAOEBB(BossModule module) : Components.CastHint(module, (uint)AID.LateAOEBB, "AOE (BB) - shape TBD");
sealed class LateAOE554(BossModule module) : Components.CastHint(module, (uint)AID.LateAOE554, "AOE (554) - shape TBD");

// Graven Image adds tether (TetherID.GravenImage = 0x2D) to players and apply GravenVuln (gaze/vuln).
// Skeleton: tracks active tethers so a future resolver can act on them.
// TODO(position-pass): determine the player requirement (face away / kill / break LOS) and add hints.
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

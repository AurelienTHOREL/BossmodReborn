namespace BossMod.Dawntrail.Ultimate.DMU;

// 制裁之光 — the actual raidwide (big whole-arena AOE).
sealed class LightOfJudgment(BossModule module) : Components.RaidwideCast(module, (uint)AID.LightOfJudgment);

// 恶狠狠毁荡 — two-hit tankbuster (first then second enmity). Shown as tankbuster cast warnings.
sealed class ViciousDevastation(BossModule module) : Components.SingleTargetCasts(module, [(uint)AID.ViciousDevastation1, (uint)AID.ViciousDevastation2], "Tankbuster (2 hits)");

// 超驱动 / 二连死刑 — death sentence, cast twice. Shown as a cast warning (stack/share TBD).
sealed class Hyperdrive(BossModule module) : Components.CastHint(module, (uint)AID.Hyperdrive, "Death sentence (x2)");

// 众神之像 — summons Graven Images / reveals the ? markers (tether + pattern setup). Not damage; just flag the cast.
sealed class GravenImageCast(BossModule module) : Components.CastHint(module, (uint)AID.GravenImageCast, "Graven Image (tethers / markers)");

// 玄乎乎魔法 — tether/knockback + "observe the ? markers" setup. Flag the cast (no danger zone).
sealed class MysteriousMagic(BossModule module) : Components.CastHint(module, (uint)AID.MysteriousMagic, "Tethers / knockback — read markers");

// 扩大大冰封 "Blizzard III Blowout" + 劈啪啪暴雷 "Crackle Thunder": directional blowouts cast from the center
// (and ring positions) in groups. Per the timeline these are TWO 90-degree cones, so halfAngle = 45 deg.
// SimpleAOEGroups reads each cast's own location+rotation, so the directions come straight from the game.
sealed class BlizzardThunderCones(BossModule module) : Components.SimpleAOEGroups(module, [
    (uint)AID.BlizzardCone1, (uint)AID.BlizzardCone2, (uint)AID.BlizzardCone3, (uint)AID.BlizzardCone4,
    (uint)AID.CrackleThunder1, (uint)AID.CrackleThunder2, (uint)AID.CrackleThunder3],
    new AOEShapeCone(25f, 45f.Degrees()));

// 呼啦啦爆炎 "Flare". Shape not yet confirmed — first-guess circle at the cast spot.
sealed class Flare(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.Flare1, (uint)AID.Flare2], new AOEShapeCircle(6f));

// 连环环陷阱 — soak towers ("踩塔"), two waves (BAA6 then BAA7). FIRST-GUESS radius/soaker count; tune in-game.
sealed class ChainTrap1(BossModule module) : Components.CastTowers(module, (uint)AID.ChainTrap1, 4f);
sealed class ChainTrap2(BossModule module) : Components.CastTowers(module, (uint)AID.ChainTrap2, 4f);

// 波动炮 — spread laser ("分散激光"), cast onto players. FIRST-GUESS radius; tune in-game.
sealed class WaveCannon(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.WaveCannon, 6f);

// 重力弹 — stack ("分摊"), cast onto a player. FIRST-GUESS radius; tune in-game.
sealed class GravityBullet(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.GravityBullet, 6f);

// 爆炸 / 大爆炸 / 重力爆发 — shapes not yet confirmed; visible named warnings until resolved.
sealed class Explosions(BossModule module) : Components.CastHints(module, [(uint)AID.Explosion, (uint)AID.BigExplosion, (uint)AID.GravityBurst], "Explosion — shape TBD");

// 唰啦啦传送 (place magic circles) + 制裁之光 enrage. Flag the casts.
sealed class Teleports(BossModule module) : Components.CastHints(module, [(uint)AID.Teleport, (uint)AID.MagicCircle], "Magic circle placement");
sealed class EnrageBlowout(BossModule module) : Components.CastHint(module, (uint)AID.EnrageBlowout, "Enrage: Blizzard III Blowout");

// Remaining Graven Image (add) attacks — shapes not yet confirmed; visible named warnings.
sealed class GravenImageAttacks(BossModule module) : Components.CastHints(module, [
    (uint)AID.RockBullet, (uint)AID.GravityWave1, (uint)AID.GravityWave2,
    (uint)AID.AveMaria, (uint)AID.HolyAura, (uint)AID.SleepAura], "Graven Image attack");

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

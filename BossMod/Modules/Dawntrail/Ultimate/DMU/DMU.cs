namespace BossMod.Dawntrail.Ultimate.DMU;

public enum OID : uint
{
    Kefka = 0x4C30,       // R6.0, primary
    GravenImage = 0x4C31, // R0.5, adds (spawn mid-fight, x36 across both pulls), tether 0x2D to players
    Helper = 0x233C,      // standard helper
}

// Names from a community timeline (整合推断, ZoneId 1363). Provisional English names approximate the
// Chinese mechanic names; replace with official names once available.
public enum AID : uint
{
    _AutoAttack_ = 0xC252, // 49746, Kefka->player, single-target

    // 恶狠狠毁荡 "Vicious Devastation" — two-hit tankbuster (MT then OT)
    ViciousDevastation1 = 0xC403, // 50179, ->player (first enmity)
    ViciousDevastation2 = 0xC4E1, // 50401, (second enmity)

    GravenImageCast = 0xBCF2, // 48370, 众神之像 — summons Graven Images / shows ? markers (tether setup; NOT a raidwide)
    LightOfJudgment = 0xC622, // 50722, 制裁之光 — big raidwide AOE
    Hyperdrive = 0xC24B,      // 49739, 超驱动 / 二连死刑 — death sentence, cast twice

    // 扩大大冰封 "Blizzard III Blowout" — 90-degree cones from arena center, fire in groups
    BlizzardCone1 = 0xBA95, // 47765
    BlizzardCone2 = 0xBA98, // 47768
    BlizzardCone3 = 0xBA9B, // 47771
    BlizzardCone4 = 0xBA9E, // 47774

    // 劈啪啪暴雷 "Crackle Thunder" — directional, cast from ring positions (assumed similar cones)
    CrackleThunder1 = 0xBA9F, // 47775
    CrackleThunder2 = 0xBAA0, // 47776
    CrackleThunder3 = 0xBAA1, // 47777

    MysteriousMagic = 0xBA94, // 47764, 玄乎乎魔法 — tether/knockback + observe ? markers (setup, not a damage cone)

    Flare1 = 0xBAA2,    // 呼啦啦爆炎
    Flare2 = 0xBAA3,    // 47779
    ChainTrap1 = 0xBAA6, // 47782, 连环环陷阱 — towers (step in)
    ChainTrap2 = 0xBAA7, // 47783
    Explosion = 0xBAAA,  // 47786, 爆炸
    BigExplosion = 0xBAAB, // 大爆炸
    GravityBurst = 0xBAAD, // 47789, 重力爆发

    // Teleport / magic-circle placement, then the enrage
    Teleport = 0xBAB9,      // 47801, 唰啦啦传送
    MagicCircle = 0xBABA,   // 唰啦啦传送 (放魔法阵)
    EnrageBlowout = 0xBABB, // 47803, 制裁之光 (狂暴) — Blizzard III Blowout, two 90-degree cones
    Unk554 = 0xC554,        // 50516, 2.7s self — seen in replay, not yet in the timeline

    LocHit = 0xC3FD, // 50173, ->location (unmapped)

    // Graven Image (add 0x4C31) actions:
    WaveCannon = 0xBAA8,    // 47784, 波动炮 — spread laser
    GravenImageUnk = 0xBAA9, // 47785, ->player (unmapped)
    GravityBullet = 0xBAAC, // 47788, 重力弹 — stack
    RockBullet = 0xBAB0,    // 47792, 岩石弹
    GravityWave1 = 0xBAB1,  // 47793, 重力波 / 扑杀的神气
    GravityWave2 = 0xBAB2,  // 47794
    AveMaria = 0xBAB3,      // 圣母颂
    HolyAura = 0xBAB5,      // 圣母的神气
    SleepAura = 0xBAB6,     // 睡魔的神气
}

public enum SID : uint
{
    GravenVuln = 0xB7D,  // 2941, Kefka/GravenImage->player, dur 4 (gaze/vuln on add hits?)
    Unk13D6 = 0x13D6,    // 5078, none->player, dur 68
}

public enum IconID : uint
{
    Headmarker80 = 0x80, // 128, ->player (x4)
    HeadmarkerDA = 0xDA, // 218, ->player (x4)
}

public enum TetherID : uint
{
    GravenImage = 0x2D, // 45, GravenImage->player (x40)
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 1094u, NameID = 7131u, PrimaryActorOID = (uint)OID.Kefka, PlanLevel = 100)]
// Arena: center (100,100) confirmed from replay positions; radius set from the player wall (p99=max=27.5 from
// center). Using 25 (players reached ~27.5 incl. knockback overshoot) — confirm the exact value in-game.
public sealed class DMU(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(25f))
{
    public override bool ShouldPrioritizeAllEnemies => true;

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.GravenImage));
    }
}

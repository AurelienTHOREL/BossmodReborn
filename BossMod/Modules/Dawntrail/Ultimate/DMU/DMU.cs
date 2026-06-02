namespace BossMod.Dawntrail.Ultimate.DMU;

public enum OID : uint
{
    Kefka = 0x4C30,       // R6.0, primary
    GravenImage = 0x4C31, // R0.5, adds (spawn mid-fight, x36 across both pulls), tether 0x2D to players
    Helper = 0x233C,      // standard helper
}

public enum AID : uint
{
    _AutoAttack_ = 0xC252,  // 49746, Kefka->player, no cast, single-target (x50)

    Raidwide1 = 0xBCF2,     // 48370, Kefka->self, 2.7s cast (recurs ~36s/~87s)
    Raidwide2 = 0xC622,     // 50722, Kefka->self, 4.7s cast, AOE (recurs ~67s/~137s)
    ProximityOrTB = 0xC403, // 50179, Kefka->player+self, 4.7s cast (recurs ~20s/~102s) — tankbuster/stack TBD

    // Signature multi-AOE pattern: 8 variants fire in simultaneous groups of 2-5 (all 4.7s self casts).
    Pattern1 = 0xBA94, // 47764
    Pattern2 = 0xBA95, // 47765
    Pattern3 = 0xBA98, // 47768
    Pattern4 = 0xBA9B, // 47771 (AOE)
    Pattern5 = 0xBA9E, // 47774
    Pattern6 = 0xBA9F, // 47775
    Pattern7 = 0xBAA0, // 47776 (AOE)
    Pattern8 = 0xBAA1, // 47777

    ShortPattern1 = 0xBAA6, // 47782, 2.7s self
    ShortPattern2 = 0xBAAA, // 47786, 2.7s self (groups of ~4)

    // Instant resolution/damage of the casts above (no cast bar):
    PatternResolve1 = 0xBAA3, // 47779, ->player, AOE
    PatternResolve2 = 0xBAA7, // 47783, ->player, AOE
    PatternResolve3 = 0xBAAD, // 47789, self, AOE
    BigHit = 0xC24B,          // 49739, ->player, AOE
    LocHit = 0xC3FD,          // 50173, ->location
    SelfHit = 0xC4E1,         // 50401, self

    // Graven Image (add) actions:
    GravenImageSelf1 = 0xBAA8,    // 47784, self
    GravenImagePlayer1 = 0xBAA9,  // 47785, ->player
    GravenImageAOE = 0xBAAC,      // 47788, ->player, AOE
    GravenImagePlayer2 = 0xBAB0,  // 47792, ->player
    GravenImageSelf2 = 0xBAB1,    // 47793, self
    GravenImageSelf3 = 0xBAB2,    // 47794, self
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
public sealed class DMU(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f))
{
    public override bool ShouldPrioritizeAllEnemies => true;

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.GravenImage));
    }
}

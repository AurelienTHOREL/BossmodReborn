namespace BossMod.Dawntrail.Ultimate.DMU;

sealed class DMUStates : StateMachineBuilder
{
    public DMUStates(DMU module) : base(module) // MUST be the concrete DMU type: registry does an exact GetConstructor([typeof(DMU)])
    {
        SimplePhase(default, P1, "P1: Kefka")
            .Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed;
    }

    // Timeline derived from the community "整合推断" sheet (ZoneId 1363), normalised to the C403 opener.
    // Times are approximate (±0.1-0.3s) and some source replays are solo/incomplete, so the sequence tracks the
    // major boss cast telegraphs; instant add resolutions (stacks/spreads/towers/explosions) are drawn by their
    // own components, which are ALL activated up front so nothing is hidden if a pull deviates from this order.
    private void P1(uint id)
    {
        // Opener tankbuster — activate every visualizer here (each draws only on its own cast).
        Cast(id, (uint)AID.ViciousDevastation1, 15.6f, 4.7f, "Tankbuster 1")
            .ActivateOnEnter<LightOfJudgment>()
            .ActivateOnEnter<ViciousDevastation>()
            .ActivateOnEnter<Hyperdrive>()
            .ActivateOnEnter<GravenImageCast>()
            .ActivateOnEnter<MysteriousMagic>()
            .ActivateOnEnter<BlizzardThunderCones>()
            .ActivateOnEnter<Flare>()
            .ActivateOnEnter<ChainTrap1>()
            .ActivateOnEnter<ChainTrap2>()
            .ActivateOnEnter<WaveCannon>()
            .ActivateOnEnter<GravityBullet>()
            .ActivateOnEnter<Explosions>()
            .ActivateOnEnter<Teleports>()
            .ActivateOnEnter<EnrageBlowout>()
            .ActivateOnEnter<GravenImageAttacks>()
            .ActivateOnEnter<GravenImageAdds>();

        // Graven Image 1 -> Blizzard cones + tethers -> towers x2 -> Blizzard + Thunder cones
        Cast(id + 0x10000, (uint)AID.GravenImageCast, 13.8f, 2.7f, "Graven Image 1");
        CastMulti(id + 0x20000, [(uint)AID.BlizzardCone1, (uint)AID.BlizzardCone2, (uint)AID.BlizzardCone3, (uint)AID.BlizzardCone4], 8.1f, 4.7f, "Blizzard cones");
        Cast(id + 0x30000, (uint)AID.ChainTrap1, 7.1f, 2.7f, "Towers 1");
        Cast(id + 0x40000, (uint)AID.ChainTrap2, 5.2f, 2.7f, "Towers 2");
        CastMulti(id + 0x50000, [(uint)AID.BlizzardCone1, (uint)AID.BlizzardCone2, (uint)AID.BlizzardCone3, (uint)AID.BlizzardCone4, (uint)AID.CrackleThunder1, (uint)AID.CrackleThunder2, (uint)AID.CrackleThunder3], 3.9f, 4.7f, "Blizzard + Thunder cones");

        // Raidwide + double death-sentence
        Cast(id + 0x60000, (uint)AID.LightOfJudgment, 9.2f, 4.7f, "Raidwide");

        // Graven Image 2 (stack) -> Blizzard stack
        Cast(id + 0x70000, (uint)AID.GravenImageCast, 17.6f, 2.7f, "Graven Image 2 (stack)");
        CastMulti(id + 0x80000, [(uint)AID.BlizzardCone1, (uint)AID.BlizzardCone2, (uint)AID.BlizzardCone3, (uint)AID.BlizzardCone4], 7.1f, 4.7f, "Blizzard (stack)");

        // Tankbuster 2 (then Graven Image gravity attacks resolve via their components during the gap)
        Cast(id + 0x90000, (uint)AID.ViciousDevastation1, 10.0f, 4.7f, "Tankbuster 2");

        // Raidwide 2 + double death-sentence
        Cast(id + 0xA0000, (uint)AID.LightOfJudgment, 35.1f, 4.7f, "Raidwide 2");

        // Teleport / magic circles -> Graven Image 3 -> Blizzard + Thunder
        Cast(id + 0xB0000, (uint)AID.Teleport, 19.1f, 4.7f, "Teleport / magic circles");
        Cast(id + 0xC0000, (uint)AID.GravenImageCast, 12.0f, 2.7f, "Graven Image 3");
        CastMulti(id + 0xD0000, [(uint)AID.BlizzardCone1, (uint)AID.BlizzardCone2, (uint)AID.BlizzardCone3, (uint)AID.BlizzardCone4, (uint)AID.CrackleThunder1, (uint)AID.CrackleThunder2, (uint)AID.CrackleThunder3], 22.6f, 4.7f, "Blizzard + Thunder cones");

        // Enrage
        Cast(id + 0xE0000, (uint)AID.EnrageBlowout, 16.3f, 4.7f, "Enrage: Blizzard III Blowout");
        SimpleState(id + 0xFF0000u, 10000f, "???");
    }
}

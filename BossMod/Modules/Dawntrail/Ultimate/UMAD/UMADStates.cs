namespace BossMod.Dawntrail.Ultimate.UMAD;

sealed class UMADStates : StateMachineBuilder
{
    public UMADStates(UMAD module) : base(module) // MUST be the concrete UMAD type: registry does an exact GetConstructor([typeof(UMAD)])
    {
        SimplePhase(default, P1, "P1")
            .Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed;
    }

    // Timeline normalised to the Revolting Ruin opener; times are approximate (some replays are solo/incomplete),
    // so all cast-driven visualizers are active for the whole phase and each draws on its own cast — the sequence
    // below just provides the named "what's next" flow. The tankbuster cone and the first tether knockback are
    // gated so they don't bleed into other mechanics.
    private void P1(uint id)
    {
        Cast(id, (uint)AID._Ability_RevoltingRuinIII, 15.6f, 5.0f, "Tankbuster 1")
            .ActivateOnEnter<GravenImageCast>()
            .ActivateOnEnter<MysteryMagic>()
            .ActivateOnEnter<BlizzardBlowout>()
            .ActivateOnEnter<ThrummingThunder>()
            .ActivateOnEnter<FlagrantFireSpread>()
            .ActivateOnEnter<FlagrantFireStack>()
            .ActivateOnEnter<Explosion>()
            .ActivateOnEnter<LightOfJudgment>()
            .ActivateOnEnter<DoubleTroubleTrap>()
            .ActivateOnEnter<DoubleTroubleTrapKnockback>()
            .ActivateOnEnter<WaveCannon>()
            .ActivateOnEnter<GravitasPuddle>()
            .ActivateOnEnter<Hyperdrive>()
            .ActivateOnEnter<GravityCleaves>()
            .ActivateOnEnter<RevoltingRuin>(); // tankbuster: active whole phase (cast-driven, so only draws on its cast — covers the Gravitas-phase tankbuster too)

        // Graven Image 1 -> the FIRST tether set knocks players back (gated off before the 2nd set, which is puddle/rock).
        Cast(id + 0x10000, (uint)AID._Ability_GravenImage, 13.8f, 3.0f, "Graven Image 1")
            .ActivateOnEnter<GravenImageTetherKnockback>();

        CastMulti(id + 0x20000, [(uint)AID._Ability_BlizzardIIIBlowout2, (uint)AID._Ability_BlizzardIIIBlowout1], 8.1f, 5.0f, "Blizzard cones");
        Cast(id + 0x30000, (uint)AID._Ability_DoubleTroubleTrap, 7.1f, 3.0f, "Double-Trouble Trap");
        CastMulti(id + 0x40000, [(uint)AID._Ability_BlizzardIIIBlowout2, (uint)AID._Ability_BlizzardIIIBlowout1, (uint)AID._Ability_ThrummingThunderIII2, (uint)AID._Ability_ThrummingThunderIII], 5.7f, 5.0f, "Blizzard + Thunder");

        Cast(id + 0x50000, (uint)AID._Ability_LightOfJudgment, 9.2f, 5.0f, "Raidwide")
            .DeactivateOnExit<GravenImageTetherKnockback>();

        Cast(id + 0x60000, (uint)AID._Ability_GravenImage, 17.6f, 3.0f, "Graven Image 2 (puddle/rock)");

        Cast(id + 0x70000, (uint)AID._Ability_RevoltingRuinIII, 20.0f, 5.0f, "Tankbuster 2");

        Cast(id + 0x80000, (uint)AID._Ability_LightOfJudgment, 35.1f, 5.0f, "Raidwide 2");
        SimpleState(id + 0xFF0000u, 10000f, "???");
    }
}

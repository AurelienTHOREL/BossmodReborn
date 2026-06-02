namespace BossMod.Dawntrail.Ultimate.DMU;

sealed class DMUStates : StateMachineBuilder
{
    public DMUStates(DMU module) : base(module) // MUST be the concrete DMU type: registry does an exact GetConstructor([typeof(DMU)])
    {
        SimplePhase(default, P1, "P1: Kefka")
            .Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed;
    }

    private void P1(uint id)
    {
        // The observed P1 cast order varies between pulls and has long gaps, so we do NOT hard-sequence it
        // (a wrong sequence just desyncs and hides hints). Instead a single open state activates every visualizer
        // for the whole phase; each component reacts to its OWN cast, so a mechanic draws whenever Kefka casts it,
        // in any order. Once the mechanics are confirmed in-game this can become a real timed sequence.
        SimpleState(id, 10000f, "P1 (open)")
            .ActivateOnEnter<LightOfJudgment>()
            .ActivateOnEnter<ViciousDevastation>()
            .ActivateOnEnter<Hyperdrive>()
            .ActivateOnEnter<GravenImageCast>()
            .ActivateOnEnter<MysteriousMagic>()
            .ActivateOnEnter<BlizzardThunderCones>()
            .ActivateOnEnter<Flare>()
            .ActivateOnEnter<ChainTrapTowers>()
            .ActivateOnEnter<Explosions>()
            .ActivateOnEnter<Teleports>()
            .ActivateOnEnter<EnrageBlowout>()
            .ActivateOnEnter<GravenImageAttacks>()
            .ActivateOnEnter<GravenImageAdds>();
    }
}

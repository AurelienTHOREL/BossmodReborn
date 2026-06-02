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
        // Raidwides are active for the whole phase; they react to their own casts.
        Cast(id, (uint)AID.ProximityOrTB, 20.1f, 4.7f, "Proximity/tankbuster?")
            .ActivateOnEnter<Raidwide1>()
            .ActivateOnEnter<Raidwide2>()
            .ActivateOnEnter<ProximityOrTB>();

        Cast(id + 0x10000, (uint)AID.Raidwide1, 9.8f, 2.7f, "Raidwide 1");

        CastMulti(id + 0x20000, [(uint)AID.Pattern1, (uint)AID.Pattern3], 4.0f, 4.7f, "Pattern burst")
            .ActivateOnEnter<PatternBurst>();

        Cast(id + 0x30000, (uint)AID.ShortPattern1, 7.1f, 2.7f, "Short pattern")
            .ActivateOnEnter<ShortPattern>();

        CastMulti(id + 0x40000, [(uint)AID.Pattern1, (uint)AID.Pattern3, (uint)AID.Pattern6], 5.7f, 4.7f, "Pattern burst 2");

        Cast(id + 0x50000, (uint)AID.Raidwide2, 4.3f, 4.7f, "Raidwide 2")
            .ActivateOnEnter<GravenImageAdds>();

        Cast(id + 0x60000, (uint)AID.Raidwide1, 15.3f, 2.7f, "Raidwide 1 (rep)");

        CastMulti(id + 0x70000, [(uint)AID.Pattern2, (uint)AID.Pattern3], 2.4f, 4.7f, "Pattern burst 3");

        // Loop point observed at ~102s; leave an open state for the rest of P1 (refine after position pass).
        Cast(id + 0x80000, (uint)AID.ProximityOrTB, 5.1f, 4.7f, "Proximity/tankbuster? (loop)");
        SimpleState(id + 0xFF0000u, 10000f, "???");
    }
}

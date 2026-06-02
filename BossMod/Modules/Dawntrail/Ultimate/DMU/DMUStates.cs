namespace BossMod.Dawntrail.Ultimate.DMU;

sealed class DMUStates : StateMachineBuilder
{
    public DMUStates(DMU module) : base(module)
    {
        // P1 only for now; expanded in a later task. Phase ends when Kefka dies/despawns.
        SimplePhase(default, P1, "P1: Kefka")
            .Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed;
    }

    private void P1(uint id)
    {
        // Placeholder open state; replaced with the real timeline in a later task.
        SimpleState(id + 0xFF0000u, 10000f, "???");
    }
}

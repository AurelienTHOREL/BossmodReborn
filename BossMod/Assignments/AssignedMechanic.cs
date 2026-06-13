namespace BossMod.Assignments;

// Thin BossComponent base that wires a single IMechanicResolver into the AI.
// Per-fight code becomes tiny: implement a resolver, pass it here, done.
//
// Crucially, the autonomous movement (AddAIHints) and the human-facing overlay
// (DrawArenaForeground) both call the SAME resolver, so the hint a player sees and
// the spot the AI walks to can never drift apart.
public abstract class AssignedMechanic(BossModule module, IMechanicResolver resolver) : BossComponent(module)
{
    protected readonly IMechanicResolver Resolver = resolver;

    // override to false to suppress the on-arena marker (e.g. when another component already draws it)
    protected virtual bool DrawTargetMarker => true;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!Resolver.Active)
            return;
        var preset = AssignmentManager.PresetFor(Module);
        var plan = Resolver.Resolve(slot, actor, assignment, preset);
        AssignmentHintBridge.Apply(plan, hints);
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (!DrawTargetMarker || !Resolver.Active)
            return;
        var assignment = AssignmentFor(pcSlot);
        var preset = AssignmentManager.PresetFor(Module);
        var plan = Resolver.Resolve(pcSlot, pc, assignment, preset);
        if (plan.TargetPos is { } pos)
            Arena.AddCircle(pos, 0.7f, Colors.Safe, 2f);
    }

    public override void AddMovementHints(int slot, Actor actor, MovementHints movementHints)
    {
        if (!Resolver.Active)
            return;
        var assignment = AssignmentFor(slot);
        var preset = AssignmentManager.PresetFor(Module);
        var plan = Resolver.Resolve(slot, actor, assignment, preset);
        if (plan.TargetPos is { } pos)
            movementHints.Add(actor.Position, pos, Colors.Safe);
    }

    private PartyRolesConfig.Assignment AssignmentFor(int slot)
    {
        var members = WorldState.Party.Members;
        return slot >= 0 && slot < members.Length
            ? Service.Config.Get<PartyRolesConfig>()[members[slot].ContentId]
            : PartyRolesConfig.Assignment.Unassigned;
    }
}

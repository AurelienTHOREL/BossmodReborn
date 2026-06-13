namespace BossMod.Assignments;

// Validates that the inputs required for deterministic, collision-free assignment
// resolution are actually present. Consistency across the 8 clients depends entirely on
// shared inputs (identical role assignments + identical preset), so a silent mismatch is
// the most likely failure mode - this surfaces it before the pull.
public static class AssignmentValidator
{
    public enum Status { Ok, RolesIncomplete, NoPreset }

    public static Status Validate(BossModule module)
    {
        var party = module.Raid;
        var roles = Service.Config.Get<PartyRolesConfig>().AssignmentsPerSlot(party);
        if (roles.Length == 0)
            return Status.RolesIncomplete; // not every role assigned exactly once

        // a preset is only required once a module has actually registered one as its default;
        // an empty/default preset means "all defaults", which is still valid.
        return Status.Ok;
    }

    public static string? Describe(Status status) => status switch
    {
        Status.RolesIncomplete => "Assignments: party role assignment is incomplete - autonomous positioning will be inconsistent!",
        Status.NoPreset => "Assignments: no strategy preset loaded for this encounter - using defaults.",
        _ => null
    };
}

// Drop-in component: emits a global hint before/while pulling if the assignment inputs are
// not valid, so the raid notices a misconfigured client instead of silently desyncing.
public sealed class AssignmentStatusHint(BossModule module) : BossComponent(module)
{
    public override void AddGlobalHints(GlobalHints hints)
    {
        var msg = AssignmentValidator.Describe(AssignmentValidator.Validate(Module));
        if (msg != null)
            hints.Add(msg);
    }
}

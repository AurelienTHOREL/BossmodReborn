namespace BossMod.Assignments;

// Generic adapter: wraps a module's existing per-slot logic into an IMechanicResolver
// in one line, so any component can drive the AI through AssignmentHintBridge without
// implementing the interface by hand. This is the intended way to bridge an existing
// "compute each player's spot" mechanic (e.g. M12S P2 StagingAssignment) into the AI:
//
//   // 'staging' already knows PlayersBySlot[slot].Clock; map clock -> world position.
//   var resolver = new DelegateResolver(
//       () => staging.PlayersAssigned && mechanicActive,
//       (slot, actor, assignment, strat) =>
//       {
//           var clone = staging.PlayersBySlot[slot];
//           if (clone == null)
//               return PlayerPlan.None;
//           var pos = Arena.Center + radius * clone.Clock.Angle.ToDirection();
//           return new PlayerPlan(targetPos: pos, hard: true, activation: resolveAt);
//       });
//   // host component: class Foo(BossModule m) : AssignedMechanic(m, resolver) { }
public sealed class DelegateResolver(Func<bool> active, DelegateResolver.ResolveFunc resolve) : IMechanicResolver
{
    public delegate PlayerPlan ResolveFunc(int slot, Actor actor, PartyRolesConfig.Assignment assignment, StrategyPreset strat);

    public bool Active => active();

    public PlayerPlan Resolve(int slot, Actor actor, PartyRolesConfig.Assignment assignment, StrategyPreset strat)
        => resolve(slot, actor, assignment, strat);
}

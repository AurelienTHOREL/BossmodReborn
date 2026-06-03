using BossMod.AI;
using BossMod.Autorotation;
using BossMod.Pathfinding;

namespace BossMod.Dawntrail.Ultimate.UMAD;

sealed class UMADAI(RotationModuleManager manager, Actor player) : AIRotationModule(manager, player)
{
    public enum Track { Movement }
    public enum MovementStrategy { None, Pathfind, Explicit }

    public static RotationModuleDefinition Definition()
    {
        var res = new RotationModuleDefinition("AI Experiment", "Experimental encounter-specific rotation", "Encounter AI", "", RotationModuleQuality.WIP, new(~1ul), 100, 1, RotationModuleOrder.Movement, typeof(UMAD));
        res.Define(Track.Movement).As<MovementStrategy>("Movement", "Movement")
            .AddOption(MovementStrategy.None, "No automatic movement")
            .AddOption(MovementStrategy.Pathfind, "Use standard pathfinding to move")
            .AddOption(MovementStrategy.Explicit, "Move to specific point", supportedTargets: ActionTargets.Area);
        return res;
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        if (Bossmods.ActiveModule is not UMAD)
            return;
        var dest = strategy.Option(Track.Movement).As<MovementStrategy>() switch
        {
            MovementStrategy.Pathfind => PathfindPosition(),
            MovementStrategy.Explicit => ResolveTargetLocation(strategy.Option(Track.Movement).Value),
            _ => (WPos?)null,
        };
        SetForcedMovement(dest);
    }

    private WPos PathfindPosition()
    {
        var res = NavigationDecision.Build(NavigationContext, World.CurrentTime, Hints, Player, Speed());
        return res.Destination ?? Player.Position;
    }
}

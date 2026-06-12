using System.IO;

namespace BossMod.Assignments;

// Central lookup for the active StrategyPreset of an encounter.
//
// Determinism across clients comes from shared inputs, not networking: as long as
// every player has the same PartyRolesConfig and imports the same preset here, the
// pure resolvers produce identical plans for the whole party.
//
// Phase 1 keeps this in-memory (presets registered in code or loaded from a file).
// Phase 2 adds an authoring/sharing UI mirroring the cooldown-planner database.
public static class AssignmentManager
{
    // keyed by BossModule concrete type
    private static readonly Dictionary<Type, StrategyPreset> _presets = [];

    // register / replace the active preset for an encounter
    public static void Register(Type moduleType, StrategyPreset preset) => _presets[moduleType] = preset;

    // active preset for a running module; never null - falls back to an empty preset
    // (every resolver supplies its own per-mechanic fallback choice, so an empty
    // preset simply means "all defaults").
    public static StrategyPreset PresetFor(BossModule module)
    {
        var t = module.GetType();
        if (_presets.TryGetValue(t, out var p))
            return p;
        var empty = new StrategyPreset("Default", t);
        _presets[t] = empty;
        return empty;
    }

    // best-effort load of a preset file; registers it against the named module type if resolvable.
    public static bool TryLoadFile(string path)
    {
        try
        {
            var preset = StrategyPreset.FromJson(File.ReadAllText(path));
            if (preset == null || preset.EncounterModule.Length == 0)
                return false;
            var t = Type.GetType(preset.EncounterModule);
            if (t == null)
            {
                Service.Log($"[Assignments] preset '{preset.Name}' references unknown module '{preset.EncounterModule}'");
                return false;
            }
            _presets[t] = preset;
            return true;
        }
        catch (Exception e)
        {
            Service.Log($"[Assignments] failed to load preset file '{path}': {e.Message}");
            return false;
        }
    }
}

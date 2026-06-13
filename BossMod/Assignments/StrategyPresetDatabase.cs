using System.IO;

namespace BossMod.Assignments;

// On-disk store of shareable StrategyPresets (one JSON file per preset), mirroring the
// cooldown-planner's PlanDatabase. Loading a preset registers it with AssignmentManager so
// modules pick it up automatically. A raid lead authors one file and shares it; every player
// drops it into their assignments folder -> identical assignments on all 8 clients.
public sealed class StrategyPresetDatabase
{
    private readonly DirectoryInfo _store;
    public readonly List<StrategyPreset> Presets = [];

    public StrategyPresetDatabase(string rootPath)
    {
        _store = new DirectoryInfo(rootPath);
        if (!_store.Exists)
            _store.Create();
        Reload();
    }

    public void Reload()
    {
        Presets.Clear();
        foreach (var f in _store.EnumerateFiles("*.json"))
        {
            try
            {
                var preset = StrategyPreset.FromJson(File.ReadAllText(f.FullName));
                if (preset != null)
                {
                    Presets.Add(preset);
                    RegisterIfResolvable(preset);
                }
            }
            catch (Exception e)
            {
                Service.Log($"[Assignments] failed to load preset '{f.FullName}': {e.Message}");
            }
        }
    }

    public void Save(StrategyPreset preset)
    {
        var name = Sanitize(preset.Name.Length > 0 ? preset.Name : "preset");
        var path = Path.Combine(_store.FullName, name + ".json");
        File.WriteAllText(path, preset.ToJson());
        if (!Presets.Contains(preset))
            Presets.Add(preset);
        RegisterIfResolvable(preset);
    }

    private static void RegisterIfResolvable(StrategyPreset preset)
    {
        if (preset.EncounterModule.Length == 0)
            return;
        var t = Type.GetType(preset.EncounterModule);
        if (t != null)
            AssignmentManager.Register(t, preset);
        else
            Service.Log($"[Assignments] preset '{preset.Name}' references unknown module '{preset.EncounterModule}'");
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}

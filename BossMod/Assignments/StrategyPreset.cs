using System.Text.Json;

namespace BossMod.Assignments;

// A serializable, shareable container for "the raid's chosen strat" for one encounter.
// Generalizes the per-fight strategy enums (e.g. M12S P2's Rep1/Rep2/Rep3 strategy
// fields) into a single object that can be authored once and imported by every player,
// which is what makes assignment resolution identical across all 8 clients.
//
// Choices are kept as string->string so the format is forward-compatible: adding a new
// mechanic or a new option never breaks deserialization of older presets.
public sealed class StrategyPreset
{
    public string Name = "";

    // fully-qualified type name of the BossModule this preset applies to
    // (Type.FullName); used by AssignmentManager to look up the right preset.
    public string EncounterModule = "";

    // mechanic-key -> chosen option id
    public Dictionary<string, string> Choices = [];

    public StrategyPreset() { }

    public StrategyPreset(string name, Type encounterModule)
    {
        Name = name;
        EncounterModule = encounterModule.FullName ?? encounterModule.Name;
    }

    // read a chosen option for a mechanic, falling back to a default when unset
    public string Choice(string mechanic, string fallback) => Choices.GetValueOrDefault(mechanic, fallback);

    public StrategyPreset With(string mechanic, string choice)
    {
        Choices[mechanic] = choice;
        return this;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Serialization.BuildSerializationOptions());

    public static StrategyPreset? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StrategyPreset>(json, Serialization.BuildSerializationOptions());
        }
        catch (Exception e)
        {
            Service.Log($"[Assignments] failed to parse strategy preset: {e.Message}");
            return null;
        }
    }
}

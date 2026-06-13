using System.Threading.Tasks;

namespace BossMod.Assignments.FFLogs;

// Turns an FFLogs report into a StrategyPreset.
//
// The generic importer only handles fetching + fight selection; the encounter-specific
// inference ("this kill used Banana Codex; R1 took NW tower") lives in a per-encounter
// IEncounterStrategyMapper, registered once per fight. This mirrors how each fight already
// owns its own components/strategy enums - the mapper just reads the same mechanic outcomes
// from logged events instead of from the live world.
public interface IEncounterStrategyMapper
{
    // FFLogs encounter id this mapper understands
    uint EncounterId { get; }

    // concrete BossModule type the produced preset targets
    Type ModuleType { get; }

    // fill 'preset.Choices' from one kill; return false if the data was insufficient
    bool TryMap(FFLogsReport report, FFLogsFight kill, StrategyPreset preset);
}

public sealed class FFLogsStrategyImporter(IFFLogsClient client)
{
    private readonly Dictionary<uint, IEncounterStrategyMapper> _mappers = [];

    public void Register(IEncounterStrategyMapper mapper) => _mappers[mapper.EncounterId] = mapper;

    // Fetch a report and derive a preset for the first matching kill of 'encounterId'.
    // Returns null if the report can't be fetched, has no matching kill, or no mapper is
    // registered for that encounter.
    public async Task<StrategyPreset?> ImportAsync(string reportCode, uint encounterId, string presetName)
    {
        if (!_mappers.TryGetValue(encounterId, out var mapper))
        {
            Service.Log($"[Assignments/FFLogs] no strategy mapper registered for encounter {encounterId}");
            return null;
        }

        var report = await client.FetchReportAsync(reportCode);
        if (report == null)
            return null;

        var kill = FirstKill(report, encounterId);
        if (kill == null)
        {
            Service.Log($"[Assignments/FFLogs] report '{reportCode}' has no kill for encounter {encounterId}");
            return null;
        }

        var preset = new StrategyPreset(presetName, mapper.ModuleType);
        if (!mapper.TryMap(report, kill, preset))
        {
            Service.Log($"[Assignments/FFLogs] mapper for encounter {encounterId} could not infer a strategy");
            return null;
        }
        return preset;
    }

    private static FFLogsFight? FirstKill(FFLogsReport report, uint encounterId)
    {
        for (var i = 0; i < report.Fights.Count; ++i)
        {
            var f = report.Fights[i];
            if (f.EncounterId == encounterId && f.Kill)
                return f;
        }
        return null;
    }
}

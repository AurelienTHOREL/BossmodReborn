namespace BossMod.Assignments.FFLogs;

// Minimal data model for the subset of an FFLogs (v2 GraphQL) report we need to infer a
// raid's strategy. These are plain DTOs so they can be deserialized from the GraphQL
// response and consumed by FFLogsStrategyImporter without pulling in any HTTP dependency.
//
// A "strategy" here means: from a clean kill, recover the choices a raid actually made
// (which light party went where, clock-spot per role, who soaked which tower, etc.) and
// encode them as a StrategyPreset so the assignment resolvers reproduce that same plan.

public sealed class FFLogsReport
{
    public string Code = "";
    public List<FFLogsFight> Fights = [];
    // flat event list for the report window (debuff applications, casts, positions, ...)
    public List<FFLogsEvent> Events = [];
    public List<FFLogsActor> Actors = [];
}

public sealed class FFLogsFight
{
    public int Id;
    public uint EncounterId;     // FFLogs encounter id (maps to a BossModule)
    public bool Kill;
    public int StartTime;        // ms relative to report
    public int EndTime;
    // ids into FFLogsReport.Actors for the 8 players in this pull
    public List<int> PlayerActorIds = [];
}

public sealed class FFLogsActor
{
    public int Id;
    public string Name = "";
    public string Subtype = "";  // FFLogs job string, e.g. "Warrior", "WhiteMage"
    public Role Role = Role.None;
}

// A single timeline event; only the fields useful for strategy inference are modelled.
public sealed class FFLogsEvent
{
    public int Timestamp;        // ms relative to report
    public string Type = "";     // "applydebuff", "cast", "damage", ...
    public uint AbilityGameID;
    public int SourceActorId;
    public int TargetActorId;
    public float X;              // FFLogs map coords (1/100 yalm); convert at use site
    public float Y;
}

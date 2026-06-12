using System.Threading.Tasks;

namespace BossMod.Assignments.FFLogs;

// Credentials for the FFLogs v2 API (OAuth2 client-credentials flow).
// Obtain a client id/secret from https://www.fflogs.com/api/clients/ .
public readonly record struct FFLogsCredentials(string ClientId, string ClientSecret);

// Fetches reports from FFLogs. The live implementation (TODO) performs the OAuth2 token
// exchange and the GraphQL POST, then deserializes into the FFLogsReport DTOs. It is kept
// behind an interface so the importer/mapper logic is fully testable offline and so the
// network call respects the environment's outbound policy + user-provided credentials.
public interface IFFLogsClient
{
    // report code is the segment in a log url: fflogs.com/reports/<CODE>
    Task<FFLogsReport?> FetchReportAsync(string reportCode);
}

// The GraphQL query used by the live client. Kept here so it is reviewable alongside the
// DTOs it populates. Tables/events are paginated in the real API; the live client must
// loop on nextPageTimestamp.
public static class FFLogsQueries
{
    public const string Report = """
        query($code: String!) {
          reportData {
            report(code: $code) {
              code
              fights(killType: Kills) { id encounterID kill startTime endTime friendlyPlayers }
              masterData { actors(type: "Player") { id name subType } }
              events(dataType: All, useAbilityIDs: false) {
                data
                nextPageTimestamp
              }
            }
          }
        }
        """;
}

// Stub used until credentials/networking are wired up; returns null so callers degrade to
// configured defaults instead of crashing.
public sealed class NotConfiguredFFLogsClient : IFFLogsClient
{
    public Task<FFLogsReport?> FetchReportAsync(string reportCode)
    {
        Service.Log("[Assignments/FFLogs] client not configured (no credentials / networking); returning no report");
        return Task.FromResult<FFLogsReport?>(null);
    }
}

using System.Text.Json.Serialization;

namespace Firepit.Core.Jobs;

/// <summary>
/// Why a run fired. Stored on the run record so the history can show e.g.
/// "manual" vs. "scheduled" runs distinctly.
/// </summary>
public enum JobTrigger
{
    [JsonStringEnumMemberName("scheduled")] Scheduled,
    [JsonStringEnumMemberName("manual")]    Manual,
    [JsonStringEnumMemberName("catchup")]   Catchup,
}

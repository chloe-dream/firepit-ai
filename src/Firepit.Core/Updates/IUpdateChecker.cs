namespace Firepit.Core.Updates;

/// <summary>
/// Looks up whether a newer Firepit release is available. Implementations are
/// transport-only — they never download or install. The shell owns the
/// download + installer hand-off so <c>Firepit.Core</c> stays free of process
/// and Windows specifics.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Returns the newest release if it is strictly greater than
    /// <paramref name="current"/>, otherwise null. Never throws for ordinary
    /// network / parse failures — those yield null so the caller can treat
    /// "couldn't check" the same as "nothing new."
    /// </summary>
    Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct);
}

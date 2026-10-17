namespace CaloriesTracker.Core;

/// <summary>What to do after comparing the local copy with the cloud copy.</summary>
public enum SyncAction
{
    /// <summary>Both sides already match.</summary>
    None,

    /// <summary>Replace the local copy with the cloud copy.</summary>
    UseRemote,

    /// <summary>Upload the local copy to the cloud.</summary>
    PushLocal,
}

/// <summary>
/// Last-write-wins decision between the local and cloud copies of the user's data.
/// The whole document is synced as one unit, which is fine for a single user on a few devices.
/// </summary>
public static class SyncResolver
{
    /// <param name="localUpdatedAt">Last change of the local copy, or null if it was never saved.</param>
    /// <param name="localHasData">True when the local copy holds at least one weight or meal.</param>
    /// <param name="remoteUpdatedAt">Last change of the cloud copy, or null if none exists yet.</param>
    /// <param name="firstSyncOnDevice">
    /// True right after signing in on this device. If the cloud already holds data, it wins,
    /// so signing in on a second device never overwrites the main one.
    /// </param>
    public static SyncAction Resolve(
        DateTimeOffset? localUpdatedAt,
        bool localHasData,
        DateTimeOffset? remoteUpdatedAt,
        bool firstSyncOnDevice)
    {
        if (remoteUpdatedAt is null)
        {
            // Nothing in the cloud yet: seed it, unless there is nothing to send.
            return localHasData || localUpdatedAt is not null ? SyncAction.PushLocal : SyncAction.None;
        }

        if (firstSyncOnDevice || localUpdatedAt is null)
        {
            return SyncAction.UseRemote;
        }

        if (localUpdatedAt > remoteUpdatedAt)
        {
            return SyncAction.PushLocal;
        }

        return localUpdatedAt < remoteUpdatedAt ? SyncAction.UseRemote : SyncAction.None;
    }
}

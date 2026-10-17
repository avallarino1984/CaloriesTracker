using CaloriesTracker.Core;

namespace CaloriesTracker.Core.Tests;

public class SyncResolverTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(5);

    [Fact]
    public void EmptyCloud_WithLocalData_PushesLocal()
    {
        Assert.Equal(SyncAction.PushLocal, SyncResolver.Resolve(Earlier, true, null, firstSyncOnDevice: true));
    }

    [Fact]
    public void EmptyCloud_AndNothingLocal_DoesNothing()
    {
        Assert.Equal(SyncAction.None, SyncResolver.Resolve(null, false, null, firstSyncOnDevice: true));
    }

    [Fact]
    public void FirstSignIn_CloudHasData_CloudWinsEvenIfLocalIsNewer()
    {
        // Signing in on the Mac must not overwrite what the iPhone already uploaded.
        Assert.Equal(SyncAction.UseRemote, SyncResolver.Resolve(Later, true, Earlier, firstSyncOnDevice: true));
    }

    [Fact]
    public void LocalNewer_PushesLocal()
    {
        Assert.Equal(SyncAction.PushLocal, SyncResolver.Resolve(Later, true, Earlier, firstSyncOnDevice: false));
    }

    [Fact]
    public void CloudNewer_UsesRemote()
    {
        Assert.Equal(SyncAction.UseRemote, SyncResolver.Resolve(Earlier, true, Later, firstSyncOnDevice: false));
    }

    [Fact]
    public void SameTimestamp_DoesNothing()
    {
        Assert.Equal(SyncAction.None, SyncResolver.Resolve(Earlier, true, Earlier, firstSyncOnDevice: false));
    }

    [Fact]
    public void NeverSavedLocally_UsesRemote()
    {
        Assert.Equal(SyncAction.UseRemote, SyncResolver.Resolve(null, false, Earlier, firstSyncOnDevice: false));
    }
}

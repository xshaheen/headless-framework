// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Internal;
using tusdotnet.Interfaces;

namespace Tests.Endpoint;

public sealed class TusSnapshotRefreshingLockProviderTests : TestBase
{
    private readonly ITusFileLock _innerLock = Substitute.For<ITusFileLock>();
    private readonly RecordingStore _store = new();
    private readonly TusSnapshotRefreshingLockProvider _sut;

    public TusSnapshotRefreshingLockProviderTests()
    {
        var inner = Substitute.For<ITusFileLockProvider>();
        inner.AquireLock("upload-1").Returns(_innerLock);
        _sut = new TusSnapshotRefreshingLockProvider(inner, _store);
    }

    [Fact]
    public async Task should_forget_the_requests_snapshot_once_it_holds_the_lock()
    {
        // given
        _innerLock.Lock().Returns(true);
        var fileLock = await _sut.AquireLock("upload-1");

        // when
        var locked = await fileLock.Lock();

        // then
        locked.Should().BeTrue();
        await _innerLock.Received(1).Lock();
        _store.Forgotten.Should().Equal("upload-1");
    }

    [Fact]
    public async Task should_keep_the_requests_snapshot_when_the_lock_is_held_elsewhere()
    {
        // given
        _innerLock.Lock().Returns(false);
        var fileLock = await _sut.AquireLock("upload-1");

        // when
        var locked = await fileLock.Lock();

        // then
        locked.Should().BeFalse();
        _store.Forgotten.Should().BeEmpty();
    }

    [Fact]
    public async Task should_release_the_inner_lock()
    {
        // given
        var fileLock = await _sut.AquireLock("upload-1");

        // when
        await fileLock.ReleaseIfHeld();

        // then
        await _innerLock.Received(1).ReleaseIfHeld();
    }

    /// <summary>The store is internal, so it is faked by hand; it records every upload it is told to forget.</summary>
    private sealed class RecordingStore : ITusRequestSnapshotStore
    {
        public List<string> Forgotten { get; } = [];

        public void ForgetRequestSnapshot(string fileId)
        {
            Forgotten.Add(fileId);
        }
    }
}

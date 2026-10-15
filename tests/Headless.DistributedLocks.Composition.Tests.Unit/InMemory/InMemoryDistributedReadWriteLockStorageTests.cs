// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.InMemory;

public sealed class InMemoryDistributedReadWriteLockStorageTests : TestBase
{
    private readonly FakeTimeProvider _fakeTime = new();
    private readonly WallClockSteppingTimeProvider _timeProvider;
    private readonly InMemoryDistributedReadWriteLockStorage _storage;

    public InMemoryDistributedReadWriteLockStorageTests()
    {
        _timeProvider = new WallClockSteppingTimeProvider(_fakeTime);
        _storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
    }

    [Fact]
    public async Task should_keep_write_lease_until_monotonic_deadline_when_wall_clock_steps()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");
        await _storage.TryAcquireWriteAsync(
            resource,
            writerId,
            DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        _timeProvider.WallClockOffset = TimeSpan.FromHours(1);
        var validAfterForwardStep = await _storage.ValidateWriteAsync(resource, writerId, AbortToken);

        _fakeTime.Advance(TimeSpan.FromSeconds(6));
        var validAfterTtl = await _storage.ValidateWriteAsync(resource, writerId, AbortToken);

        validAfterForwardStep.Should().BeTrue();
        validAfterTtl.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_entry_when_reader_and_writer_released()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");

        await _storage.TryAcquireReadAsync(resource, "reader-1", TimeSpan.FromSeconds(30), AbortToken);
        await _storage.ReleaseReadAsync(resource, "reader-1", AbortToken);
        var entriesAfterReadRelease = _storage.EntryCount;

        await _storage.TryAcquireWriteAsync(
            resource,
            writerId,
            DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            AbortToken
        );
        await _storage.ReleaseWriteAsync(resource, writerId, AbortToken);

        entriesAfterReadRelease.Should().Be(0);
        _storage.EntryCount.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_entry_when_only_writer_waiting_marker_remains()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");

        await _storage.TryAcquireReadAsync(resource, "reader-1", TimeSpan.FromSeconds(30), AbortToken);
        var queued = await _storage.TryAcquireWriteAsync(
            resource,
            writerId,
            DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            AbortToken
        );
        await _storage.ReleaseReadAsync(resource, "reader-1", AbortToken);

        var entriesWithMarker = _storage.EntryCount;
        var readerBlocked = !await _storage.TryAcquireReadAsync(
            resource,
            "reader-2",
            TimeSpan.FromSeconds(30),
            AbortToken
        );

        await _storage.ReleaseWriteAsync(resource, writerId, AbortToken);

        queued.Should().BeFalse();
        entriesWithMarker.Should().Be(1);
        readerBlocked.Should().BeTrue("the surviving marker still enforces writer preference");
        _storage.EntryCount.Should().Be(0);
    }
}

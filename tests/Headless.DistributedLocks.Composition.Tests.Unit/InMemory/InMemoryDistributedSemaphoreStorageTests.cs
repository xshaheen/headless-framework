// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.InMemory;

public sealed class InMemoryDistributedSemaphoreStorageTests : TestBase
{
    private readonly FakeTimeProvider _fakeTime = new();
    private readonly WallClockSteppingTimeProvider _timeProvider;
    private readonly InMemoryDistributedSemaphoreStorage _storage;

    public InMemoryDistributedSemaphoreStorageTests()
    {
        _timeProvider = new WallClockSteppingTimeProvider(_fakeTime);
        _storage = new InMemoryDistributedSemaphoreStorage(_timeProvider);
    }

    [Fact]
    public async Task should_keep_slot_until_monotonic_deadline_when_wall_clock_steps()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await _storage.TryAcquireAsync(resource, "slot-1", 1, TimeSpan.FromSeconds(5), AbortToken);

        _timeProvider.WallClockOffset = TimeSpan.FromHours(1);
        var validAfterForwardStep = await _storage.ValidateAsync(resource, "slot-1", AbortToken);

        _fakeTime.Advance(TimeSpan.FromSeconds(6));
        var validAfterTtl = await _storage.ValidateAsync(resource, "slot-1", AbortToken);

        validAfterForwardStep.Should().BeTrue();
        validAfterTtl.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_entry_and_issue_greater_token_when_all_slots_released()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        var first = await _storage.TryAcquireAsync(resource, "slot-1", 2, TimeSpan.FromSeconds(30), AbortToken);
        var second = await _storage.TryAcquireAsync(resource, "slot-2", 2, TimeSpan.FromSeconds(30), AbortToken);

        await _storage.ReleaseAsync(resource, "slot-1", AbortToken);
        await _storage.ReleaseAsync(resource, "slot-2", AbortToken);
        var entriesAfterRelease = _storage.EntryCount;

        var third = await _storage.TryAcquireAsync(resource, "slot-3", 2, TimeSpan.FromSeconds(30), AbortToken);

        entriesAfterRelease.Should().Be(0);
        third.Acquired.Should().BeTrue();
        third
            .FencingToken!.Value.Should()
            .BeGreaterThan(first.FencingToken!.Value)
            .And.BeGreaterThan(second.FencingToken!.Value);
    }

    [Fact]
    public async Task should_remove_entry_when_expired_slot_is_next_accessed()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await _storage.TryAcquireAsync(resource, "slot-1", 1, TimeSpan.FromSeconds(5), AbortToken);

        _fakeTime.Advance(TimeSpan.FromSeconds(6));
        var count = await _storage.GetCountAsync(resource, AbortToken);

        count.Should().Be(0);
        _storage.EntryCount.Should().Be(0);
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests.ReaderWriterLocks;

[Collection<RedisTestFixture>]
public sealed class RedisReaderWriterLockStorageTests(RedisTestFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public async Task should_allow_multiple_concurrent_readers()
    {
        // given
        var resource = _NewResource();
        var lockIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid().ToString("N")).ToArray();

        // when
        var results = await Task.WhenAll(
            lockIds.Select(lockId =>
                fixture.ReaderWriterLockStorage
                    .TryAcquireReadAsync(resource, lockId, TimeSpan.FromMinutes(1), AbortToken)
                    .AsTask()
            )
        );

        // then
        results.Should().OnlyContain(x => x);
        (await fixture.ReaderWriterLockStorage.GetReaderCountAsync(resource, AbortToken)).Should().Be(10);
    }

    [Fact]
    public async Task should_block_reader_when_writer_waiting_marker_exists()
    {
        // given
        var resource = _NewResource();
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = RedisDistributedReaderWriterLockStorage.GetWaitingId(writerId);
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await db.StringSetAsync(_WriterKey(resource), waitingId, TimeSpan.FromMinutes(1));

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        result.Should().BeFalse();
        (await fixture.ReaderWriterLockStorage.GetReaderCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_claim_writer_waiting_marker_when_readers_are_present()
    {
        // given
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = RedisDistributedReaderWriterLockStorage.GetWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(resource, readerId, TimeSpan.FromMinutes(1), AbortToken);

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        result.Should().BeFalse();
        var stored = await fixture.ConnectionMultiplexer.GetDatabase().StringGetAsync(_WriterKey(resource));
        stored.ToString().Should().Be(waitingId);
        (await fixture.ReaderWriterLockStorage.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_promote_own_waiting_writer_after_readers_drain()
    {
        // given
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = RedisDistributedReaderWriterLockStorage.GetWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(resource, readerId, TimeSpan.FromMinutes(1), AbortToken);
        await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        result.Should().BeTrue();
        (await fixture.ReaderWriterLockStorage.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
        (await fixture.ReaderWriterLockStorage.ValidateWriteAsync(resource, writerId, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_extend_read_ttl_without_shortening()
    {
        // given
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(resource, readerId, TimeSpan.FromSeconds(10), AbortToken);
        var before = await db.KeyTimeToLiveAsync(_ReaderKey(resource));

        // when
        var result = await fixture.ReaderWriterLockStorage.TryExtendReadAsync(
            resource,
            readerId,
            TimeSpan.FromSeconds(1),
            AbortToken
        );
        var after = await db.KeyTimeToLiveAsync(_ReaderKey(resource));

        // then
        result.Should().BeTrue();
        after.Should().NotBeNull();
        before.Should().NotBeNull();
        after!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task should_release_writer_or_waiting_marker_only_for_matching_id()
    {
        // given
        var resource = _NewResource();
        var writerId = Guid.NewGuid().ToString("N");
        var wrongWriterId = Guid.NewGuid().ToString("N");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await db.StringSetAsync(_WriterKey(resource), RedisDistributedReaderWriterLockStorage.GetWaitingId(writerId));

        // when
        await fixture.ReaderWriterLockStorage.ReleaseWriteAsync(resource, wrongWriterId, AbortToken);
        var afterWrongRelease = await db.StringGetAsync(_WriterKey(resource));
        await fixture.ReaderWriterLockStorage.ReleaseWriteAsync(resource, writerId, AbortToken);
        var existsAfterCorrectRelease = await db.KeyExistsAsync(_WriterKey(resource));

        // then
        afterWrongRelease.ToString().Should().Be(RedisDistributedReaderWriterLockStorage.GetWaitingId(writerId));
        existsAfterCorrectRelease.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_resource_names_with_braces()
    {
        // when
        var act = () =>
            fixture.ReaderWriterLockStorage
                .TryAcquireReadAsync("tenant:{bad}", Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(1), AbortToken)
                .AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private string _NewResource()
    {
        return $"rw:{Faker.Random.AlphaNumeric(10)}";
    }

    private static RedisKey _WriterKey(string resource)
    {
        return "{" + resource + "}:writer";
    }

    private static RedisKey _ReaderKey(string resource)
    {
        return "{" + resource + "}:readers";
    }
}

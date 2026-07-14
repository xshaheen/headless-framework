// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Redis.Testing;
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
            lockIds.Select(leaseId =>
                fixture
                    .ReaderWriterLockStorage.TryAcquireReadAsync(resource, leaseId, TimeSpan.FromMinutes(1), AbortToken)
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
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);
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
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
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
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );
        await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
        );
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
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
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromSeconds(10),
            AbortToken
        );
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
        await db.StringSetAsync(_WriterKey(resource), DistributedLockCoreHelpers.GetWriterWaitingId(writerId));

        // when
        await fixture.ReaderWriterLockStorage.ReleaseWriteAsync(resource, wrongWriterId, AbortToken);
        var afterWrongRelease = await db.StringGetAsync(_WriterKey(resource));
        await fixture.ReaderWriterLockStorage.ReleaseWriteAsync(resource, writerId, AbortToken);
        var existsAfterCorrectRelease = await db.KeyExistsAsync(_WriterKey(resource));

        // then
        afterWrongRelease.ToString().Should().Be(DistributedLockCoreHelpers.GetWriterWaitingId(writerId));
        existsAfterCorrectRelease.Should().BeFalse();
    }

    [Fact]
    public async Task should_succeed_try_acquire_write_on_clean_state()
    {
        // given
        var resource = _NewResource();
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        (await fixture.ReaderWriterLockStorage.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
        (await fixture.ReaderWriterLockStorage.ValidateWriteAsync(resource, writerId, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_for_try_acquire_read_when_real_writer_holds_key()
    {
        // given - plant a real writer (not the :_WRITERWAITING marker) at the writer key.
        var resource = _NewResource();
        var writerId = Guid.NewGuid().ToString("N");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await db.StringSetAsync(_WriterKey(resource), writerId, TimeSpan.FromMinutes(1));

        // when
        var result = await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_for_try_extend_read_when_reader_id_not_in_set()
    {
        // given - empty reader set; the caller's id was never granted.
        var resource = _NewResource();

        // when
        var result = await fixture.ReaderWriterLockStorage.TryExtendReadAsync(
            resource,
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_be_idempotent_for_release_read()
    {
        // given - never acquired, then release twice. Both must be no-ops.
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");

        // when & then - neither call may throw
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);

        // and after granting + releasing twice, the second is still a no-op.
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);
        (await fixture.ReaderWriterLockStorage.GetReaderCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_grant_only_one_concurrent_try_acquire_write()
    {
        // given
        var resource = _NewResource();
        const int contenderCount = 10;
        var writers = Enumerable.Range(0, contenderCount).Select(_ => Guid.NewGuid().ToString("N")).ToArray();

        // when - race writers against a clean state.
        var results = await Task.WhenAll(
            writers.Select(id =>
                fixture
                    .ReaderWriterLockStorage.TryAcquireWriteAsync(
                        resource,
                        id,
                        DistributedLockCoreHelpers.GetWriterWaitingId(id),
                        TimeSpan.FromMinutes(1),
                        cancellationToken: AbortToken
                    )
                    .AsTask()
            )
        );

        // then - exactly one winner.
        results.Count(x => x).Should().Be(1);
    }

    [Fact]
    public async Task should_extend_write_only_when_id_matches_and_refresh_ttl()
    {
        // given
        var resource = _NewResource();
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromSeconds(10),
            cancellationToken: AbortToken
        );

        // when - wrong id is rejected.
        var wrongResult = await fixture.ReaderWriterLockStorage.TryExtendWriteAsync(
            resource,
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromSeconds(30),
            AbortToken
        );

        // and - correct id refreshes TTL.
        var rightResult = await fixture.ReaderWriterLockStorage.TryExtendWriteAsync(
            resource,
            writerId,
            TimeSpan.FromSeconds(60),
            AbortToken
        );
        var ttl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(_WriterKey(resource));

        // then
        wrongResult.Should().BeFalse();
        rightResult.Should().BeTrue();
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task should_report_is_read_locked_true_only_when_readers_present()
    {
        // given
        var resource = _NewResource();
        (await fixture.ReaderWriterLockStorage.IsReadLockedAsync(resource, AbortToken)).Should().BeFalse();

        // when
        var readerId = Guid.NewGuid().ToString("N");
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        (await fixture.ReaderWriterLockStorage.IsReadLockedAsync(resource, AbortToken))
            .Should()
            .BeTrue();

        // and after release returns to false
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, readerId, AbortToken);
        (await fixture.ReaderWriterLockStorage.IsReadLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_report_is_write_locked_true_only_for_real_writer_not_waiting_marker()
    {
        // given - plant the waiting marker first. IsWriteLocked must still be false.
        var resource = _NewResource();
        var writerId = Guid.NewGuid().ToString("N");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await db.StringSetAsync(_WriterKey(resource), DistributedLockCoreHelpers.GetWriterWaitingId(writerId));

        (await fixture.ReaderWriterLockStorage.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();

        // when - promote to a real writer.
        await db.StringSetAsync(_WriterKey(resource), writerId);

        // then
        (await fixture.ReaderWriterLockStorage.IsWriteLockedAsync(resource, AbortToken))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_report_exact_reader_count()
    {
        // given
        var resource = _NewResource();
        var lockIds = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid().ToString("N")).ToArray();

        // when
        foreach (var id in lockIds)
        {
            await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
                resource,
                id,
                TimeSpan.FromMinutes(1),
                AbortToken
            );
        }

        // then
        (await fixture.ReaderWriterLockStorage.GetReaderCountAsync(resource, AbortToken))
            .Should()
            .Be(7);

        // and after removing two, count drops.
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, lockIds[0], AbortToken);
        await fixture.ReaderWriterLockStorage.ReleaseReadAsync(resource, lockIds[1], AbortToken);
        (await fixture.ReaderWriterLockStorage.GetReaderCountAsync(resource, AbortToken)).Should().Be(5);
    }

    [Fact]
    public async Task should_return_false_for_validate_read_after_redis_ttl_expires()
    {
        // given - short TTL so Redis can drop the key inside test budget.
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromSeconds(1),
            AbortToken
        );
        (await fixture.ReaderWriterLockStorage.ValidateReadAsync(resource, readerId, AbortToken)).Should().BeTrue();

        // when - wait past the TTL.
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        // then
        (await fixture.ReaderWriterLockStorage.ValidateReadAsync(resource, readerId, AbortToken))
            .Should()
            .BeFalse();
        (await fixture.ReaderWriterLockStorage.GetReaderCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_extend_read_when_writer_waiting_marker_present()
    {
        // given - a reader is admitted, then a queued writer plants a marker.
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );
        await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
        );

        // when - the reader tries to extend.
        var extended = await fixture.ReaderWriterLockStorage.TryExtendReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then - the writer-preference guarantee forces extend to return false.
        extended.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_marker_alive_when_second_writer_arrives_and_first_cancels()
    {
        // given - first writer queues behind a reader and plants the marker.
        var resource = _NewResource();
        var readerId = Guid.NewGuid().ToString("N");
        var writer1 = Guid.NewGuid().ToString("N");
        var writer2 = Guid.NewGuid().ToString("N");
        var waiting1 = DistributedLockCoreHelpers.GetWriterWaitingId(writer1);
        var waiting2 = DistributedLockCoreHelpers.GetWriterWaitingId(writer2);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            readerId,
            TimeSpan.FromMinutes(1),
            AbortToken
        );
        await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writer1,
            waiting1,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
        );

        // when - second writer arrives (sees the first's marker, refreshes with its own waitingId).
        var w2Queued = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writer2,
            waiting2,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
        );

        // and the first writer cancels (release clears only the matching id; second writer's id remains).
        await fixture.ReaderWriterLockStorage.ReleaseWriteAsync(resource, writer1, AbortToken);

        // then - the marker should still be present (now owned by writer2), so a new reader is blocked.
        w2Queued.Should().BeFalse();
        var newReader = await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromMinutes(1),
            AbortToken
        );
        newReader.Should().BeFalse();
    }

    [Fact]
    public async Task should_prune_zombie_reader_when_writer_acquires_after_per_entry_expiry()
    {
        // given - reader added with a 1s TTL but never released; writer arrives after expiry.
        var resource = _NewResource();
        var zombieReader = Guid.NewGuid().ToString("N");
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);
        await fixture.ReaderWriterLockStorage.TryAcquireReadAsync(
            resource,
            zombieReader,
            TimeSpan.FromSeconds(1),
            AbortToken
        );
        // Wait past the zombie's per-entry expiry; the Redis HASH still holds the field
        // because the per-entry expiry is enforced inside the Lua script, not by Redis itself.
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        // when - the writer acquires. The script must prune the expired entry before checking HLEN.
        var result = await fixture.ReaderWriterLockStorage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromMinutes(1),
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        (await fixture.ReaderWriterLockStorage.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_resource_names_with_braces()
    {
        // when
        var act = () =>
            fixture
                .ReaderWriterLockStorage.TryAcquireReadAsync(
                    "tenant:{bad}",
                    Guid.NewGuid().ToString("N"),
                    TimeSpan.FromMinutes(1),
                    AbortToken
                )
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

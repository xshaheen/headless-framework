// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics;

namespace Tests.ConnectionScopedLocks;

public sealed class ConnectionScopedDistributedLockProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeConnectionScopedLockStorage _storage = new();
    private readonly FakeReleaseSignal _releaseSignal = new();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();

    private long _lockIdCounter = 1000;

    [Fact]
    public async Task should_release_acquired_storage_handle_when_fencing_token_source_fails()
    {
        var provider = _CreateProvider(fencingTokenSource: new ThrowingFencingTokenSource());
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () => await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _storage.ReleaseCount.Should().Be(1);
        _releaseSignal.PublishCount.Should().Be(1);
    }

    [Fact]
    public async Task should_jitter_polling_fallback_across_waits_when_waiting_for_contention()
    {
        // 50 contended attempts before success forces 50 jittered waits on the same resource.
        const int contendedAttempts = 50;
        var pollingFallback = TimeSpan.FromSeconds(7);

        for (var i = 0; i < contendedAttempts; i++)
        {
            _storage.AcquireResults.Enqueue(false);
        }

        _storage.AcquireResults.Enqueue(true);

        var provider = _CreateProvider(pollingFallback: pollingFallback);
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(10) },
            AbortToken
        );

        handle.Should().NotBeNull();

        var waits = _releaseSignal.WaitDurations;
        waits.Should().HaveCount(contendedAttempts);

        // Every wait must stay inside the [0.8, 1.2) jitter band around the base fallback.
        waits.Should().AllSatisfy(wait =>
            wait.Should().BeGreaterThanOrEqualTo(pollingFallback * 0.8).And.BeLessThanOrEqualTo(pollingFallback * 1.2)
        );

        // Prove jitter is actually applied: a no-jitter implementation would return exactly the base
        // every time. Require distinct values that fall on BOTH sides of the base fallback so the test
        // cannot pass against an impl that just returns a constant (even a constant != base).
        waits.Distinct().Should().HaveCountGreaterThan(1);
        waits.Should().Contain(wait => wait < pollingFallback);
        waits.Should().Contain(wait => wait > pollingFallback);
    }

    [Fact]
    public async Task should_emit_a_lock_acquire_activity_with_the_resource_tag_when_a_lock_is_acquired()
    {
        // given (listen to the distributed-locks activity source)
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Headless.DistributedLocks",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        // when (an uncontended acquire — the storage fake grants immediately)
        await using var handle = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // then (the connection-scoped provider now emits the same lock.acquire activity as the regular provider).
        // Filter by this test's unique resource tag so activities emitted by test classes running in parallel — the
        // listener is process-wide — cannot pollute the assertion.
        activities
            .Should()
            .ContainSingle(a =>
                a.OperationName == "lock.acquire" && (string?)a.GetTagItem("headless.lock.resource") == resource
            );
    }

    private ConnectionScopedDistributedLockProvider _CreateProvider(
        IFencingTokenSource? fencingTokenSource = null,
        TimeSpan? pollingFallback = null
    )
    {
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new ConnectionScopedDistributedLockProvider(
            _storage,
            _releaseSignal,
            new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<ConnectionScopedDistributedLockProvider>(),
            fencingTokenSource,
            pollingFallback
        );
    }

    private sealed class FakeConnectionScopedLockStorage : IConnectionScopedLockStorage
    {
        public Queue<bool> AcquireResults { get; } = new();

        public int ReleaseCount { get; private set; }

        public ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
            string resource,
            string lockId,
            bool isShared,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquired = AcquireResults.Count == 0 || AcquireResults.Dequeue();

            return ValueTask.FromResult<ConnectionScopedLockHandle?>(
                acquired ? new ConnectionScopedLockHandle(resource, lockId, ReleaseAsync, CancellationToken.None) : null
            );
        }

        public ValueTask ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsLockedAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(false);
        }

        public ValueTask<long> GetLocksCountAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0L);
        }

        public ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<IReadOnlyList<LockInfo>>([]);
        }

        public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0L);
        }
    }

    private sealed class FakeReleaseSignal : IReleaseSignal
    {
        public List<TimeSpan> WaitDurations { get; } = [];

        public int PublishCount { get; private set; }

        public ValueTask WaitAsync(
            string resource,
            TimeSpan pollingFallback,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitDurations.Add(pollingFallback);

            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishCount++;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingFencingTokenSource : IFencingTokenSource
    {
        public ValueTask<long?> NextAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("fencing failed");
        }
    }
}

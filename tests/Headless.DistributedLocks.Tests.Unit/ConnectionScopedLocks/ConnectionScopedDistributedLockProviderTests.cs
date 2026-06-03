// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

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
    public async Task should_use_configured_polling_fallback_when_waiting_for_contention()
    {
        var pollingFallback = TimeSpan.FromSeconds(7);
        _storage.AcquireResults.Enqueue(false);
        _storage.AcquireResults.Enqueue(true);

        var provider = _CreateProvider(pollingFallback: pollingFallback);
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(1) },
            AbortToken
        );

        handle.Should().NotBeNull();

        // The polling fallback is jittered by [0.8, 1.2) before each wait so that many waiters on the
        // same resource do not wake in lockstep. Assert the single wait lands within that band rather
        // than pinning the exact value.
        var wait = _releaseSignal.WaitDurations.Should().ContainSingle().Subject;
        wait.Should().BeGreaterThanOrEqualTo(pollingFallback * 0.8).And.BeLessThanOrEqualTo(pollingFallback * 1.2);
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

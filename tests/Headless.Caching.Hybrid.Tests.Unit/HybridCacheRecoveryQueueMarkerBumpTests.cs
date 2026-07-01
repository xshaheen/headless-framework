// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Direct tests for <see cref="HybridCacheRecoveryQueue"/> MarkerBump semantics: generation-aware supersession
/// (<see cref="HybridCacheRecoveryQueue.OnSuccessfulMarkerBump"/>) and conflict-drop exemption.
/// </summary>
public sealed class HybridCacheRecoveryQueueMarkerBumpTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private HybridCacheRecoveryQueue _CreateQueue() =>
        new(new HybridCacheOptions { EnableAutoRecovery = true }, _timeProvider, NullLogger.Instance);

    private void _EnqueueMarkerBump(HybridCacheRecoveryQueue queue, string key, DateTimeOffset enqueuedAt) =>
        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.MarkerBump,
            _timeProvider.GetUtcNow() + queue.DefaultRetention,
            _ => new ValueTask<HybridCacheRecoveryReplayOutcome>(HybridCacheRecoveryReplayOutcome.Replayed),
            enqueuedAt: enqueuedAt
        );

    [Fact]
    public void on_successful_marker_bump_keeps_a_queued_newer_generation()
    {
        using var queue = _CreateQueue();
        const string key = "\0hybrid-marker:tag:orders";
        var tNew = _timeProvider.GetUtcNow();
        var tOld = tNew - TimeSpan.FromSeconds(10);

        // A newer-generation bump is queued (its live L2 write failed).
        _EnqueueMarkerBump(queue, key, tNew);
        queue.Count.Should().Be(1);

        // An OLDER live write succeeds — it must NOT drop the queued newer bump (raise-only left the newer marker
        // unwritten on L2; dropping it would lose the newer invalidation).
        queue.OnSuccessfulMarkerBump(key, tOld);
        queue.Count.Should().Be(1);

        // A live write at the queued generation (or newer) supersedes it.
        queue.OnSuccessfulMarkerBump(key, tNew);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public void on_incoming_flush_all_does_not_drop_a_queued_marker_bump()
    {
        using var queue = _CreateQueue();
        const string key = "\0hybrid-marker:clear";

        _EnqueueMarkerBump(queue, key, _timeProvider.GetUtcNow());
        queue.Count.Should().Be(1);

        // A foreign FlushAll with a strictly newer timestamp would drop older value-op items, but MarkerBump is
        // exempt (raise-only/idempotent).
        queue.OnIncomingInvalidation(
            new CacheInvalidationMessage
            {
                InstanceId = "other",
                FlushAll = true,
                Timestamp = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(1),
            }
        );

        queue.Count.Should().Be(1);
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheRecoveryQueueConcurrencyTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_run_only_one_recovery_pass_when_process_calls_overlap()
    {
        using var queue = _CreateQueue();
        var replayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReplay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        queue.Enqueue(
            "key",
            HybridCacheRecoveryKind.SetEntry,
            _timeProvider.GetUtcNow() + TimeSpan.FromMinutes(5),
            async cancellationToken =>
            {
                Interlocked.Increment(ref attempts);
                replayStarted.TrySetResult();
                await releaseReplay.Task.WaitAsync(cancellationToken);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );

        var firstPass = queue.ProcessAsync(AbortToken);
        try
        {
            await replayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            await queue.ProcessAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            attempts.Should().Be(1);
            queue.Count.Should().Be(1, "the in-flight replay still owns the queued item");
        }
        finally
        {
            releaseReplay.TrySetResult();
            await firstPass.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }

        attempts.Should().Be(1);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_leave_items_queued_when_recovery_pass_is_cancelled_before_replay()
    {
        using var queue = _CreateQueue();
        var attempts = 0;
        queue.Enqueue(
            "key",
            HybridCacheRecoveryKind.Remove,
            _timeProvider.GetUtcNow() + TimeSpan.FromMinutes(5),
            _ =>
            {
                Interlocked.Increment(ref attempts);
                return new ValueTask<HybridCacheRecoveryReplayOutcome>(HybridCacheRecoveryReplayOutcome.Replayed);
            }
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await queue.ProcessAsync(cancellation.Token);

        attempts.Should().Be(0);
        queue.Count.Should().Be(1, "cancellation must not lose pending recovery work");

        await queue.ProcessAsync(AbortToken);

        attempts.Should().Be(1, "a cancelled pass must release the process gate");
        queue.Count.Should().Be(0);
    }

    private HybridCacheRecoveryQueue _CreateQueue()
    {
        return new(
            new HybridCacheOptions { EnableAutoRecovery = true, AutoRecoveryMaxRetries = 2 },
            _timeProvider,
            NullLogger.Instance
        );
    }
}

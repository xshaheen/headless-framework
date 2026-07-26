// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Processor;

public sealed class RetryProcessorLaneIsolationTests : TestBase
{
    private static readonly (MessageType Direction, MessageLane Lane)[] _Quadrants =
    [
        (MessageType.Publish, MessageLane.Bus),
        (MessageType.Publish, MessageLane.Queue),
        (MessageType.Subscribe, MessageLane.Bus),
        (MessageType.Subscribe, MessageLane.Queue),
    ];

    [Fact]
    public void adaptive_backpressure_changes_only_the_target_quadrant()
    {
        var sut = _CreateProcessor();

        sut.AdjustPollingInterval(MessageType.Subscribe, MessageLane.Queue, enqueued: 1, skippedCircuitOpen: 9);

        sut.GetCurrentIntervalForTest(MessageType.Subscribe, MessageLane.Queue).Should().Be(TimeSpan.FromSeconds(2));
        foreach (var (direction, lane) in _Quadrants.Where(key => key != (MessageType.Subscribe, MessageLane.Queue)))
        {
            sut.GetCurrentIntervalForTest(direction, lane).Should().Be(TimeSpan.FromSeconds(1));
        }

        sut.CurrentPollingInterval.Should().Be(TimeSpan.FromSeconds(2));
        sut.IsBackedOff.Should().BeTrue();
    }

    [Fact]
    public async Task aggregate_monitor_uses_maximum_and_reset_clears_every_quadrant()
    {
        var sut = _CreateProcessor();
        var intervals = new[]
        {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(5),
        };

        for (var index = 0; index < _Quadrants.Length; index++)
        {
            var (direction, lane) = _Quadrants[index];
            sut.SetCurrentIntervalForTest(direction, lane, intervals[index]);
        }

        sut.CurrentPollingInterval.Should().Be(TimeSpan.FromSeconds(5));
        sut.IsBackedOff.Should().BeTrue();

        await sut.ResetBackpressureAsync(AbortToken);

        foreach (var (direction, lane) in _Quadrants)
        {
            sut.GetCurrentIntervalForTest(direction, lane).Should().Be(TimeSpan.FromSeconds(1));
        }

        sut.CurrentPollingInterval.Should().Be(TimeSpan.FromSeconds(1));
        sut.IsBackedOff.Should().BeFalse();
    }

    [Fact]
    public void active_overdue_quadrant_keeps_a_positive_wait_floor()
    {
        var sut = _CreateProcessor();
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.MarkQuadrantDueForTest(MessageType.Publish, MessageLane.Bus);
        sut.SetQuadrantActiveTaskForTest(MessageType.Publish, MessageLane.Bus, active.Task);

        var delay = sut.GetQuadrantDelayForTest(MessageType.Publish, MessageLane.Bus, DateTimeOffset.UtcNow);

        delay.Should().Be(TimeSpan.FromSeconds(1));
        active.SetResult();
    }

    private static MessageNeedToRetryProcessor _CreateProcessor()
    {
        return new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions()),
            Options.Create(
                new RetryProcessorOptions
                {
                    AdaptivePolling = true,
                    BaseInterval = TimeSpan.FromSeconds(1),
                    MaxPollingInterval = TimeSpan.FromMinutes(1),
                    CircuitOpenRateThreshold = 0.5,
                }
            ),
            NullLogger<MessageNeedToRetryProcessor>.Instance,
            Substitute.For<IDispatcher>(),
            Substitute.For<IDistributedLock>()
        );
    }
}

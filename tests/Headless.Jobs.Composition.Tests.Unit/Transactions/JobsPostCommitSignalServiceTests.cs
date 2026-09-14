// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transactions;

/// <summary>
/// The hosted worker that drains coordinated post-commit signals: bounded per-signal processing, log-and-continue,
/// drop-on-full, and the shutdown drain. The manager-side callback shape is covered by
/// <see cref="JobsManagerCoordinatedRoutingTests" />.
/// </summary>
public sealed class JobsPostCommitSignalServiceTests : TestBase
{
    // Every wait on worker progress is bounded so a regression fails the test instead of hanging the run.
    private static readonly TimeSpan _WaitTimeout = TimeSpan.FromSeconds(30);
    private readonly List<JobsPostCommitSignalService> _services = [];

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var service in _services)
        {
            await service.StopAsync(AbortToken);
            service.Dispose();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_read_the_clock_when_the_signal_is_processed_not_when_it_was_enqueued()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        var (service, _) = _CreateService(timeProvider);
        var observed = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        service
            .TrySignal(
                new TestPostCommitSignal(
                    "clock",
                    (now, _) =>
                    {
                        observed.TrySetResult(now);
                        return Task.CompletedTask;
                    }
                )
            )
            .Should()
            .BeTrue();

        // The commit happened five seconds after the enqueue; the worker must see the later instant.
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await service.StartAsync(AbortToken);

        (await observed.Task.WaitAsync(_WaitTimeout, AbortToken)).Should().Be(timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task should_log_a_faulting_signal_and_process_the_next_one()
    {
        var (service, logger) = _CreateService(new FakeTimeProvider());
        var boom = new InvalidOperationException("side effect boom");
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TrySignal(new TestPostCommitSignal("first", (_, _) => Task.FromException(boom)));
        service.TrySignal(
            new TestPostCommitSignal(
                "second",
                (_, _) =>
                {
                    second.TrySetResult();
                    return Task.CompletedTask;
                }
            )
        );

        await service.StartAsync(AbortToken);

        await second.Task.WaitAsync(_WaitTimeout, AbortToken);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && ReferenceEquals(e.Exception, boom));
    }

    [Fact]
    public async Task should_abandon_a_signal_that_ignores_cancellation_after_the_deadline_and_process_the_next_one()
    {
        var timeProvider = new FakeTimeProvider();
        var (service, logger) = _CreateService(timeProvider);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TrySignal(
            new TestPostCommitSignal(
                "hung",
                (_, _) =>
                {
                    started.TrySetResult();
                    return neverCompletes.Task;
                }
            )
        );
        service.TrySignal(
            new TestPostCommitSignal(
                "second",
                (_, _) =>
                {
                    second.TrySetResult();
                    return Task.CompletedTask;
                }
            )
        );

        await service.StartAsync(AbortToken);
        await started.Task.WaitAsync(_WaitTimeout, AbortToken);
        await FakeClock.AdvanceUntilAsync(
            timeProvider,
            JobsPostCommitSignalService.SignalDeadline + TimeSpan.FromTicks(1),
            second.Task,
            AbortToken
        );

        neverCompletes.Task.IsCompleted.Should().BeFalse();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);

        // Release the abandoned task; the worker never retained it.
        neverCompletes.SetResult();
    }

    [Fact]
    public async Task should_log_a_late_fault_from_an_abandoned_signal()
    {
        var timeProvider = new FakeTimeProvider();
        var (service, logger) = _CreateService(timeProvider);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TrySignal(
            new TestPostCommitSignal(
                "late",
                (_, _) =>
                {
                    started.TrySetResult();
                    return lateFault.Task;
                }
            )
        );

        await service.StartAsync(AbortToken);
        await started.Task.WaitAsync(_WaitTimeout, AbortToken);
        await FakeClock.AdvanceUntilAsync(
            timeProvider,
            JobsPostCommitSignalService.SignalDeadline + TimeSpan.FromTicks(1),
            logger.WaitForAsync(e => e.Level == LogLevel.Warning && e.Exception == null, AbortToken),
            AbortToken
        );

        var boom = new InvalidOperationException("late boom");
        lateFault.SetException(boom);

        var entry = await logger.WaitForAsync(e => e.Exception is not null, AbortToken);
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().Contain(boom);
    }

    [Fact]
    public void should_drop_the_incoming_signal_with_a_warning_and_a_counter_when_the_channel_is_full()
    {
        var (service, logger) = _CreateService(new FakeTimeProvider());
        using var drops = new DroppedSignalCounter();

        for (var i = 0; i < JobsPostCommitSignalService.Capacity; i++)
        {
            service.TrySignal(TestPostCommitSignal.NoOp()).Should().BeTrue();
        }

        service.TrySignal(TestPostCommitSignal.NoOp("overflow")).Should().BeFalse();

        service.PendingCount.Should().Be(JobsPostCommitSignalService.Capacity);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
        drops.Measurements.Should().ContainSingle().Which.Should().Be((1L, "full"));
    }

    [Fact]
    public async Task should_drain_signals_already_present_on_stop_and_reject_new_ones()
    {
        var (service, logger) = _CreateService(new FakeTimeProvider());
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProcessed = false;
        var thirdProcessed = false;
        await service.StartAsync(AbortToken);
        service.TrySignal(
            new TestPostCommitSignal(
                "first",
                (_, _) =>
                {
                    firstStarted.TrySetResult();
                    return releaseFirst.Task;
                }
            )
        );
        await firstStarted.Task.WaitAsync(_WaitTimeout, AbortToken);
        service
            .TrySignal(
                new TestPostCommitSignal(
                    "second",
                    (_, _) =>
                    {
                        secondProcessed = true;
                        return Task.CompletedTask;
                    }
                )
            )
            .Should()
            .BeTrue();

        var stop = service.StopAsync(AbortToken);

        service
            .TrySignal(
                new TestPostCommitSignal(
                    "third",
                    (_, _) =>
                    {
                        thirdProcessed = true;
                        return Task.CompletedTask;
                    }
                )
            )
            .Should()
            .BeFalse();
        releaseFirst.SetResult();
        await stop.WaitAsync(_WaitTimeout, AbortToken);

        secondProcessed.Should().BeTrue();
        thirdProcessed.Should().BeFalse();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
    }

    [Fact]
    public async Task should_process_or_report_a_signal_accepted_while_idle_when_stop_follows_without_yielding()
    {
        // Two races share this shape. base.StartAsync runs the loop through Task.Run bound to the stopping token,
        // so a stop before the pool picks the worker up cancels it without the loop ever reading; and once the
        // loop runs, a stop that cancelled a token observed by its idle wait would skip an item queued between the
        // inner loop running dry and the next wait (two adjacent statements on the worker thread, so that exact
        // window cannot be pinned from a test). Either way TrySignal already reported the signal accepted, so it
        // must run or be reported as dropped; it must never vanish.
        var (service, logger) = _CreateService(new FakeTimeProvider());
        using var drops = new DroppedSignalCounter();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await service.StartAsync(AbortToken);

        service
            .TrySignal(
                new TestPostCommitSignal(
                    "idle",
                    (_, _) =>
                    {
                        processed.TrySetResult();
                        return Task.CompletedTask;
                    }
                )
            )
            .Should()
            .BeTrue();
        var stop = service.StopAsync(AbortToken);

        await stop.WaitAsync(_WaitTimeout, AbortToken);
        service.PendingCount.Should().Be(0);

        if (processed.Task.IsCompleted)
        {
            logger.Entries.Should().BeEmpty();
            drops.Measurements.Should().BeEmpty();
        }
        else
        {
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
            drops.Measurements.Should().ContainSingle().Which.Should().Be((1L, "stopping"));
        }
    }

    [Fact]
    public async Task should_report_a_signal_still_queued_when_stopped_before_activation()
    {
        var (service, logger) = _CreateService(new FakeTimeProvider(), new JobsActivationBarrier());
        using var drops = new DroppedSignalCounter();
        var processed = false;
        await service.StartAsync(AbortToken);
        service
            .TrySignal(
                new TestPostCommitSignal(
                    "queued",
                    (_, _) =>
                    {
                        processed = true;
                        return Task.CompletedTask;
                    }
                )
            )
            .Should()
            .BeTrue();

        await service.StopAsync(AbortToken).WaitAsync(_WaitTimeout, AbortToken);

        processed.Should().BeFalse();
        service.PendingCount.Should().Be(0);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
        drops.Measurements.Should().ContainSingle().Which.Should().Be((1L, "stopping"));
    }

    [Fact]
    public async Task should_abandon_the_drain_when_the_shutdown_budget_is_exhausted()
    {
        var (service, logger) = _CreateService(new FakeTimeProvider());
        using var drops = new DroppedSignalCounter();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProcessed = false;
        await service.StartAsync(AbortToken);
        service.TrySignal(
            new TestPostCommitSignal(
                "first",
                async (_, cancellationToken) =>
                {
                    firstStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        firstCancelled.TrySetResult();
                        throw;
                    }
                }
            )
        );
        await firstStarted.Task.WaitAsync(_WaitTimeout, AbortToken);
        service.TrySignal(
            new TestPostCommitSignal(
                "second",
                (_, _) =>
                {
                    secondProcessed = true;
                    return Task.CompletedTask;
                }
            )
        );
        using var shutdownBudget = new CancellationTokenSource();

        var stop = service.StopAsync(shutdownBudget.Token);
        await shutdownBudget.CancelAsync();

        await stop.WaitAsync(_WaitTimeout, AbortToken);
        await firstCancelled.Task.WaitAsync(_WaitTimeout, AbortToken);
        await service.ExecuteTask!.WaitAsync(_WaitTimeout, AbortToken);
        secondProcessed.Should().BeFalse();
        service.PendingCount.Should().Be(0);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception == null);
        drops.Measurements.Should().ContainSingle().Which.Should().Be((1L, "stopping"));
    }

    [Fact]
    public async Task should_stay_closed_when_activation_failed()
    {
        var barrier = new JobsActivationBarrier();
        barrier.MarkFailed(new InvalidOperationException("activation failed"));
        var (service, logger) = _CreateService(new FakeTimeProvider(), barrier);
        var processed = false;

        await service.StartAsync(AbortToken);
        await service.ExecuteTask!.WaitAsync(_WaitTimeout, AbortToken);
        service.TrySignal(
            new TestPostCommitSignal(
                "late",
                (_, _) =>
                {
                    processed = true;
                    return Task.CompletedTask;
                }
            )
        );

        processed.Should().BeFalse();
        logger
            .Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task should_exit_when_stopped_before_activation()
    {
        var (service, _) = _CreateService(new FakeTimeProvider(), new JobsActivationBarrier());

        await service.StartAsync(AbortToken);
        await service.StopAsync(AbortToken);

        service.ExecuteTask!.IsCompleted.Should().BeTrue();
    }

    private (JobsPostCommitSignalService Service, CapturingLogger<JobsPostCommitSignalService> Logger) _CreateService(
        TimeProvider timeProvider,
        JobsActivationBarrier? barrier = null
    )
    {
        var logger = new CapturingLogger<JobsPostCommitSignalService>();
        var service = new JobsPostCommitSignalService(barrier ?? TestActivationBarrier.Opened(), timeProvider, logger);
        _services.Add(service);

        return (service, logger);
    }

    private sealed class DroppedSignalCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();

        public List<(long Value, string Reason)> Measurements { get; } = [];

        public DroppedSignalCounter()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    ReferenceEquals(instrument.Meter, JobsDiagnostics.Meter)
                    && string.Equals(
                        instrument.Name,
                        JobsMetrics.PostCommitSignalsDroppedName,
                        StringComparison.Ordinal
                    )
                )
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, measurement, tags, _) =>
                {
                    var reason = "";

                    foreach (var tag in tags)
                    {
                        if (string.Equals(tag.Key, JobsMetrics.TagDropReason, StringComparison.Ordinal))
                        {
                            reason = tag.Value?.ToString() ?? "";
                        }
                    }

                    lock (_gate)
                    {
                        Measurements.Add((measurement, reason));
                    }
                }
            );
            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}

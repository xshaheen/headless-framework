// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Processor;

public sealed class InfiniteRetryProcessorTests : TestBase
{
    [Fact]
    public async Task should_retry_with_exponential_backoff_after_processor_failures()
    {
        // given
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new ControlledTimeProvider();
        using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
        var inner = new SequenceProcessor(
            _ => Task.FromException(new InvalidOperationException("first failure")),
            _ => Task.FromException(new InvalidOperationException("second failure")),
            async _ =>
            {
                await cancellation.CancelAsync();
            }
        );
        var sut = new InfiniteRetryProcessor(inner, LoggerFactory);

        // when
#pragma warning disable CA2025 // The test awaits the processor task before provider/cancellation disposal.
        var run = sut.ProcessAsync(context);
#pragma warning restore CA2025

        await _WaitUntilAsync(() => inner.Calls == 1 && timeProvider.DelayCount == 1, AbortToken);
        var firstDelay = timeProvider.DelayAt(0);

        await timeProvider.FireNextTimerAsync();

        await _WaitUntilAsync(() => inner.Calls == 2 && timeProvider.DelayCount == 2, AbortToken);
        var secondDelay = timeProvider.DelayAt(1);

        await timeProvider.FireNextTimerAsync();
        await _WaitUntilAsync(() => inner.Calls == 3, AbortToken);
        await run.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);

        // then
        firstDelay.Should().Be(TimeSpan.FromSeconds(1));
        secondDelay.Should().Be(TimeSpan.FromSeconds(2));
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task should_reset_backoff_after_successful_iteration()
    {
        // given
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new ControlledTimeProvider();
        using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
        var inner = new SequenceProcessor(
            _ => Task.FromException(new InvalidOperationException("first failure")),
            _ => Task.CompletedTask,
            _ => Task.FromException(new InvalidOperationException("second failure after recovery")),
            async _ =>
            {
                await cancellation.CancelAsync();
            }
        );
        var sut = new InfiniteRetryProcessor(inner, LoggerFactory);

        // when
#pragma warning disable CA2025 // The test awaits the processor task before provider/cancellation disposal.
        var run = sut.ProcessAsync(context);
#pragma warning restore CA2025

        await _WaitUntilAsync(() => inner.Calls == 1 && timeProvider.DelayCount == 1, AbortToken);
        await timeProvider.FireNextTimerAsync();

        await _WaitUntilAsync(() => inner.Calls == 3 && timeProvider.DelayCount == 2, AbortToken);
        var secondFailureDelay = timeProvider.DelayAt(1);

        await timeProvider.FireNextTimerAsync();
        await _WaitUntilAsync(() => inner.Calls == 4, AbortToken);
        await run.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);

        // then
        secondFailureDelay.Should().Be(TimeSpan.FromSeconds(1));
        inner.Calls.Should().Be(4);
    }

    [Fact]
    public async Task should_exit_when_context_is_cancelled_after_operation_canceled_exception()
    {
        // given
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new ControlledTimeProvider();
        using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
        var inner = new SequenceProcessor(async _ =>
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        });
        var sut = new InfiniteRetryProcessor(inner, LoggerFactory);

        // when
        await sut.ProcessAsync(context).WaitAsync(TimeSpan.FromSeconds(1), AbortToken);

        // then
        inner.Calls.Should().Be(1);
        timeProvider.DelayCount.Should().Be(0);
    }

    private static async Task _WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private sealed class SequenceProcessor(params Func<ProcessingContext, Task>[] actions) : IProcessor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task ProcessAsync(ProcessingContext context)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call > actions.Length)
            {
                throw new InvalidOperationException($"Unexpected processor call {call}.");
            }

            return actions[call - 1](context);
        }

        public override string ToString() => nameof(SequenceProcessor);
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<TimeSpan> _delays = [];
        private readonly Queue<ControlledTimer> _timers = [];

        public int DelayCount
        {
            get
            {
                lock (_gate)
                {
                    return _delays.Count;
                }
            }
        }

        public TimeSpan DelayAt(int index)
        {
            lock (_gate)
            {
                return _delays[index];
            }
        }

        public async ValueTask FireNextTimerAsync()
        {
            ControlledTimer timer;

            lock (_gate)
            {
                if (!_timers.TryDequeue(out timer!))
                {
                    throw new InvalidOperationException("No pending timer is available to fire.");
                }
            }

            timer.Fire();
            await timer.DisposeAsync();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state);

            lock (_gate)
            {
                _delays.Add(dueTime);
                _timers.Enqueue(timer);
            }

            return timer;
        }
    }

    private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
            _disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }
    }
}

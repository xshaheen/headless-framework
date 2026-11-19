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
        await using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
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
        var run = sut.ProcessAsync(context);

        await timeProvider.WaitForDelaysAsync(1);
        var firstDelay = timeProvider.DelayAt(0);

        await timeProvider.FireNextTimerAsync();

        await timeProvider.WaitForDelaysAsync(2);
        var secondDelay = timeProvider.DelayAt(1);

        await timeProvider.FireNextTimerAsync();
        await run.Bounded();

        // then: each wait is the doubled base delay plus 0-25% jitter
        firstDelay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        firstDelay.Should().BeLessThan(TimeSpan.FromSeconds(1.25));
        secondDelay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        secondDelay.Should().BeLessThan(TimeSpan.FromSeconds(2.5));
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task should_reset_backoff_after_successful_iteration()
    {
        // given
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new ControlledTimeProvider();
        await using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
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
        var run = sut.ProcessAsync(context);

        await timeProvider.WaitForDelaysAsync(1);
        await timeProvider.FireNextTimerAsync();

        await timeProvider.WaitForDelaysAsync(2);
        var secondFailureDelay = timeProvider.DelayAt(1);

        await timeProvider.FireNextTimerAsync();
        await run.Bounded();

        // then: the post-recovery wait restarts from the initial 1s (plus 0-25% jitter)
        secondFailureDelay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        secondFailureDelay.Should().BeLessThan(TimeSpan.FromSeconds(1.25));
        inner.Calls.Should().Be(4);
    }

    [Fact]
    public async Task should_exit_when_context_is_cancelled_after_operation_canceled_exception()
    {
        // given
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new ControlledTimeProvider();
        await using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
        var inner = new SequenceProcessor(async _ =>
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        });
        var sut = new InfiniteRetryProcessor(inner, LoggerFactory);

        // when
        await sut.ProcessAsync(context).Bounded();

        // then
        inner.Calls.Should().Be(1);
        timeProvider.DelayCount.Should().Be(0);
    }

    [Fact]
    public async Task should_back_off_after_a_cancellation_the_processor_raised_while_the_context_runs()
    {
        // given — a timeout inside the processor (not the host stopping) surfaces as OperationCanceledException.
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new ControlledTimeProvider();
        await using var context = new ProcessingContext(provider, timeProvider, cancellation.Token);
        var inner = new SequenceProcessor(
            _ => Task.FromException(new OperationCanceledException("command timeout")),
            async _ =>
            {
                await cancellation.CancelAsync();
            }
        );
        var sut = new InfiniteRetryProcessor(inner, LoggerFactory);

        // when
        var run = sut.ProcessAsync(context);
        _ = await Task.WhenAny(timeProvider.WaitForDelaysAsync(1), run);

        // then — it waits like any other failure instead of re-entering the processor at once.
        timeProvider.DelayCount.Should().Be(1, "a cancellation while the context runs is a failure to back off from");
        timeProvider.DelayAt(0).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        inner.Calls.Should().Be(1);

        await timeProvider.FireNextTimerAsync();
        await run.Bounded();
        inner.Calls.Should().Be(2);
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

        public override string ToString()
        {
            return nameof(SequenceProcessor);
        }
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<TimeSpan> _delays = [];
        private readonly Queue<ControlledTimer> _timers = [];
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];

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

        /// <summary>Completes once the processor has armed <paramref name="count"/> waits in total.</summary>
        public Task WaitForDelaysAsync(int count)
        {
            lock (_gate)
            {
                if (_delays.Count >= count)
                {
                    return Task.CompletedTask;
                }

                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, signal));
                return signal.Task.Bounded();
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

            List<TaskCompletionSource>? ready = null;

            lock (_gate)
            {
                _delays.Add(dueTime);
                _timers.Enqueue(timer);
                foreach (var (count, signal) in _waiters.Where(waiter => waiter.Count <= _delays.Count).ToList())
                {
                    (ready ??= []).Add(signal);
                    _waiters.Remove((count, signal));
                }
            }

            ready?.ForEach(signal => signal.TrySetResult());

            return timer;
        }
    }

    private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return true;
        }

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

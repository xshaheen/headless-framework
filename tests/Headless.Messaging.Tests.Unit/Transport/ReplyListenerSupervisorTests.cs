// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transport;

/// <summary>
/// The supervisor keeps a reply channel open for the listener's whole life: it hands out the address a pass reports
/// ready, withdraws it when the pass ends, backs off before the next pass, and stops the pass when the listener closes.
/// </summary>
public sealed class ReplyListenerSupervisorTests : TestBase
{
    private const string _Address = "headless.reply.0f8fad5bd9cb469fa16570867728950e";
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(10);

    private readonly FakeReplyListener _listener = new();
    private readonly TimerRecordingTimeProvider _clock = new();
    private readonly RecordingLogger _logger = new();
    private readonly ScriptedPasses _passes = new();
    private readonly ReplyListenerSupervisor _supervisor;

    public ReplyListenerSupervisorTests()
    {
        // The minimum jitter pins every delay to the low edge of its band: 1s, then 1.5s, 3s, ...
        _supervisor = new ReplyListenerSupervisor(
            "Test",
            _listener,
            _passes.ServeOnceAsync,
            _clock,
            _logger,
            new MinimumRandom()
        );
    }

    [Fact]
    public async Task should_hand_out_the_address_once_the_pass_is_ready()
    {
        // given
        _supervisor.Start();
        var pass = await _passes.NextAsync();
        var wait = _supervisor.WaitForAddressAsync(AbortToken).AsTask();
        wait.IsCompleted.Should().BeFalse();

        // when
        _supervisor.Ready(_Address);

        // then
        (await wait.WaitAsync(_Wait, AbortToken))
            .Should()
            .Be(_Address);
        _logger.EventNames.Should().Equal("ReplyListenerReady");

        await _supervisor.DisposeAsync();
        pass.Released.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_withdraw_the_address_and_back_off_before_the_next_pass_when_the_channel_is_lost()
    {
        // given
        _supervisor.Start();
        var first = await _passes.NextAsync();
        _supervisor.Ready(_Address);

        // when
        first.Outcome.SetResult("the subscription ended");

        // then — the next pass waits exactly the backoff
        var delay = await _clock.NextTimerAsync();
        delay.Should().Be(TimeSpan.FromSeconds(1));
        _supervisor.WaitForAddressAsync(AbortToken).IsCompleted.Should().BeFalse();

        _clock.Advance(delay - TimeSpan.FromTicks(1));
        _passes.Started.Should().Be(1);
        _clock.Advance(TimeSpan.FromTicks(1));
        await _passes.NextAsync();

        var lost = _logger.Entries.Single(e =>
            string.Equals(e.EventName, "ReplyListenerLost", StringComparison.Ordinal)
        );
        lost.Level.Should().Be(LogLevel.Warning);
        lost.State["Transport"].Should().Be("Test");
        lost.State["ReplyAddress"].Should().Be(_Address);
        lost.State["Reason"].Should().Be("the subscription ended");
        lost.State["RetryDelay"].Should().Be(TimeSpan.FromSeconds(1));

        await _supervisor.DisposeAsync();
    }

    [Fact]
    public async Task should_double_the_delay_across_failed_passes_and_restart_it_once_a_pass_is_ready()
    {
        // given
        _supervisor.Start();
        var delays = new List<TimeSpan>();

        // when — two passes fail before ready, then one is ready and is lost
        foreach (var ready in new[] { false, false, true })
        {
            var pass = await _passes.NextAsync();
            if (ready)
            {
                _supervisor.Ready(_Address);
                pass.Outcome.SetResult("the subscription ended");
            }
            else
            {
                pass.Outcome.SetException(new InvalidOperationException("broker unreachable"));
            }

            var delay = await _clock.NextTimerAsync();
            delays.Add(delay);
            _clock.Advance(delay);
        }

        await _passes.NextAsync();

        // then
        delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1));

        var failures = _logger
            .Entries.Where(e => string.Equals(e.EventName, "ReplyListenerFailed", StringComparison.Ordinal))
            .ToList();
        failures.Should().HaveCount(2);
        failures.Should().AllSatisfy(e => e.Exception.Should().BeOfType<InvalidOperationException>());
        failures.Select(e => e.State["RetryDelay"]).Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5));

        await _supervisor.DisposeAsync();
    }

    [Fact]
    public async Task should_stop_the_pass_and_fail_waiting_callers_when_disposed()
    {
        // given
        _supervisor.Start();
        var pass = await _passes.NextAsync();
        var wait = _supervisor.WaitForAddressAsync(AbortToken).AsTask();

        // when
        await _supervisor.DisposeAsync();

        // then — the pass released its broker objects before the close returned, and nothing reports a loss
        pass.ClosingToken.IsCancellationRequested.Should().BeTrue();
        pass.Released.Task.IsCompleted.Should().BeTrue();
        _supervisor.IsClosed.Should().BeTrue();
        _supervisor.ClosingToken.IsCancellationRequested.Should().BeTrue();
        _logger.EventNames.Should().BeEmpty();

        (await wait.Awaiting(t => t).Should().ThrowAsync<ObjectDisposedException>())
            .Which.ObjectName.Should()
            .Be(nameof(FakeReplyListener));
        (
            await _supervisor
                .Awaiting(s => s.WaitForAddressAsync(AbortToken))
                .Should()
                .ThrowAsync<ObjectDisposedException>()
        )
            .Which.ObjectName.Should()
            .Be(typeof(FakeReplyListener).FullName);
    }

    [Fact]
    public async Task should_stop_during_the_backoff_when_disposed()
    {
        // given
        _supervisor.Start();
        var pass = await _passes.NextAsync();
        pass.Outcome.SetException(new InvalidOperationException("broker unreachable"));
        await _clock.NextTimerAsync();

        // when
        await _supervisor.DisposeAsync().AsTask().WaitAsync(_Wait, AbortToken);

        // then
        _passes.Started.Should().Be(1);
    }

    [Fact]
    public async Task should_return_from_every_concurrent_close_only_after_the_pass_released()
    {
        // given
        _supervisor.Start();
        var pass = await _passes.NextAsync();
        pass.HoldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // when
        var first = _supervisor.DisposeAsync().AsTask();
        var second = _supervisor.DisposeAsync().AsTask();
        await pass.ReleaseStarted.Task.WaitAsync(_Wait, AbortToken);

        // then
        first.IsCompleted.Should().BeFalse();
        second.IsCompleted.Should().BeFalse();

        pass.HoldRelease.SetResult();
        await Task.WhenAll(first, second).WaitAsync(_Wait, AbortToken);
        pass.Released.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_close_a_supervisor_that_never_started()
    {
        // when
        await _supervisor.DisposeAsync();

        // then
        _passes.Started.Should().Be(0);
        await _supervisor
            .Awaiting(s => s.WaitForAddressAsync(AbortToken))
            .Should()
            .ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>A serve-once callback whose passes the test ends by hand.</summary>
    private sealed class ScriptedPasses
    {
        private readonly Channel<Pass> _started = Channel.CreateUnbounded<Pass>();
        private int _count;

        public int Started => Volatile.Read(ref _count);

        public async Task<string> ServeOnceAsync(CancellationToken closingToken)
        {
            var pass = new Pass(closingToken);
            Interlocked.Increment(ref _count);
            _started.Writer.TryWrite(pass);

            try
            {
                return await pass.Outcome.Task.WaitAsync(closingToken);
            }
            finally
            {
                // Stands in for unsubscribing or closing the connection.
                pass.ReleaseStarted.TrySetResult();
                if (pass.HoldRelease is not null)
                {
                    await pass.HoldRelease.Task;
                }

                pass.Released.TrySetResult();
            }
        }

        public async Task<Pass> NextAsync()
        {
            return await _started.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(_Wait, AbortToken);
        }
    }

    private sealed class Pass(CancellationToken closingToken)
    {
        public CancellationToken ClosingToken { get; } = closingToken;

        public TaskCompletionSource<string> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? HoldRelease { get; set; }
    }

    /// <summary>A fake clock that reports the due time of every timer the backoff starts.</summary>
    private sealed class TimerRecordingTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider _inner = new();
        private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();

        public override long TimestampFrequency => _inner.TimestampFrequency;

        public override DateTimeOffset GetUtcNow()
        {
            return _inner.GetUtcNow();
        }

        public override long GetTimestamp()
        {
            return _inner.GetTimestamp();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            _timers.Writer.TryWrite(dueTime);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            _inner.Advance(delta);
        }

        public async Task<TimeSpan> NextTimerAsync()
        {
            return await _timers.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(_Wait, AbortToken);
        }
    }

    private sealed class MinimumRandom : Random
    {
        public override int Next(int minValue, int maxValue)
        {
            return minValue;
        }
    }

    private sealed class FakeReplyListener : IReplyListener
    {
        public ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string? EventName,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> State
    );

    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public IReadOnlyList<string?> EventNames => [.. _entries.Select(e => e.EventName)];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            _entries.Enqueue(
                new LogEntry(logLevel, eventId.Name, exception, values.ToDictionary(p => p.Key, p => p.Value))
            );
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }
    }
}

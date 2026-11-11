// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.LiveQueries;
using Headless.LiveQueries.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Drives the hub the way the family's other packages do: a run that returns bytes with recorded row filters, and a
/// write set that carries row changes. No public entry point feeds either yet.
/// </summary>
public sealed class LiveQueryHubTests : TestBase
{
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_run_again_when_a_changed_row_passes_the_row_filter()
    {
        // given - a query that read the todos of owner 3
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3);
        await using var updates = _Hub(host).WatchAsync(runs.RunAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);

        // when
        runs.Result = "[1]";
        _Hub(host).Invalidate(new LiveWriteSet([], [new LiveRowChange("todos", Old: null, New: new Todo(3))]));
        var update = await _NextAsync(updates);

        // then
        update.Kind.Should().Be(LiveUpdateKind.Changed);
        Encoding.UTF8.GetString(update.Payload.Span).Should().Be("[1]");
    }

    [Fact]
    public async Task should_run_again_when_a_changed_row_leaves_the_row_filter()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3);
        await using var updates = _Hub(host).WatchAsync(runs.RunAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);

        // when - the row belonged to owner 3 before the write and to owner 4 after it
        runs.Result = "[1]";
        _Hub(host).Invalidate(new LiveWriteSet([], [new LiveRowChange("todos", Old: new Todo(3), New: new Todo(4))]));
        var update = await _NextAsync(updates);

        // then
        update.Kind.Should().Be(LiveUpdateKind.Changed);
    }

    [Fact]
    public async Task should_not_run_again_and_should_count_the_rows_when_no_changed_row_passes_the_row_filter()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3);
        await using var updates = _Hub(host).WatchAsync(runs.RunAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);
        var pending = updates.MoveNextAsync().AsTask();

        // when - two rows of other owners change, and a row of a key the query never read
        runs.Result = "[1]";
        _Hub(host)
            .Invalidate(
                new LiveWriteSet(
                    [],
                    [
                        new LiveRowChange("todos", Old: null, New: new Todo(4)),
                        new LiveRowChange("todos", Old: new Todo(5), New: null),
                        new LiveRowChange("counters", Old: null, New: new Todo(3)),
                    ]
                )
            );

        // then
        pending.IsCompleted.Should().BeFalse();
        runs.Count.Should().Be(1);
        host.Meters.Total("headless.live_queries.rows.ruled_out").Should().Be(2);
        host.Meters.Total("headless.live_queries.wakes").Should().Be(0);

        // and a write to the whole key still reaches it
        _Hub(host).Invalidate(new LiveWriteSet(["todos"], []));
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        updates.Current.Kind.Should().Be(LiveUpdateKind.Changed);
    }

    [Fact]
    public async Task should_run_again_for_any_row_when_the_key_was_also_read_without_a_filter()
    {
        // given - the same run read all todos and the todos of owner 3
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3) { AlsoReadsWholeKey = true };
        await using var updates = _Hub(host).WatchAsync(runs.RunAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);

        // when
        runs.Result = "[1]";
        _Hub(host).Invalidate(new LiveWriteSet([], [new LiveRowChange("todos", Old: null, New: new Todo(4))]));
        var update = await _NextAsync(updates);

        // then
        update.Kind.Should().Be(LiveUpdateKind.Changed);
        host.Meters.Total("headless.live_queries.rows.ruled_out").Should().Be(0);
    }

    [Fact]
    public async Task should_run_again_when_a_changed_row_passes_the_second_of_two_filters_on_one_key()
    {
        // given - the same run read the todos of owner 3 and the todos of owner 7
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3) { SecondOwner = 7 };
        await using var updates = _Hub(host).WatchAsync(runs.RunAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);

        // when
        runs.Result = "[1]";
        _Hub(host).Invalidate(new LiveWriteSet([], [new LiveRowChange("todos", Old: null, New: new Todo(7))]));
        var update = await _NextAsync(updates);

        // then
        update.Kind.Should().Be(LiveUpdateKind.Changed);
    }

    [Fact]
    public async Task should_send_one_failed_update_and_end_when_a_run_reports_a_failure()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3);
        await using var updates = _Hub(host).WatchAsync(runs.RunAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);

        // when
        runs.Result = """{"status":403}""";
        runs.Fails = true;
        _Hub(host).Invalidate(new LiveWriteSet(["todos"], []));
        var failed = await _NextAsync(updates);
        var more = await updates.MoveNextAsync();

        // then
        failed.Kind.Should().Be(LiveUpdateKind.Failed);
        Encoding.UTF8.GetString(failed.Payload.Span).Should().Be("""{"status":403}""");
        failed.Version.Should().NotBeNullOrEmpty();
        more.Should().BeFalse();
        host.Meters.Total("headless.live_queries.runs.failed").Should().Be(1);
        host.ActiveSubscriptions.Should().Be(0);
    }

    [Fact]
    public async Task should_ignore_an_empty_write_set()
    {
        await using var host = new LiveQueriesTestHost();

        _Hub(host).Invalidate(new LiveWriteSet([], []));

        host.Meters.Total("headless.live_queries.wakes").Should().Be(0);
    }

    [Fact]
    public async Task should_enter_the_rerun_gate_before_every_run_but_the_first_and_release_its_turn_after_the_run()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3) { AlsoReadsWholeKey = true };
        var gate = new CountingGate();
        var watch = _Hub(host).WatchAsync(runs.RunAsync, gate.EnterAsync, subscriber: null, AbortToken);
        await using var updates = watch.GetAsyncEnumerator(AbortToken);

        // when
        await _NextAsync(updates);
        var enteredForFirstRun = gate.Entered;
        runs.Result = "[1]";
        _Hub(host).Invalidate(new LiveWriteSet(["todos"], []));
        await _NextAsync(updates);

        // then
        enteredForFirstRun.Should().Be(0);
        gate.Entered.Should().Be(1);
        gate.Released.Should().Be(1);
    }

    [Fact]
    public async Task should_run_once_for_the_writes_announced_while_a_subscription_waits_at_the_rerun_gate()
    {
        // given - a subscription stopped at the gate by a first write
        await using var host = new LiveQueriesTestHost();
        var runs = new OwnerTodosRun(owner: 3) { AlsoReadsWholeKey = true };
        var held = new HeldRun();
        var holding = true;
        var watch = _Hub(host)
            .WatchAsync(
                runs.RunAsync,
                async cancellationToken =>
                {
                    if (holding)
                    {
                        await held.WaitAsync(cancellationToken);
                    }

                    return null;
                },
                subscriber: null,
                AbortToken
            );
        await using var updates = watch.GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);
        _Hub(host).Invalidate(new LiveWriteSet(["todos"], []));
        var next = updates.MoveNextAsync().AsTask();
        await held.Entered.WaitAsync(_Patience, AbortToken);

        // when - a second write lands before the gate opens
        runs.Result = "[2]";
        _Hub(host).Invalidate(new LiveWriteSet(["todos"], []));
        holding = false;
        held.Release();
        (await next.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        var afterBothWrites = updates.Current;

        // then - a third write causes the third run: the second write caused none of its own
        runs.Result = "[3]";
        _Hub(host).Invalidate(new LiveWriteSet(["todos"], []));
        var afterThirdWrite = await _NextAsync(updates);
        Encoding.UTF8.GetString(afterBothWrites.Payload.Span).Should().Be("[2]");
        Encoding.UTF8.GetString(afterThirdWrite.Payload.Span).Should().Be("[3]");
        runs.Count.Should().Be(3);
    }

    [Fact]
    public async Task should_count_each_time_a_tracked_data_source_reports_that_it_fell_back()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var metrics = host.Services.GetRequiredService<LiveQueriesMetrics>();

        // when
        metrics.TrackingFellBack();
        metrics.TrackingFellBack();

        // then
        host.Meters.Total("headless.live_queries.tracking.fallbacks").Should().Be(2);
    }

    private static LiveQueryHub _Hub(LiveQueriesTestHost host) => host.Services.GetRequiredService<LiveQueryHub>();

    private static async Task<LiveUpdate> _NextAsync(IAsyncEnumerator<LiveUpdate> updates)
    {
        (await updates.MoveNextAsync().AsTask().WaitAsync(_Patience, AbortToken)).Should().BeTrue();

        return updates.Current;
    }

    private sealed record Todo(int OwnerId);

    private sealed class CountingGate
    {
        private readonly Lock _lock = new();

        public int Entered
        {
            get
            {
                lock (_lock)
                {
                    return field;
                }
            }
            private set;
        }

        public int Released
        {
            get
            {
                lock (_lock)
                {
                    return field;
                }
            }
            private set;
        }

        public ValueTask<IDisposable?> EnterAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                Entered++;
            }

#pragma warning disable CA2000 // False positive: the hub owns the turn it is handed, and its dispose is what this gate counts.
            return new(new Turn(this));
#pragma warning restore CA2000
        }

        private void _Release()
        {
            lock (_lock)
            {
                Released++;
            }
        }

        private sealed class Turn(CountingGate gate) : IDisposable
        {
            public void Dispose() => gate._Release();
        }
    }

    private sealed class OwnerTodosRun(int owner)
    {
        public string Result { get; set; } = "[]";

        public bool Fails { get; set; }

        public bool AlsoReadsWholeKey { get; init; }

        public int? SecondOwner { get; init; }

        public int Count { get; private set; }

        public ValueTask<LiveRunResult> RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            var recorder = new LiveQueryRecorder();

            if (AlsoReadsWholeKey)
            {
                recorder.Add(["todos"]);
            }

            recorder.AddFiltered("todos", row => ((Todo)row).OwnerId == owner);

            if (SecondOwner is { } second)
            {
                recorder.AddFiltered("todos", row => ((Todo)row).OwnerId == second);
            }

            return new(new LiveRunResult(Encoding.UTF8.GetBytes(Result), Fails, recorder.Snapshot()));
        }
    }
}

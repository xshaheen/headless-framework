// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveQueriesWatchTests : TestBase
{
    // Bounds a wait that only a defect makes endless; no assertion depends on how long anything takes.
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_yield_a_second_result_when_a_declared_dependency_is_invalidated_and_the_value_changed()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        var first = await _NextAsync(results);

        // when
        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        var second = await _NextAsync(results);

        // then
        first.Value.Should().Be(1);
        first.IsInitial.Should().BeTrue();
        second.Value.Should().Be(2);
        second.IsInitial.Should().BeFalse();
        second.Version.Should().NotBe(first.Version);
    }

    [Fact]
    public async Task should_fail_a_query_that_invalidates_a_key_instead_of_running_it_again()
    {
        // given - a query that announces the key it reads would make every one of its own runs stale
        await using var host = new LiveQueriesTestHost();
        var runs = 0;
        await using var results = host
            .Queries.WatchAsync(
                async (services, cancellationToken) =>
                {
                    runs++;
                    services.GetRequiredService<ILiveQueryDependencies>().DependsOn("a");

                    // Bounded, so a hub that lets the call through ends its loop and the test reports it.
                    if (runs <= 3)
                    {
                        await services
                            .GetRequiredService<ILiveQueryInvalidator>()
                            .InvalidateAsync(["a"], cancellationToken);
                    }

                    return 1;
                },
                AbortToken
            )
            .GetAsyncEnumerator(AbortToken);

        // when
        var firstResult = async () => await results.MoveNextAsync();

        // then
        await firstResult
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*InvalidateAsync*a*must be read-only*");
        runs.Should().Be(1);
    }

    [Fact]
    public async Task should_let_work_a_run_left_running_invalidate_and_record_nothing_for_it()
    {
        // given - the query starts work that is still running when the run returns
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        var runReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? leftover = null;
        await using var results = host
            .Queries.WatchAsync(
                (services, cancellationToken) =>
                {
                    var root = host.Services;
                    leftover ??= Task.Run(
                        async () =>
                        {
                            await runReturned.Task;
                            root.GetRequiredService<ILiveQueryDependencies>().DependsOn("late");
                            await host.Invalidator.InvalidateAsync(["a"], AbortToken);
                        },
                        AbortToken
                    );

                    return source.ReadAsync(services, cancellationToken);
                },
                AbortToken
            )
            .GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);

        // when
        source.Value = 2;
        runReturned.SetResult();
        var announcement = () => leftover!;

        // then - the announcement is an ordinary one, and the key declared after the run wakes nothing
        await announcement.Should().NotThrowAsync();
        (await _NextAsync(results)).Value.Should().Be(2);
        await host.Invalidator.InvalidateAsync(["late"], AbortToken);
        var pending = results.MoveNextAsync().AsTask();
        pending.IsCompleted.Should().BeFalse();
        source.Runs.Should().Be(2);

        source.Value = 3;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_not_run_again_when_a_key_it_does_not_depend_on_is_invalidated()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);

        // when
        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["b"], AbortToken);
        var pending = results.MoveNextAsync().AsTask();

        // then - the query completes synchronously, so a wake would already have run it and yielded 2
        pending.IsCompleted.Should().BeFalse();
        source.Runs.Should().Be(1);

        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        results.Current.Value.Should().Be(2);
        source.Runs.Should().Be(2);
    }

    [Fact]
    public async Task should_yield_nothing_when_a_re_run_returns_an_identical_result()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        var first = await _NextAsync(results);

        // when - the dependency is announced but the value is the same
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        var pending = results.MoveNextAsync().AsTask();

        // then - it ran again and had nothing new to yield
        source.Runs.Should().Be(2);
        pending.IsCompleted.Should().BeFalse();

        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        results.Current.Value.Should().Be(2);
        results.Current.IsInitial.Should().BeFalse();
        results.Current.Version.Should().NotBe(first.Version);
    }

    [Fact]
    public async Task should_give_equal_results_of_one_subscription_the_same_version_and_other_subscriptions_another()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await using var other = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        var first = await _NextAsync(results);
        var otherFirst = await _NextAsync(other);

        // when - the value changes and then returns to what it was
        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        var changed = await _NextAsync(results);
        source.Value = 1;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        var restored = await _NextAsync(results);

        // then
        restored.Version.Should().Be(first.Version);
        changed.Version.Should().NotBe(first.Version);
        otherFirst.Version.Should().NotBe(first.Version);
    }

    [Fact]
    public async Task should_run_once_more_when_a_write_is_announced_while_the_first_run_is_in_flight()
    {
        // given - the first run has read 1 and has not returned yet
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        var held = source.HoldNextRun();
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        var pending = results.MoveNextAsync().AsTask();
        await held.Entered.WaitAsync(_Patience, AbortToken);

        // when - the write lands and is announced once, before the run returns its stale read
        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        held.Release();
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        var stale = results.Current;
        var fresh = await _NextAsync(results);

        // then - no second announcement was needed
        stale.Value.Should().Be(1);
        fresh.Value.Should().Be(2);
        source.Runs.Should().Be(2);
    }

    [Fact]
    public async Task should_collapse_ten_invalidations_during_one_run_into_one_further_run()
    {
        // given - a second run that has read 2 and has not returned yet
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        await using var results = host
            .Queries.WatchAsync(source.ReadAsync, cancellation.Token)
            .GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);
        source.Value = 2;
        var held = source.HoldNextRun();
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        var pending = results.MoveNextAsync().AsTask();
        await held.Entered.WaitAsync(_Patience, AbortToken);

        // when
        for (var i = 0; i < 10; i++)
        {
            source.Value++;
            await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        }

        held.Release();
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        var duringWrites = results.Current;
        var afterWrites = await _NextAsync(results);
        var idle = results.MoveNextAsync().AsTask();

        // then - one run caught up with all ten, and nothing is left to run
        duringWrites.Value.Should().Be(2);
        afterWrites.Value.Should().Be(12);
        source.Runs.Should().Be(3);
        idle.IsCompleted.Should().BeFalse();

        // An enumerator cannot be disposed while it waits, so end the watch first.
        await cancellation.CancelAsync();
        var ended = async () => await idle;
        await ended.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_run_once_more_when_the_written_key_was_evicted_before_the_racing_run_finished()
    {
        // given - a hub that remembers one key, and a first run that has read 1 and has not returned yet
        await using var host = new LiveQueriesTestHost(options => options.MaxTrackedKeys = 1);
        var source = new WatchedSource("a");
        var held = source.HoldNextRun();
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        var pending = results.MoveNextAsync().AsTask();
        await held.Entered.WaitAsync(_Patience, AbortToken);

        // when - the write to "a" is announced, and a write to "b" then pushes "a" out of the hub's memory
        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        await host.Invalidator.InvalidateAsync(["b"], AbortToken);
        held.Release();
        (await pending.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        var stale = results.Current;
        var fresh = await _NextAsync(results);

        // then
        stale.Value.Should().Be(1);
        fresh.Value.Should().Be(2);
    }

    [Fact]
    public async Task should_fail_the_watch_when_the_query_records_no_dependency()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        await using var results = host
            .Queries.WatchAsync(static (_, _) => ValueTask.FromResult(1), AbortToken)
            .GetAsyncEnumerator(AbortToken);

        // when
        var act = async () => await results.MoveNextAsync();

        // then
        (await act.Should().ThrowAsync<LiveQueryHasNoDependenciesException>()).WithMessage(
            "*recorded no dependency*ILiveQueryDependencies.DependsOn*"
        );
        host.ActiveSubscriptions.Should().Be(0);
    }

    [Fact]
    public async Task should_remove_the_subscription_when_the_watch_is_cancelled()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        await using var results = host
            .Queries.WatchAsync(source.ReadAsync, cancellation.Token)
            .GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);
        var pending = results.MoveNextAsync().AsTask();
        host.ActiveSubscriptions.Should().Be(1);

        // when
        await cancellation.CancelAsync();
        var act = async () => await pending.WaitAsync(_Patience, AbortToken);

        // then - the meter no longer counts it, and an announcement no longer runs its query
        await act.Should().ThrowAsync<OperationCanceledException>();
        host.ActiveSubscriptions.Should().Be(0);

        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        source.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_remove_the_subscription_when_the_caller_stops_enumerating()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);

        // when
        await results.DisposeAsync();

        // then
        host.ActiveSubscriptions.Should().Be(0);
    }

    [Fact]
    public async Task should_surface_an_exception_the_query_throws_and_count_the_failed_run()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        await using var results = host
            .Queries.WatchAsync<int>(static (_, _) => throw new InvalidOperationException("boom"), AbortToken)
            .GetAsyncEnumerator(AbortToken);

        // when
        var act = async () => await results.MoveNextAsync();

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");
        host.Meters.Total("headless.live_queries.runs.failed").Should().Be(1);
        host.ActiveSubscriptions.Should().Be(0);
    }

    [Fact]
    public async Task should_count_runs_and_wakes()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var source = new WatchedSource("a");
        await using var results = host.Queries.WatchAsync(source.ReadAsync, AbortToken).GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);
        var pending = results.MoveNextAsync().AsTask();

        // when - one announcement reaches the waiting subscription and one misses it
        source.Value = 2;
        await host.Invalidator.InvalidateAsync(["b"], AbortToken);
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        await pending.WaitAsync(_Patience, AbortToken);

        // then
        host.Meters.Total("headless.live_queries.runs").Should().Be(2);
        host.Meters.Total("headless.live_queries.wakes").Should().Be(1);
        host.Meters.Total("headless.live_queries.runs.failed").Should().Be(0);
    }

    [Fact]
    public async Task should_give_each_run_its_own_service_scope()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var scopes = new List<IServiceProvider>();
        await using var results = host
            .Queries.WatchAsync(
                (services, _) =>
                {
                    scopes.Add(services);
                    services.GetRequiredService<ILiveQueryDependencies>().DependsOn("a");

                    return ValueTask.FromResult(scopes.Count);
                },
                AbortToken
            )
            .GetAsyncEnumerator(AbortToken);
        await _NextAsync(results);

        // when
        await host.Invalidator.InvalidateAsync(["a"], AbortToken);
        await _NextAsync(results);

        // then
        scopes.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        scopes.Should().NotContain(host.Services);
    }

    [Fact]
    public async Task should_reject_a_null_query()
    {
        await using var host = new LiveQueriesTestHost();

        var act = () => host.Queries.WatchAsync<int>(null!, AbortToken);

        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task<LiveSnapshot<int>> _NextAsync(IAsyncEnumerator<LiveSnapshot<int>> results)
    {
        (await results.MoveNextAsync().AsTask().WaitAsync(_Patience, AbortToken)).Should().BeTrue();

        return results.Current;
    }
}

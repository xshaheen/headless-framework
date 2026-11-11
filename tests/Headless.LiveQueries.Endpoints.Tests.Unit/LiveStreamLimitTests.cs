// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Threading.Channels;
using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class LiveStreamLimitTests : TestBase
{
    // Ends a wait that would otherwise hang the run; no assertion depends on it.
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_refuse_a_users_stream_past_the_per_user_limit_and_leave_other_users_alone()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            _MapTodos,
            AbortToken,
            configureLive: options => options.MaxStreamsPerUser = 2
        );
        app.Users.Set("alice");
        app.Users.Set("bob");
        using var first = await app.OpenAsync("/todos", AbortToken, user: "alice");
        using var second = await app.OpenAsync("/todos", AbortToken, user: "alice");

        // when
        using var third = await app.OpenAsync("/todos", AbortToken, user: "alice");
        using var ofBob = await app.OpenAsync("/todos", AbortToken, user: "bob");
        var problem = JsonElement.Parse(await third.Response.Content.ReadAsStringAsync(AbortToken));

        // then
        second.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        third.Response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        third.Response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(3));
        problem
            .GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LiveEndpointsErrorCodes.StreamLimitReached);
        ofBob.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        app.Meters.Total("headless.live_queries.streams.rejected").Should().Be(1);
    }

    [Fact]
    public async Task should_count_anonymous_streams_apart_from_an_authenticated_users()
    {
        // given - anonymous callers of one address at their limit
        await using var app = await LiveTestApp.StartAsync(
            _MapTodos,
            AbortToken,
            configureLive: options => options.MaxStreamsPerUser = 1
        );
        app.Users.Set("alice");
        using var anonymous = await app.OpenAsync("/todos", AbortToken);

        // when
        using var anotherAnonymous = await app.OpenAsync("/todos", AbortToken);
        using var ofAlice = await app.OpenAsync("/todos", AbortToken, user: "alice");

        // then
        anonymous.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        anotherAnonymous.Response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        ofAlice.Response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_refuse_any_stream_past_the_hosts_total_limit()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            _MapTodos,
            AbortToken,
            configureLive: options => options.MaxStreams = 2
        );
        app.Users.Set("alice");
        app.Users.Set("bob");
        app.Users.Set("carol");
        using var ofAlice = await app.OpenAsync("/todos", AbortToken, user: "alice");
        using var ofBob = await app.OpenAsync("/todos", AbortToken, user: "bob");

        // when
        using var ofCarol = await app.OpenAsync("/todos", AbortToken, user: "carol");

        // then
        ofBob.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        ofCarol.Response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task should_run_two_signalled_streams_one_after_the_other_when_one_rerun_is_allowed_at_a_time()
    {
        // given - two open streams whose re-runs stop inside the handler until the test lets them go
        var reruns = new HeldReruns();
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/slow",
                        async (ILiveQueryDependencies dependencies, CancellationToken cancellationToken) =>
                        {
                            dependencies.DependsOn("slow");

                            return new { run = await reruns.RunAsync(cancellationToken) };
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken,
            configureLive: options => options.MaxConcurrentReruns = 1
        );
        using var first = await app.OpenAsync("/slow", AbortToken);
        using var second = await app.OpenAsync("/slow", AbortToken);
        await first.NextResultAsync(AbortToken);
        await second.NextResultAsync(AbortToken);
        reruns.Holding = true;

        // when - one write signals both; the first re-run is in the handler and the other waits for its turn
        await app.InvalidateAsync("slow", AbortToken);
        var running = await reruns.NextAsync(AbortToken);
        await app.Meters.ReachedAsync("headless.live_queries.reruns.queued", 1).WaitAsync(_Patience, AbortToken);
        var inFlightWhileTheOtherWaits = reruns.InFlight;
        running.Release();
        var runningNext = await reruns.NextAsync(AbortToken);
        runningNext.Release();
        await first.NextResultAsync(AbortToken);
        await second.NextResultAsync(AbortToken);

        // then
        inFlightWhileTheOtherWaits.Should().Be(1);
        reruns.MostInFlight.Should().Be(1);
    }

    [Fact]
    public async Task should_answer_the_invalidations_inside_the_minimum_interval_with_one_rerun()
    {
        // given - a stream that may run again one minute after its last run started
        var clock = new RerunClock();
        var value = new Shared<string>("a");
        var runs = 0;
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/value",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("value");
                            Interlocked.Increment(ref runs);

                            return new { value = value.Value };
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken,
            configureLive: options => options.MinRerunInterval = TimeSpan.FromMinutes(1),
            configureBuilder: builder =>
                builder.Services.AddKeyedSingleton<TimeProvider>(LiveEndpointsOptions.RerunClockServiceKey, clock)
        );
        using var stream = await app.OpenAsync("/value", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when - a change is announced inside the interval, a second one while the stream waits the interval out,
        // and then the interval passes
        value.Value = "b";
        await app.InvalidateAsync("value", AbortToken);
        await clock.Waiting.WaitAsync(_Patience, AbortToken);
        await app.InvalidateAsync("value", AbortToken);
        var runsInsideTheInterval = Volatile.Read(ref runs);
        clock.Advance(TimeSpan.FromMinutes(1));
        var afterTheInterval = await stream.NextResultAsync(AbortToken);
        var runsAfterTheInterval = Volatile.Read(ref runs);

        // then - a third change one interval later is answered at once, which it would not be had the second
        // announcement left a run of its own waiting
        clock.Advance(TimeSpan.FromMinutes(1));
        value.Value = "c";
        await app.InvalidateAsync("value", AbortToken);
        var afterTheThirdChange = await stream.NextResultAsync(AbortToken);
        runsInsideTheInterval.Should().Be(1);
        afterTheInterval.GetProperty("value").GetString().Should().Be("b");
        runsAfterTheInterval.Should().Be(2);
        afterTheThirdChange.GetProperty("value").GetString().Should().Be("c");
        Volatile.Read(ref runs).Should().Be(3);
    }

    private static void _MapTodos(WebApplication application)
    {
        application
            .MapGet(
                "/todos",
                (ILiveQueryDependencies dependencies) =>
                {
                    dependencies.DependsOn("todos");

                    return new[] { "milk" };
                }
            )
            .WithLiveUpdates();
    }

    /// <summary>Stops each run inside the handler, once <see cref="Holding" /> is set, until the test releases it.</summary>
    private sealed class HeldReruns
    {
        private readonly Channel<Held> _entered = Channel.CreateUnbounded<Held>();
        private int _inFlight;
        private int _mostInFlight;
        private int _runs;

        public volatile bool Holding;

        public int InFlight => Volatile.Read(ref _inFlight);

        public int MostInFlight => Volatile.Read(ref _mostInFlight);

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            if (Holding)
            {
                var inFlight = Interlocked.Increment(ref _inFlight);
                _RaiseMostInFlight(inFlight);
                var held = new Held();
                await _entered.Writer.WriteAsync(held, cancellationToken);

                try
                {
                    await held.Released.WaitAsync(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }

            return Interlocked.Increment(ref _runs);
        }

        public async Task<Held> NextAsync(CancellationToken cancellationToken)
        {
            return await _entered.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_Patience, cancellationToken);
        }

        private void _RaiseMostInFlight(int inFlight)
        {
            int seen;

            do
            {
                seen = Volatile.Read(ref _mostInFlight);
            } while (seen < inFlight && Interlocked.CompareExchange(ref _mostInFlight, inFlight, seen) != seen);
        }
    }

    private sealed class Held
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Released => _released.Task;

        public void Release() => _released.TrySetResult();
    }
}

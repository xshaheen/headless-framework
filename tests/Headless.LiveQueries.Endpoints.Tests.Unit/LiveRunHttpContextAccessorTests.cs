// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Threading.Channels;
using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// What <see cref="IHttpContextAccessor" /> returns around a stream: the run's request inside a run, and the
/// request that opened the stream everywhere else, for as long as that request lives.
/// </summary>
public sealed class LiveRunHttpContextAccessorTests : TestBase
{
    // Ends a wait that would otherwise hang the run; no assertion depends on it.
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_keep_the_opening_request_on_the_accessor_for_middleware_above_the_endpoint()
    {
        // given - a middleware above the endpoint that reads the accessor whenever the test asks
        var probe = new AccessorProbe();
        await using var app = await LiveTestApp.StartAsync(
            _MapTodos,
            AbortToken,
            afterAuthentication: application => application.Use(probe.AnswerAsync)
        );
        using var stream = await app.OpenAsync("/todos", AbortToken);

        // when
        var first = await stream.NextResultAsync(AbortToken);
        var afterFirstRun = await probe.AskAsync(AbortToken);
        await app.InvalidateAsync("todos", AbortToken);
        var second = await stream.NextResultAsync(AbortToken);
        var afterRerun = await probe.AskAsync(AbortToken);

        // then - each run read its own request, and the middleware's frame never lost the opening one
        first.GetProperty("accessorIsTheRun").GetBoolean().Should().BeTrue();
        second.GetProperty("accessorIsTheRun").GetBoolean().Should().BeTrue();
        afterFirstRun.Should().BeTrue("the first run must not take the opening request away from the frames above");
        afterRerun.Should().BeTrue("a re-run must not take the opening request away from the frames above");
    }

    [Fact]
    public async Task should_keep_the_opening_request_on_the_accessor_for_the_transport()
    {
        // given - a transport that reads the accessor each time it writes an update
        await using var app = await LiveTestApp.StartAsync(
            _MapTodos,
            AbortToken,
            configureSetup: setup => setup.AddTransport<AccessorTransport>()
        );
        using var stream = await app.OpenAsync("/todos", AbortToken, accept: AccessorTransport.MediaType);

        // when
        var afterFirstRun = await stream.NextLineAsync(AbortToken);
        await app.InvalidateAsync("todos", AbortToken);
        var afterRerun = await stream.NextLineAsync(AbortToken);

        // then
        afterFirstRun.Should().Be("opening request");
        afterRerun.Should().Be("opening request");
    }

    [Fact]
    public async Task should_not_hand_the_runs_request_to_work_the_run_left_running()
    {
        // given - a handler that leaves a task behind, which reads the accessor once the run has returned
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leftover = new TaskCompletionSource<HttpContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/todos",
                        (IHttpContextAccessor accessor, ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("todos");

                            _ = Task.Run(async () =>
                            {
                                await release.Task;
                                leftover.TrySetResult(accessor.HttpContext);
                            });

                            return new[] { "milk" };
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );
        using var stream = await app.OpenAsync("/todos", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when - the run is over and its scope disposed
        release.SetResult();
        var seen = await leftover.Task.WaitAsync(_Patience, AbortToken);

        // then
        seen.Should().BeNull("the run's request is gone with its scope, and no other request started that work");
    }

    [Fact]
    public async Task should_name_the_instance_in_the_problem_of_a_first_run_that_throws()
    {
        // given - the host's exception handler writes the problem, above the live endpoint
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet("/broken", string () => throw new InvalidOperationException("the database is on fire"))
                    .WithLiveUpdates(),
            AbortToken,
            useHeadless: true
        );

        // when
        using var stream = await app.OpenAsync("/broken", AbortToken, accept: "text/event-stream, application/json");
        var body = JsonElement.Parse(await stream.Response.Content.ReadAsStringAsync(AbortToken));

        // then
        stream.Response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.GetProperty("instance").GetString().Should().Be("/broken");
    }

    private static void _MapTodos(WebApplication application)
    {
        var runs = 0;

        application
            .MapGet(
                "/todos",
                (IHttpContextAccessor accessor, HttpContext run, ILiveQueryDependencies dependencies) =>
                {
                    dependencies.DependsOn("todos");

                    // Numbered, because a run that answers what the last one answered sends no update.
                    return new
                    {
                        run = Interlocked.Increment(ref runs),
                        accessorIsTheRun = ReferenceEquals(accessor.HttpContext, run),
                    };
                }
            )
            .WithLiveUpdates();
    }

    /// <summary>
    /// A middleware that stays in its own frame while the stream is open and reads the accessor there each time a
    /// test asks, so the answer is what code above the endpoint sees at that moment.
    /// </summary>
    private sealed class AccessorProbe
    {
        private readonly Channel<TaskCompletionSource<bool>> _questions = Channel.CreateUnbounded<
            TaskCompletionSource<bool>
        >();

        /// <summary>Returns whether the middleware's frame reads the opening request from the accessor.</summary>
        public async Task<bool> AskAsync(CancellationToken cancellationToken)
        {
            var question = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _questions.Writer.WriteAsync(question, cancellationToken);

            return await question.Task.WaitAsync(_Patience, cancellationToken);
        }

        public async Task AnswerAsync(HttpContext opener, RequestDelegate next)
        {
            var accessor = opener.RequestServices.GetRequiredService<IHttpContextAccessor>();
            var pending = next(opener);

            try
            {
                await foreach (var question in _questions.Reader.ReadAllAsync(opener.RequestAborted))
                {
                    question.SetResult(ReferenceEquals(accessor.HttpContext, opener));
                }
            }
            catch (OperationCanceledException)
            {
                // The client left; the stream below ends for the same reason.
            }

            await pending;
        }
    }

    /// <summary>Writes, for each update, whose request the accessor returned on the transport's own flow.</summary>
    private sealed class AccessorTransport(IHttpContextAccessor accessor) : ILiveTransport
    {
        public const string MediaType = "application/x-test-accessor";

        public bool CanHandle(HttpContext httpContext)
        {
            return httpContext
                .Request.GetTypedHeaders()
                .Accept.Any(accepted => accepted.MediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase));
        }

        public async Task WriteAsync(
            HttpContext httpContext,
            IAsyncEnumerable<LiveUpdate> updates,
            CancellationToken cancellationToken
        )
        {
            httpContext.Response.ContentType = MediaType;

            await foreach (var _ in updates.WithCancellation(cancellationToken))
            {
                var current = accessor.HttpContext;
                var seen =
                    current is null ? "nothing"
                    : ReferenceEquals(current, httpContext) ? "opening request"
                    : "another request";
                await httpContext.Response.WriteAsync(seen + "\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
        }
    }
}

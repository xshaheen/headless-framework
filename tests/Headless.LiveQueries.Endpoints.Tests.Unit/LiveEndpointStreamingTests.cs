// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveEndpointStreamingTests : TestBase
{
    [Fact]
    public async Task should_send_the_result_and_send_it_again_after_a_dependency_was_invalidated_and_the_value_changed()
    {
        // given
        var todos = new Shared<string[]>(["milk"]);
        await using var app = await LiveTestApp.StartAsync(application => _MapTodos(application, todos), AbortToken);
        using var stream = await app.OpenAsync("/todos", AbortToken);
        var first = await stream.NextEventAsync(AbortToken);

        // when - a key the handler never declared changes, then the one it declared
        await app.InvalidateAsync("unrelated", AbortToken);
        todos.Value = ["milk", "eggs"];
        await app.InvalidateAsync("todos", AbortToken);
        var second = await stream.NextEventAsync(AbortToken);

        // then
        stream.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        first!.Name.Should().Be("result");
        first.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk");
        second!.Name.Should().Be("result");
        second.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
        first.Id.Should().NotBeNullOrEmpty();
        second.Id.Should().NotBeNullOrEmpty().And.NotBe(first.Id);
    }

    [Fact]
    public async Task should_send_nothing_when_a_rerun_returns_the_result_already_sent()
    {
        // given
        var todos = new Shared<string[]>(["milk"]);
        await using var app = await LiveTestApp.StartAsync(application => _MapTodos(application, todos), AbortToken);
        using var stream = await app.OpenAsync("/todos", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when - one re-run with the same value, then one with a new value
        await app.InvalidateAsync("todos", AbortToken);
        todos.Value = ["eggs"];
        await app.InvalidateAsync("todos", AbortToken);
        var next = await stream.NextResultAsync(AbortToken);

        // then - the next thing on the wire is the new value
        next.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("eggs");
    }

    [Fact]
    public async Task should_let_work_that_outlived_the_run_that_started_it_announce_a_change()
    {
        // given - the handler starts work that is still running when its run returns, as a lazily started loop does
        var todos = new Shared<string[]>(["milk"]);
        var leftover = new Shared<Task?>(null);
        var resultReachedClient = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/todos",
                        (ILiveQueryDependencies dependencies, ILiveQueryInvalidator invalidator) =>
                        {
                            dependencies.DependsOn("todos");

                            // Started inside the run, so it carries the run's ambient state for the rest of its life.
                            leftover.Value ??= Task.Run(
                                async () =>
                                {
                                    await resultReachedClient.Task;
                                    todos.Value = ["milk", "eggs"];
                                    await invalidator.InvalidateAsync(["todos"], AbortToken);
                                },
                                AbortToken
                            );

                            return todos.Value;
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );
        using var stream = await app.OpenAsync("/todos", AbortToken);
        var first = await stream.NextResultAsync(AbortToken);

        // when - the first run has returned and its result is with the client
        resultReachedClient.SetResult();
        var announce = async () => await leftover.Value!;

        // then - the work is no longer part of a live query, which may not announce a change, so it is not refused
        await announce.Should().NotThrowAsync();
        var second = await stream.NextResultAsync(AbortToken);
        first.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk");
        second.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public async Task should_answer_with_the_ordinary_response_and_open_no_subscription_when_no_stream_is_requested(
        string accept
    )
    {
        // given
        var todos = new Shared<string[]>(["milk"]);
        await using var app = await LiveTestApp.StartAsync(application => _MapTodos(application, todos), AbortToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/todos");
        request.Headers.TryAddWithoutValidation("Accept", accept);

        // when
        using var response = await app.Client.SendAsync(request, AbortToken);
        var body = await response.Content.ReadAsStringAsync(AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        body.Should().Be("""["milk"]""");
        response.Headers.Vary.Should().Contain("Accept");
        app.Meters.Total("headless.live_queries.runs").Should().Be(0);
    }

    [Fact]
    public async Task should_bind_every_argument_again_and_resolve_fresh_scoped_services_on_every_run()
    {
        // given - route values, a query-bound record, a parameter object holding a scoped service, a keyed service
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/lists/{list}",
                        (
                            [AsParameters] ListArguments arguments,
                            [AsParameters] Paging paging,
                            [FromKeyedServices("audit")] ScopedProbe keyed
                        ) =>
                        {
                            arguments.Dependencies.DependsOn("lists");

                            return new
                            {
                                list = arguments.List,
                                limit = paging.Limit,
                                offset = paging.Offset,
                                probe = arguments.Probe.Id,
                                keyed = keyed.Id,
                            };
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken,
            configureBuilder: builder =>
            {
                builder.Services.AddScoped<ScopedProbe>();
                builder.Services.AddKeyedScoped<ScopedProbe>("audit");
            }
        );
        using var stream = await app.OpenAsync("/lists/groceries?limit=5&offset=10", AbortToken);

        // when
        var first = await stream.NextResultAsync(AbortToken);
        await app.InvalidateAsync("lists", AbortToken);
        var second = await stream.NextResultAsync(AbortToken);

        // then
        foreach (var result in new[] { first, second })
        {
            result.GetProperty("list").GetString().Should().Be("groceries");
            result.GetProperty("limit").GetInt32().Should().Be(5);
            result.GetProperty("offset").GetInt32().Should().Be(10);
        }

        second.GetProperty("probe").GetGuid().Should().NotBe(first.GetProperty("probe").GetGuid());
        second.GetProperty("keyed").GetGuid().Should().NotBe(first.GetProperty("keyed").GetGuid());
        first.GetProperty("keyed").GetGuid().Should().NotBe(first.GetProperty("probe").GetGuid());
    }

    [Fact]
    public async Task should_run_the_endpoint_filters_again_on_every_run()
    {
        // given - a validation filter that starts rejecting while the stream is open
        var rejecting = new Shared<bool>(false);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/items",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("items");

                            return new[] { 1 };
                        }
                    )
                    .WithLiveUpdates()
                    .AddEndpointFilter(
                        async (context, next) =>
                            rejecting.Value
                                ? Results.ValidationProblem(
                                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                                    {
                                        ["limit"] = ["too large"],
                                    }
                                )
                                : await next(context)
                    ),
            AbortToken
        );
        using var stream = await app.OpenAsync("/items", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when
        rejecting.Value = true;
        await app.InvalidateAsync("items", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);
        var afterFailure = await stream.NextEventAsync(AbortToken);

        // then
        failure.GetProperty("status").GetInt32().Should().Be(400);
        failure.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.RunFailed);
        afterFailure.Should().BeNull("a failure ends the stream");
    }

    [Fact]
    public async Task should_refuse_the_stream_with_a_problem_when_the_first_run_recorded_no_dependency()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            application => application.MapGet("/static", () => new { value = 1 }).WithLiveUpdates(),
            AbortToken
        );

        // when
        using var stream = await app.OpenAsync("/static", AbortToken);
        var body = JsonElement.Parse(await stream.Response.Content.ReadAsStringAsync(AbortToken));

        // then
        stream.Response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.NoDependencies);
        app.Meters.Total("headless.live_queries.subscriptions.active").Should().Be(0);
    }

    [Fact]
    public async Task should_end_the_stream_with_a_failure_when_a_rerun_recorded_no_dependency()
    {
        // given
        var declaring = new Shared<bool>(true);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/sometimes",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            if (declaring.Value)
                            {
                                dependencies.DependsOn("sometimes");
                            }

                            return new { value = 1 };
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );
        using var stream = await app.OpenAsync("/sometimes", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when
        declaring.Value = false;
        await app.InvalidateAsync("sometimes", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);

        // then
        failure.GetProperty("status").GetInt32().Should().Be(500);
        failure
            .GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LiveEndpointsErrorCodes.NoDependencies);
        (await stream.NextEventAsync(AbortToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(422)]
    public async Task should_answer_with_the_ordinary_response_and_no_stream_when_the_first_run_does_not_succeed(
        int statusCode
    )
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/missing",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("missing");

                            return Results.Problem(statusCode: statusCode, title: "Not this time");
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );

        // when
        using var stream = await app.OpenAsync("/missing", AbortToken);
        var body = JsonElement.Parse(await stream.Response.Content.ReadAsStringAsync(AbortToken));

        // then
        ((int)stream.Response.StatusCode)
            .Should()
            .Be(statusCode);
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.GetProperty("title").GetString().Should().Be("Not this time");
        body.GetProperty("status").GetInt32().Should().Be(statusCode);
    }

    [Fact]
    public async Task should_leave_the_first_runs_exception_to_the_hosts_error_handling()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet("/broken", string () => throw new InvalidOperationException("the database is on fire"))
                    .WithLiveUpdates(),
            AbortToken,
            useHeadless: true
        );

        // when
        using var stream = await app.OpenAsync("/broken", AbortToken);
        var body = await stream.Response.Content.ReadAsStringAsync(AbortToken);

        // then
        var mediaType = stream.Response.Content.Headers.ContentType?.MediaType;
        stream.Response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        mediaType.Should().NotBe("text/event-stream");
        body.Should().NotContain("on fire").And.NotContain("event:");
    }

    [Fact]
    public async Task should_send_one_failure_and_close_when_a_rerun_throws_and_should_keep_serving()
    {
        // given
        var throwing = new Shared<bool>(false);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/fragile",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("fragile");

                            return throwing.Value
                                ? throw new InvalidOperationException("the database is on fire")
                                : new[] { 1 };
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );
        using var stream = await app.OpenAsync("/fragile", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when
        throwing.Value = true;
        await app.InvalidateAsync("fragile", AbortToken);
        var failure = await stream.NextEventAsync(AbortToken);
        var afterFailure = await stream.NextEventAsync(AbortToken);
        throwing.Value = false;
        using var later = await app.Client.GetAsync("/fragile", AbortToken);

        // then
        failure!.Name.Should().Be("failure");
        failure.Json.GetProperty("status").GetInt32().Should().Be(500);
        failure
            .Json.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LiveEndpointsErrorCodes.RunFailed);
        failure.Data.Should().ContainSingle().Which.Should().NotContain("on fire");
        afterFailure.Should().BeNull("a failure ends the stream");
        later.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_end_the_stream_with_the_reruns_status_when_a_rerun_does_not_succeed()
    {
        // given
        var found = new Shared<bool>(true);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/todo",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("todo");

                            return found.Value ? Results.Ok(new { title = "milk" }) : Results.NotFound();
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );
        using var stream = await app.OpenAsync("/todo", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when
        found.Value = false;
        await app.InvalidateAsync("todo", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);

        // then
        failure.GetProperty("status").GetInt32().Should().Be(404);
        failure.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.RunFailed);
    }

    [Fact]
    public async Task should_fail_when_the_endpoints_are_built_for_an_endpoint_that_is_not_a_get()
    {
        // when - the host builds its endpoints as it starts
        var starting = async () =>
        {
            await using var app = await LiveTestApp.StartAsync(
                application => application.MapPost("/commands", () => Results.Ok()).WithLiveUpdates(),
                AbortToken
            );
        };

        // then
        (await starting.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*WithLiveUpdates()*GET*")
            .WithMessage("*POST /commands*");
    }

    private static void _MapTodos(WebApplication application, Shared<string[]> todos)
    {
        application
            .MapGet(
                "/todos",
                (ILiveQueryDependencies dependencies) =>
                {
                    dependencies.DependsOn("todos");

                    return todos.Value;
                }
            )
            .WithLiveUpdates();
    }

    public sealed class ScopedProbe
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed record Paging(int Limit, int Offset);

    public sealed record ListArguments([FromRoute] string List, ScopedProbe Probe, ILiveQueryDependencies Dependencies);
}

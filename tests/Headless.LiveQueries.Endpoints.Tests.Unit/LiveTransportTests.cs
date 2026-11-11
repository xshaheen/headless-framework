// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveTransportTests : TestBase
{
    [Fact]
    public async Task should_frame_server_sent_events_as_named_events_with_an_id_and_one_data_line()
    {
        // given - a host whose serializer indents, so a payload spans lines unless the transport joins them
        var state = new Shared<(string Title, bool Found)>(("milk", true));
        await using var app = await LiveTestApp.StartAsync(
            application => _MapTodo(application, state),
            AbortToken,
            configureBuilder: _IndentJson
        );
        using var stream = await app.OpenAsync("/todo", AbortToken);

        // when - the first result arrives, then a re-run fails
        List<string> lines = [];

        for (var index = 0; index < 6; index++)
        {
            lines.Add((await stream.NextLineAsync(AbortToken))!);
        }

        state.Value = ("milk", false);
        await app.InvalidateAsync("todo", AbortToken);
        var failure = await stream.NextEventAsync(AbortToken);
        var end = await stream.NextLineAsync(AbortToken);

        // then
        lines[0].Should().Be("retry: 3000");
        lines[1].Should().BeEmpty();
        lines[2].Should().Be("event: result");
        lines[3].Should().MatchRegex("^id: [0-9a-f]+$");
        lines[4].Should().StartWith("data: {").And.EndWith("}");
        JsonElement.Parse(lines[4][6..]).GetProperty("title").GetString().Should().Be("milk");
        lines[5].Should().BeEmpty();
        failure!.Name.Should().Be("failure");
        failure.Id.Should().NotBeNullOrEmpty();
        failure.Data.Should().ContainSingle();
        failure
            .Json.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LiveEndpointsErrorCodes.RunFailed);
        end.Should().BeNull("a failure ends the stream");
    }

    [Fact]
    public async Task should_send_the_headers_a_stream_needs_to_pass_caches_and_proxies_unbuffered()
    {
        // given
        var state = new Shared<(string Title, bool Found)>(("milk", true));
        await using var app = await LiveTestApp.StartAsync(application => _MapTodo(application, state), AbortToken);

        // when
        using var stream = await app.OpenAsync("/todo", AbortToken);

        // then
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        stream.Response.Headers.Vary.Should().Contain("Accept");
        stream.Response.Headers.CacheControl!.NoCache.Should().BeTrue();
        stream.Response.Headers.CacheControl.NoStore.Should().BeTrue();
        stream.Response.Headers.GetValues("X-Accel-Buffering").Should().Equal("no");
    }

    [Fact]
    public async Task should_frame_newline_delimited_json_as_one_object_per_line()
    {
        // given
        var state = new Shared<(string Title, bool Found)>(("milk", true));
        await using var app = await LiveTestApp.StartAsync(
            application => _MapTodo(application, state),
            AbortToken,
            configureBuilder: _IndentJson
        );
        using var stream = await app.OpenAsync("/todo", AbortToken, accept: "application/x-ndjson");

        // when
        var first = JsonElement.Parse((await stream.NextLineAsync(AbortToken))!);
        state.Value = ("eggs", true);
        await app.InvalidateAsync("todo", AbortToken);
        var second = JsonElement.Parse((await stream.NextLineAsync(AbortToken))!);
        state.Value = ("eggs", false);
        await app.InvalidateAsync("todo", AbortToken);
        var failure = JsonElement.Parse((await stream.NextLineAsync(AbortToken))!);
        var end = await stream.NextLineAsync(AbortToken);

        // then
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("application/x-ndjson");
        first.GetProperty("kind").GetString().Should().Be("result");
        first.GetProperty("value").GetProperty("title").GetString().Should().Be("milk");
        second.GetProperty("kind").GetString().Should().Be("result");
        second.GetProperty("value").GetProperty("title").GetString().Should().Be("eggs");
        second.GetProperty("version").GetString().Should().NotBe(first.GetProperty("version").GetString());
        failure.GetProperty("kind").GetString().Should().Be("failure");
        failure
            .GetProperty("value")
            .GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LiveEndpointsErrorCodes.RunFailed);
        end.Should().BeNull("a failure ends the stream");
    }

    [Fact]
    public async Task should_answer_with_an_added_transport_before_a_built_in_one_that_accepts_the_same_request()
    {
        // given
        var state = new Shared<(string Title, bool Found)>(("milk", true));
        await using var app = await LiveTestApp.StartAsync(
            application => _MapTodo(application, state),
            AbortToken,
            configureSetup: setup => setup.AddTransport<TestLiveTransport>()
        );

        // when - the request accepts the added transport's format and server-sent events
        using var stream = await app.OpenAsync(
            "/todo",
            AbortToken,
            accept: $"text/event-stream, {TestLiveTransport.MediaType}"
        );
        var first = (await stream.NextLineAsync(AbortToken))!.Split('|');
        state.Value = ("eggs", true);
        await app.InvalidateAsync("todo", AbortToken);
        var second = (await stream.NextLineAsync(AbortToken))!.Split('|');

        // then
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be(TestLiveTransport.MediaType);
        first[0].Should().Be(nameof(LiveUpdateKind.Initial));
        JsonElement.Parse(first[3]).GetProperty("title").GetString().Should().Be("milk");
        second[0].Should().Be(nameof(LiveUpdateKind.Changed));
        JsonElement.Parse(second[3]).GetProperty("title").GetString().Should().Be("eggs");
    }

    [Theory]
    [InlineData("text/event-stream", ": heartbeat")]
    [InlineData("application/x-ndjson", """{"kind":"heartbeat"}""")]
    public async Task should_write_a_heartbeat_on_an_idle_stream(string accept, string heartbeat)
    {
        // given
        var state = new Shared<(string Title, bool Found)>(("milk", true));
        await using var app = await LiveTestApp.StartAsync(
            application => _MapTodo(application, state),
            AbortToken,
            configureLive: options => options.HeartbeatInterval = TimeSpan.FromMilliseconds(20)
        );
        using var stream = await app.OpenAsync("/todo", AbortToken, accept: accept);

        // when - nothing changes
        string? line;

        do
        {
            line = await stream.NextLineAsync(AbortToken);
        } while (line is not null && !string.Equals(line, heartbeat, StringComparison.Ordinal));

        // then
        line.Should().Be(heartbeat);
    }

    [Fact]
    public async Task should_deliver_an_update_while_the_stream_is_open_behind_the_frameworks_outer_middleware()
    {
        // given - response compression and the no-cache default are in front of the endpoint
        var state = new Shared<(string Title, bool Found)>(("milk", true));
        await using var app = await LiveTestApp.StartAsync(
            application => _MapTodo(application, state),
            AbortToken,
            useHeadless: true
        );
        using var stream = await app.OpenAsync(
            "/todo",
            AbortToken,
            configure: request => request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br")
        );

        // when
        var first = await stream.NextResultAsync(AbortToken);
        state.Value = ("eggs", true);
        await app.InvalidateAsync("todo", AbortToken);
        var second = await stream.NextResultAsync(AbortToken);

        // then
        first.GetProperty("title").GetString().Should().Be("milk");
        second.GetProperty("title").GetString().Should().Be("eggs");
        stream.Response.Content.Headers.ContentEncoding.Should().BeEmpty();
        stream.Response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Theory]
    [InlineData("text/event-stream")]
    [InlineData("application/x-ndjson")]
    public async Task should_refuse_the_stream_with_a_problem_when_the_first_run_returns_text(string accept)
    {
        // given - a handler that returns a string, which the framework writes as text/plain
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/note",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("note");

                            return _TextThatForgesAFailure;
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );

        // when
        using var stream = await app.OpenAsync("/note", AbortToken, accept: accept);

        // then - the status is checked before the body is read, because a stream's body never ends
        stream.Response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = JsonElement.Parse(await stream.Response.Content.ReadAsStringAsync(AbortToken));
        body.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.ResponseNotJson);
        app.Meters.Total("headless.live_queries.subscriptions.active").Should().Be(0);
    }

    [Theory]
    [InlineData("text/event-stream")]
    [InlineData("application/x-ndjson")]
    public async Task should_end_the_stream_with_one_failure_when_a_rerun_returns_text(string accept)
    {
        // given - a stream that opened on JSON
        var text = new Shared<bool>(false);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/note",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("note");

                            return text.Value
                                ? Results.Text(_TextThatForgesAFailure)
                                : Results.Json(new { title = "milk" });
                        }
                    )
                    .WithLiveUpdates(),
            AbortToken
        );
        using var stream = await app.OpenAsync("/note", AbortToken, accept: accept);
        var first = await _NextAsync(stream, accept);

        // when - the handler's text would close the JSON string the stream wraps it in and add a member
        text.Value = true;
        await app.InvalidateAsync("note", AbortToken);
        var failure = await _NextAsync(stream, accept);

        // then - the failure is checked before the end is awaited, because a stream that stayed open has none
        first!.Value.Kind.Should().Be("result");
        failure!.Value.Kind.Should().Be("failure");
        failure.Value.Value.GetProperty("status").GetInt32().Should().Be(500);
        failure
            .Value.Value.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(LiveEndpointsErrorCodes.ResponseNotJson);
        failure.Value.Value.GetRawText().Should().NotContain("milk", "the handler's text never reaches the stream");
        (await _NextAsync(stream, accept)).Should().BeNull();
    }

    /// <summary>Returns the next event or line of either built-in format, or <see langword="null" /> at the end.</summary>
    private static async Task<(string Kind, JsonElement Value)?> _NextAsync(LiveStreamClient stream, string accept)
    {
        if (string.Equals(accept, "text/event-stream", StringComparison.Ordinal))
        {
            return await stream.NextEventAsync(AbortToken) is { } next ? (next.Name!, next.Json) : null;
        }

        if (await stream.NextLineAsync(AbortToken) is not { } line)
        {
            return null;
        }

        var json = JsonElement.Parse(line);

        return (json.GetProperty("kind").GetString()!, json.GetProperty("value"));
    }

    private const string _TextThatForgesAFailure = "milk\",\"kind\":\"failure";

    private static void _IndentJson(WebApplicationBuilder builder)
    {
        builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.WriteIndented = true);
    }

    private static void _MapTodo(WebApplication application, Shared<(string Title, bool Found)> state)
    {
        application
            .MapGet(
                "/todo",
                (ILiveQueryDependencies dependencies) =>
                {
                    dependencies.DependsOn("todo");
                    var (title, found) = state.Value;

                    return found ? Results.Ok(new { title, tags = new[] { "a", "b" } }) : Results.NotFound();
                }
            )
            .WithLiveUpdates();
    }
}

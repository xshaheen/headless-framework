// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class LiveCommandAcknowledgementTests : TestBase
{
    // Exactly the shortest session id the server accepts, and another client's.
    private const string _Session = "session-a-0123456789ab";
    private const string _OtherSession = "session-b-0123456789ab";

    [Fact]
    public async Task should_acknowledge_a_command_on_the_senders_streams_and_on_no_other_clients_stream()
    {
        // given - streams on the same list: the sender's, and one for each way of being somebody else
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var sender = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        using var sameUserOtherClient = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _OtherSession);
        using var otherUserSameSession = await app.OpenAsync("/todos", AbortToken, user: "bob", session: _Session);
        using var anonymousSameSession = await app.OpenAsync("/todos", AbortToken, session: _Session);
        using var senderWithoutSession = await app.OpenAsync("/todos", AbortToken, user: "ada");
        LiveStreamClient[] others =
        [
            sameUserOtherClient,
            otherUserSameSession,
            anonymousSameSession,
            senderWithoutSession,
        ];

        await sender.NextResultAsync(AbortToken);

        foreach (var other in others)
        {
            await other.NextResultAsync(AbortToken);
        }

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "ada",
            session: _Session,
            command: "command-1"
        );

        // then - every stream receives the new list, and only the sender's says whose command it holds
        response.Headers.GetValues(LiveTestApp.AcknowledgementHeader).Should().Equal("pending");
        var acknowledged = await sender.NextEventAsync(AbortToken);
        acknowledged!.Name.Should().Be("result");
        acknowledged.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
        acknowledged.AcknowledgedCommands.Should().Equal("command-1");

        foreach (var other in others)
        {
            var update = await other.NextEventAsync(AbortToken);
            update!.Name.Should().Be("result");
            update.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
            update.Id.Should().MatchRegex("^[0-9a-f]+$");
        }
    }

    [Fact]
    public async Task should_not_acknowledge_a_command_on_a_stream_of_the_same_user_id_in_another_tenant()
    {
        // given - one user id and one session id in two tenants, each with a stream on the same list
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        app.Users.SetWithId("one-in-a", userId: "1", (UserClaimTypes.TenantId, "A"));
        app.Users.SetWithId("one-in-b", userId: "1", (UserClaimTypes.TenantId, "B"));
        using var streamOfA = await app.OpenAsync("/todos", AbortToken, user: "one-in-a", session: _Session);
        using var streamOfB = await app.OpenAsync("/todos", AbortToken, user: "one-in-b", session: _Session);
        await streamOfA.NextResultAsync(AbortToken);
        await streamOfB.NextResultAsync(AbortToken);

        // when - tenant A's user sends the command
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "one-in-a",
            session: _Session,
            command: "command-1"
        );
        var updateOfA = await streamOfA.NextEventAsync(AbortToken);
        var updateOfB = await streamOfB.NextEventAsync(AbortToken);

        // then - both streams read the list, and only tenant A's is told whose command changed it
        response.Headers.GetValues(LiveTestApp.AcknowledgementHeader).Should().Equal("pending");
        updateOfA!.Name.Should().Be("result");
        updateOfA.AcknowledgedCommands.Should().Equal("command-1");
        updateOfB!.Name.Should().Be("result");
        updateOfB.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
        updateOfB.Id.Should().MatchRegex("^[0-9a-f]+$");
        updateOfB.AcknowledgedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task should_acknowledge_with_the_result_where_it_changed_and_alone_on_the_senders_other_streams()
    {
        // given - the sender watches the list its command changes and one it does not; so does another client
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        using var notes = await app.OpenAsync("/notes", AbortToken, user: "ada", session: _Session);
        using var otherNotes = await app.OpenAsync("/notes", AbortToken, user: "bob", session: _OtherSession);
        await todos.NextResultAsync(AbortToken);
        await notes.NextResultAsync(AbortToken);
        await otherNotes.NextResultAsync(AbortToken);

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "ada",
            session: _Session,
            command: "command-1"
        );
        var changed = await todos.NextEventAsync(AbortToken);
        var bare = await notes.NextEventAsync(AbortToken);
        var runs = app.Meters.Total("headless.live_queries.runs");

        // then - one event carries the new result and the id; the untouched stream gets the id alone, unrun
        changed!.Name.Should().Be("result");
        changed.Id.Should().MatchRegex("^[0-9a-f]+;command-1$");
        changed.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
        bare!.Name.Should().Be("acknowledged");
        bare.Id.Should().BeNull();
        bare.Data.Should().Equal("""["command-1"]""");
        runs.Should().Be(4, "three first runs and the re-run of the list the command changed");

        // and the other client's stream was sent nothing for it: its next event is a later change of its own list
        lists.Notes.Value = ["later"];
        await app.InvalidateAsync("notes", AbortToken);

        foreach (var stream in new[] { notes, otherNotes })
        {
            var later = await stream.NextEventAsync(AbortToken);
            later!.Name.Should().Be("result");
            later.Id.Should().MatchRegex("^[0-9a-f]+$");
        }
    }

    [Fact]
    public async Task should_send_only_the_acknowledgement_when_the_senders_rerun_returns_the_result_already_sent()
    {
        // given - a command id of the greatest length the server accepts
        var command = new string('c', 64);
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        await todos.NextResultAsync(AbortToken);

        // when - the command announces the list without changing what the endpoint returns
        using var response = await app.SendCommandAsync(
            "/todos/touch",
            AbortToken,
            user: "ada",
            session: _Session,
            command: command
        );
        var next = await todos.NextEventAsync(AbortToken);

        // then
        next!.Name.Should().Be("acknowledged");
        next.AcknowledgedCommands.Should().Equal(command);
        app.Meters.Total("headless.live_queries.runs").Should().Be(2, "the stream ran again to learn nothing changed");
    }

    [Fact]
    public async Task should_send_nothing_for_an_identical_rerun_with_no_acknowledgement_to_deliver()
    {
        // given
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        await todos.NextResultAsync(AbortToken);

        // when - another client's tagged command and an untagged write re-run the stream to the same result
        using var response = await app.SendCommandAsync(
            "/todos/touch",
            AbortToken,
            user: "bob",
            session: _OtherSession,
            command: "command-1"
        );
        await app.InvalidateAsync("todos", AbortToken);

        // then - the next thing on the wire is a later change, with nobody's command on it
        lists.Todos.Value = ["bread"];
        await app.InvalidateAsync("todos", AbortToken);
        var next = await todos.NextEventAsync(AbortToken);
        next!.Name.Should().Be("result");
        next.Json.EnumerateArray().Select(todo => todo.GetString()).Should().Equal("bread");
        next.Id.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public async Task should_ignore_a_session_id_shorter_than_22_characters()
    {
        // given - a client whose streams and command carry the same session id, one character too short
        var session = _Session[..21];
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: session);
        using var notes = await app.OpenAsync("/notes", AbortToken, user: "ada", session: session);
        await todos.NextResultAsync(AbortToken);
        await notes.NextResultAsync(AbortToken);

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "ada",
            session: session,
            command: "command-1"
        );

        // then - the command ran, and nothing says so but the new list
        response.Headers.Contains(LiveTestApp.AcknowledgementHeader).Should().BeFalse();
        await _ShouldReceivePlainResultAsync(todos, "milk", "eggs");
        lists.Notes.Value = ["later"];
        await app.InvalidateAsync("notes", AbortToken);
        await _ShouldReceivePlainResultAsync(notes, "later");
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("semi;colon")]
    [InlineData("com,ma")]
    [InlineData("quo\"te")]
    [InlineData("ccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    public async Task should_ignore_a_command_id_that_is_not_a_token_of_at_most_64_characters(string command)
    {
        // given
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        using var notes = await app.OpenAsync("/notes", AbortToken, user: "ada", session: _Session);
        await todos.NextResultAsync(AbortToken);
        await notes.NextResultAsync(AbortToken);

        // when
        var headers = await app.SendRawCommandAsync("/todos/eggs", "ada", _Session, command, AbortToken);

        // then - the command ran, and the id reached no stream
        headers.Should().NotContainKey(LiveTestApp.AcknowledgementHeader);
        await _ShouldReceivePlainResultAsync(todos, "milk", "eggs");
        lists.Notes.Value = ["later"];
        await app.InvalidateAsync("notes", AbortToken);
        await _ShouldReceivePlainResultAsync(notes, "later");
    }

    [Fact]
    public async Task should_acknowledge_on_no_stream_and_say_so_when_a_command_writes_nothing()
    {
        // given
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        await todos.NextResultAsync(AbortToken);

        // when
        using var response = await app.SendCommandAsync(
            "/nothing",
            AbortToken,
            user: "ada",
            session: _Session,
            command: "command-1"
        );

        // then
        response.Headers.GetValues(LiveTestApp.AcknowledgementHeader).Should().Equal("none");
        lists.Todos.Value = ["bread"];
        await app.InvalidateAsync("todos", AbortToken);
        await _ShouldReceivePlainResultAsync(todos, "bread");
    }

    [Fact]
    public async Task should_report_the_write_in_the_response_when_the_middleware_is_added_twice()
    {
        // given - a pipeline that calls UseHeadlessLiveCommands a second time
        var lists = new Lists();
        await using var app = await _StartAsync(
            lists,
            afterAuthentication: application => application.UseHeadlessLiveCommands()
        );
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", session: _Session);
        await todos.NextResultAsync(AbortToken);

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "ada",
            session: _Session,
            command: "command-1"
        );
        var next = await todos.NextEventAsync(AbortToken);

        // then - one middleware ran: a second would have seen no write and answered for the first
        response.Headers.GetValues(LiveTestApp.AcknowledgementHeader).Should().Equal("pending");
        next!.AcknowledgedCommands.Should().Equal("command-1");
    }

    [Fact]
    public async Task should_acknowledge_an_anonymous_command_only_on_streams_with_the_same_session_id()
    {
        // given
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var sender = await app.OpenAsync("/notes", AbortToken, session: _Session);
        using var otherAnonymous = await app.OpenAsync("/notes", AbortToken, session: _OtherSession);
        using var signedInSameSession = await app.OpenAsync("/notes", AbortToken, user: "ada", session: _Session);
        await sender.NextResultAsync(AbortToken);
        await otherAnonymous.NextResultAsync(AbortToken);
        await signedInSameSession.NextResultAsync(AbortToken);

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            session: _Session,
            command: "command-1"
        );
        var acknowledged = await sender.NextEventAsync(AbortToken);

        // then
        acknowledged!.Name.Should().Be("acknowledged");
        acknowledged.AcknowledgedCommands.Should().Equal("command-1");
        lists.Notes.Value = ["later"];
        await app.InvalidateAsync("notes", AbortToken);
        await _ShouldReceivePlainResultAsync(sender, "later");
        await _ShouldReceivePlainResultAsync(otherAnonymous, "later");
        await _ShouldReceivePlainResultAsync(signedInSameSession, "later");
    }

    [Fact]
    public async Task should_carry_acknowledgements_in_newline_delimited_json_as_a_field_and_as_a_line_kind()
    {
        // given
        const string ndjson = "application/x-ndjson";
        var lists = new Lists();
        await using var app = await _StartAsync(lists);
        using var todos = await app.OpenAsync("/todos", AbortToken, user: "ada", accept: ndjson, session: _Session);
        using var notes = await app.OpenAsync("/notes", AbortToken, user: "ada", accept: ndjson, session: _Session);
        var first = JsonElement.Parse((await todos.NextLineAsync(AbortToken))!);
        await notes.NextLineAsync(AbortToken);

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "ada",
            session: _Session,
            command: "command-1"
        );
        var changed = JsonElement.Parse((await todos.NextLineAsync(AbortToken))!);
        var bare = await notes.NextLineAsync(AbortToken);

        // then
        first.TryGetProperty("acknowledged", out _).Should().BeFalse();
        changed.GetProperty("kind").GetString().Should().Be("result");
        changed.GetProperty("acknowledged").EnumerateArray().Select(id => id.GetString()).Should().Equal("command-1");
        changed.GetProperty("value").EnumerateArray().Select(todo => todo.GetString()).Should().Equal("milk", "eggs");
        bare.Should().Be("""{"kind":"acknowledged","acknowledged":["command-1"]}""");
    }

    [Fact]
    public async Task should_hand_a_custom_transport_the_acknowledged_commands()
    {
        // given
        var lists = new Lists();
        await using var app = await _StartAsync(lists, setup => setup.AddTransport<TestLiveTransport>());
        using var todos = await app.OpenAsync(
            "/todos",
            AbortToken,
            user: "ada",
            accept: TestLiveTransport.MediaType,
            session: _Session
        );
        using var notes = await app.OpenAsync(
            "/notes",
            AbortToken,
            user: "ada",
            accept: TestLiveTransport.MediaType,
            session: _Session
        );
        await todos.NextLineAsync(AbortToken);
        var firstNotes = (await notes.NextLineAsync(AbortToken))!.Split('|');

        // when
        using var response = await app.SendCommandAsync(
            "/todos/eggs",
            AbortToken,
            user: "ada",
            session: _Session,
            command: "command-1"
        );
        var changed = (await todos.NextLineAsync(AbortToken))!.Split('|');
        var bare = (await notes.NextLineAsync(AbortToken))!.Split('|');

        // then - the bare acknowledgement names the result it follows and carries no payload
        changed[0].Should().Be(nameof(LiveUpdateKind.Changed));
        changed[2].Should().Be("command-1");
        bare[0].Should().Be(nameof(LiveUpdateKind.Acknowledged));
        bare[1].Should().Be(firstNotes[1]);
        bare[2].Should().Be("command-1");
        bare[3].Should().BeEmpty();
    }

    private static async Task _ShouldReceivePlainResultAsync(LiveStreamClient stream, params string[] expected)
    {
        var next = await stream.NextEventAsync(AbortToken);
        next!.Name.Should().Be("result", "the stream sent: {0}", string.Join('\n', next.Data));
        next.Id.Should().MatchRegex("^[0-9a-f]+$");
        next.Json.EnumerateArray().Select(item => item.GetString()).Should().Equal(expected);
    }

    private static async Task<LiveTestApp> _StartAsync(
        Lists lists,
        Action<HeadlessLiveQueriesSetupBuilder>? configureSetup = null,
        Action<WebApplication>? afterAuthentication = null
    )
    {
        var app = await LiveTestApp.StartAsync(
            application =>
            {
                application
                    .MapGet(
                        "/todos",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("todos");

                            return lists.Todos.Value;
                        }
                    )
                    .WithLiveUpdates();

                application
                    .MapGet(
                        "/notes",
                        (ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("notes");

                            return lists.Notes.Value;
                        }
                    )
                    .WithLiveUpdates();

                // Announces the list without changing it.
                application.MapPost(
                    "/todos/touch",
                    async (ILiveQueryInvalidator invalidator, CancellationToken cancellationToken) =>
                    {
                        await invalidator.InvalidateAsync(["todos"], cancellationToken);

                        return Results.NoContent();
                    }
                );

                application.MapPost(
                    "/todos/{title}",
                    async (string title, ILiveQueryInvalidator invalidator, CancellationToken cancellationToken) =>
                    {
                        lists.Todos.Value = [.. lists.Todos.Value, title];
                        await invalidator.InvalidateAsync(["todos"], cancellationToken);

                        return Results.NoContent();
                    }
                );

                application.MapPost("/nothing", () => Results.NoContent());
            },
            AbortToken,
            configureSetup: configureSetup,
            afterAuthentication: afterAuthentication
        );

        app.Users.Set("ada");
        app.Users.Set("bob");

        return app;
    }

    /// <summary>The two lists the endpoints serve, which a test changes behind them.</summary>
    private sealed class Lists
    {
        public Shared<string[]> Todos { get; } = new(["milk"]);

        public Shared<string[]> Notes { get; } = new(["first"]);
    }
}

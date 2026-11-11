// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.LiveQueries;
using Headless.LiveQueries.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Drives the hub the way live endpoints do: subscriptions that name who holds them, and writes announced on the
/// flow of a tagged command.
/// </summary>
public sealed class LiveQueryHubAcknowledgementTests : TestBase
{
    private const string _Session = "session-a-0123456789ab";
    private const string _OtherSession = "session-b-0123456789ab";

    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_acknowledge_a_command_only_to_subscriptions_of_the_same_session_and_user()
    {
        // given - one subscription for the sender and one for each way of being somebody else
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("todos");
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;
        await using var sender = await _WatchAsync(hub, runs, ada);
        await using var sameUserOtherSession = await _WatchAsync(
            hub,
            runs,
            LiveSubscriber.TryCreate(_OtherSession, "ada")
        );
        await using var otherUserSameSession = await _WatchAsync(hub, runs, LiveSubscriber.TryCreate(_Session, "bob"));
        await using var anonymousSameSession = await _WatchAsync(
            hub,
            runs,
            LiveSubscriber.TryCreate(_Session, user: null)
        );
        await using var nobody = await _WatchAsync(hub, runs, subscriber: null);

        // when
        runs.Result = "[1]";
        var command = _Send(hub, ada, "command-1", "todos");

        // then
        command.Announced.Should().BeTrue();
        var acknowledged = await _NextAsync(sender);
        acknowledged.Kind.Should().Be(LiveUpdateKind.Changed);
        acknowledged.AcknowledgedCommands.Should().Equal("command-1");

        foreach (var other in new[] { sameUserOtherSession, otherUserSameSession, anonymousSameSession, nobody })
        {
            var update = await _NextAsync(other);
            update.Kind.Should().Be(LiveUpdateKind.Changed);
            update.AcknowledgedCommands.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task should_acknowledge_an_anonymous_command_only_to_anonymous_subscriptions_of_its_session()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("todos");
        var anonymous = LiveSubscriber.TryCreate(_Session, user: null)!;
        await using var sender = await _WatchAsync(hub, runs, anonymous);
        await using var otherSession = await _WatchAsync(
            hub,
            runs,
            LiveSubscriber.TryCreate(_OtherSession, user: null)
        );
        await using var signedIn = await _WatchAsync(hub, runs, LiveSubscriber.TryCreate(_Session, "ada"));

        // when
        runs.Result = "[1]";
        _Send(hub, anonymous, "command-1", "todos");

        // then
        (await _NextAsync(sender))
            .AcknowledgedCommands.Should()
            .Equal("command-1");
        (await _NextAsync(otherSession)).AcknowledgedCommands.Should().BeEmpty();
        (await _NextAsync(signedIn)).AcknowledgedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task should_acknowledge_alone_and_without_a_run_when_the_write_touched_nothing_the_query_read()
    {
        // given - the sender watches notes, and its command writes todos
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("notes");
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;
        await using var updates = await _WatchAsync(hub, runs, ada);
        var first = updates.Current;

        // when
        _Send(hub, ada, "command-1", "todos");
        var update = await _NextAsync(updates);

        // then - no result travels, and the token is still the last result's
        update.Kind.Should().Be(LiveUpdateKind.Acknowledged);
        update.AcknowledgedCommands.Should().Equal("command-1");
        update.Payload.IsEmpty.Should().BeTrue();
        update.Version.Should().Be(first.Version);
        runs.Count.Should().Be(1);
        host.Meters.Total("headless.live_queries.wakes").Should().Be(0);
    }

    [Fact]
    public async Task should_acknowledge_alone_when_the_run_after_the_write_returns_the_result_already_yielded()
    {
        // given
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("todos");
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;
        await using var updates = await _WatchAsync(hub, runs, ada);
        var first = updates.Current;

        // when - the command writes what the query read, and the query returns what it returned before
        _Send(hub, ada, "command-1", "todos");
        var update = await _NextAsync(updates);

        // then
        update.Kind.Should().Be(LiveUpdateKind.Acknowledged);
        update.AcknowledgedCommands.Should().Equal("command-1");
        update.Version.Should().Be(first.Version);
        runs.Count.Should().Be(2);
    }

    [Fact]
    public async Task should_hold_an_acknowledgement_for_the_run_that_started_after_the_write()
    {
        // given - a run in flight that read the list before the command wrote it
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("todos");
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;
        await using var updates = await _WatchAsync(hub, runs, ada);
        runs.Result = "[1]";
        var held = runs.HoldNextRun();
        hub.Invalidate(new LiveWriteSet(["todos"], []));
        var next = updates.MoveNextAsync().AsTask();
        await held.Entered.WaitAsync(_Patience, AbortToken);

        // when - the command commits while that run is still out
        runs.Result = "[1,2]";
        _Send(hub, ada, "command-1", "todos");
        held.Release();
        (await next.WaitAsync(_Patience, AbortToken)).Should().BeTrue();
        var before = updates.Current;
        var after = await _NextAsync(updates);

        // then - the result read before the write says nothing of the command; the next one holds it
        Encoding.UTF8.GetString(before.Payload.Span).Should().Be("[1]");
        before.AcknowledgedCommands.Should().BeEmpty();
        Encoding.UTF8.GetString(after.Payload.Span).Should().Be("[1,2]");
        after.AcknowledgedCommands.Should().Equal("command-1");
    }

    [Fact]
    public async Task should_acknowledge_every_command_a_run_holds_in_one_update_and_a_repeated_command_once()
    {
        // given - a run in flight while three writes of two commands commit
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("todos");
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;
        await using var updates = await _WatchAsync(hub, runs, ada);
        var held = runs.HoldNextRun();
        hub.Invalidate(new LiveWriteSet(["todos"], []));
        var next = updates.MoveNextAsync().AsTask();
        await held.Entered.WaitAsync(_Patience, AbortToken);

        // when - the first command commits a second time after the other one
        _Send(hub, ada, "command-1", "todos");
        _Send(hub, ada, "command-2", "todos");
        _Send(hub, ada, "command-1", "todos");
        held.Release();
        (await next.WaitAsync(_Patience, AbortToken)).Should().BeTrue();

        // then - one update, after the run that followed all three, with each command once
        updates.Current.Kind.Should().Be(LiveUpdateKind.Acknowledged);
        updates.Current.AcknowledgedCommands.Should().Equal("command-2", "command-1");
        runs.Count.Should().Be(3);
    }

    [Fact]
    public async Task should_acknowledge_nothing_for_a_write_announced_after_the_command_ended()
    {
        // given - work the command left running, which still carries the command as its ambient state
        await using var host = new LiveQueriesTestHost();
        var hub = _Hub(host);
        var runs = new KeyRun("todos");
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;
        await using var updates = await _WatchAsync(hub, runs, ada);
        var command = LiveCommandContext.TryCreate(ada, "command-1")!;

        // when
        runs.Result = "[1]";
        ExecutionContext.Run(
            ExecutionContext.Capture()!,
            _ =>
            {
                command.Enter();
                command.End();
                hub.Invalidate(new LiveWriteSet(["todos"], []));
            },
            state: null
        );
        var update = await _NextAsync(updates);

        // then
        update.Kind.Should().Be(LiveUpdateKind.Changed);
        update.AcknowledgedCommands.Should().BeEmpty();
        command.Announced.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_count_an_empty_write_set_as_a_write_of_the_command()
    {
        await using var host = new LiveQueriesTestHost();
        var ada = LiveSubscriber.TryCreate(_Session, "ada")!;

        var command = _Send(_Hub(host), ada, "command-1");

        command.Announced.Should().BeFalse();
    }

    [Theory]
    [InlineData("0123456789abcdefghijk", false)] // 21 characters
    [InlineData("0123456789abcdefghijkl", true)] // 22 characters
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ-abcdefghijklmnopqrstuvwxyz-0123456789", true)] // 64 characters
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ-abcdefghijklmnopqrstuvwxyz-0123456789x", false)] // 65 characters
    [InlineData("0123456789abcdefghijkl_", false)]
    [InlineData("0123456789abcdefghijklé", false)]
    [InlineData("0123456789 abcdefghijkl", false)]
    [InlineData(null, false)]
    public void should_accept_a_session_id_only_as_a_token_of_22_to_64_characters(string? session, bool accepted)
    {
        (LiveSubscriber.TryCreate(session, "ada") is not null).Should().Be(accepted);
    }

    [Theory]
    [InlineData("c", true)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ-abcdefghijklmnopqrstuvwxyz-0123456789", true)] // 64 characters
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ-abcdefghijklmnopqrstuvwxyz-0123456789x", false)] // 65 characters
    [InlineData("", false)]
    [InlineData("two words", false)]
    [InlineData("line\nbreak", false)]
    [InlineData("quo\"te", false)]
    [InlineData("com,ma", false)]
    [InlineData("semi;colon", false)]
    [InlineData("١٢٣", false)] // digits, but not ASCII ones
    [InlineData(null, false)]
    public void should_accept_a_command_id_only_as_a_token_of_at_most_64_characters(string? command, bool accepted)
    {
        var sender = LiveSubscriber.TryCreate(_Session, "ada");

        (LiveCommandContext.TryCreate(sender, command) is not null).Should().Be(accepted);
        LiveCommandContext.TryCreate(sender: null, "command-1").Should().BeNull("a command needs a sender");
    }

    private static LiveQueryHub _Hub(LiveQueriesTestHost host) => host.Services.GetRequiredService<LiveQueryHub>();

    /// <summary>Opens a subscription and returns it once its first result has arrived.</summary>
    private static async Task<IAsyncEnumerator<LiveUpdate>> _WatchAsync(
        LiveQueryHub hub,
        KeyRun runs,
        LiveSubscriber? subscriber
    )
    {
        var updates = hub.WatchAsync(runs.RunAsync, enterRerun: null, subscriber, AbortToken)
            .GetAsyncEnumerator(AbortToken);
        await _NextAsync(updates);

        return updates;
    }

    /// <summary>
    /// Announces <paramref name="keys" /> as a write of a command, the way a request does: the command is ambient
    /// for the announcement and for nothing after it.
    /// </summary>
    private static LiveCommandContext _Send(
        LiveQueryHub hub,
        LiveSubscriber sender,
        string commandId,
        params string[] keys
    )
    {
        var command = LiveCommandContext.TryCreate(sender, commandId)!;

        ExecutionContext.Run(
            ExecutionContext.Capture()!,
            _ =>
            {
                command.Enter();
                hub.Invalidate(new LiveWriteSet(keys, []));
                command.End();
            },
            state: null
        );

        return command;
    }

    private static async Task<LiveUpdate> _NextAsync(IAsyncEnumerator<LiveUpdate> updates)
    {
        (await updates.MoveNextAsync().AsTask().WaitAsync(_Patience, AbortToken)).Should().BeTrue();

        return updates.Current;
    }

    /// <summary>A query that read every row of one key, and whose next run a test can stop in flight.</summary>
    private sealed class KeyRun(string key)
    {
        private HeldRun? _held;

        public string Result { get; set; } = "[]";

        public int Count { get; private set; }

        public HeldRun HoldNextRun() => _held = new HeldRun();

        public async ValueTask<LiveRunResult> RunAsync(CancellationToken cancellationToken)
        {
            Count++;
            var recorder = new LiveQueryRecorder();
            recorder.Add([key]);

            // Read before the hold: a run returns what the data was when it read it.
            var result = Result;

            if (_held is { } held)
            {
                _held = null;
                await held.WaitAsync(cancellationToken);
            }

            return new LiveRunResult(Encoding.UTF8.GetBytes(result), Failed: false, recorder.Snapshot());
        }
    }
}

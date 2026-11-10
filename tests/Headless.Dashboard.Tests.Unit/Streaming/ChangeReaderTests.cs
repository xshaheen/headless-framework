// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Streaming;

/// <summary>
/// Reading each module's changes from its store: only while a stream is open, once per object and version, and on
/// past a failing store without closing the stream.
/// </summary>
public sealed class ChangeReaderTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChangeStore _store;

    public ChangeReaderTests()
    {
        _store = new FakeChangeStore(_time);
    }

    [Fact]
    public async Task should_read_no_changes_while_no_stream_is_open_and_read_from_the_first_open_to_the_last_close()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();

        await FakeClock.AdvanceByAsync(_time, TimeSpan.FromSeconds(10), AbortToken);
        _store.StartCount.Should().Be(0);
        _store.ReadCount.Should().Be(0);

        var first = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await FakeClock.WithinAsync(_store.Started(1), AbortToken);
        await FakeClock.AdvanceUntilAsync(_time, () => _store.ReadCount >= 1, AbortToken);

        // A second stream shares the running reader, and closing the first leaves it running.
        await using var second = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await first.DisposeAsync();
        var readsWithOneStream = _store.ReadCount;
        await FakeClock.AdvanceUntilAsync(_time, () => _store.ReadCount > readsWithOneStream, AbortToken);
        _store.StartCount.Should().Be(1);
        _store.Stopped(1).IsCompleted.Should().BeFalse();

        await second.DisposeAsync();
        await FakeClock.WithinAsync(_store.Stopped(1), AbortToken);
        var readsAtStop = _store.ReadCount;

        await FakeClock.AdvanceByAsync(_time, TimeSpan.FromSeconds(10), AbortToken);
        _store.ReadCount.Should().Be(readsAtStop);
        _store.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task should_send_one_event_when_the_overlap_window_returns_the_same_object_and_version_twice()
    {
        _store.Script("diag", FakeChangeStore.Change("diag", "run", "r-1", "5"));
        _store.Script(
            "diag",
            FakeChangeStore.Change("diag", "run", "r-1", "5"),
            FakeChangeStore.Change("diag", "run", "r-2", "1")
        );

        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        var first = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var second = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        first.Json.GetProperty("id").GetString().Should().Be("r-1");
        second.Json.GetProperty("id").GetString().Should().Be("r-2");
    }

    [Fact]
    public async Task should_drop_an_older_version_that_arrives_after_a_newer_one()
    {
        // "10" after "9" is newer: versions that parse as integers compare as integers.
        _store.Script("diag", FakeChangeStore.Change("diag", "run", "r-1", "10"));
        _store.Script(
            "diag",
            FakeChangeStore.Change("diag", "run", "r-1", "9"),
            FakeChangeStore.Change("diag", "run", "r-2", "1")
        );

        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        var first = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var second = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        first.Json.GetProperty("version").GetString().Should().Be("10");
        second.Json.GetProperty("id").GetString().Should().Be("r-2");
    }

    [Fact]
    public async Task should_mark_the_module_stale_back_off_and_keep_the_stream_open_while_its_store_fails()
    {
        _store.Failing = true;

        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        var stale = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        stale.Type.Should().Be("module.stale");
        stale.Json.GetProperty("module").GetString().Should().Be("diag");
        stale.Json.GetProperty("stale").GetBoolean().Should().BeTrue();

        // Without back-off the tries would be one interval apart; they are two, then four, intervals apart.
        var step = TimeSpan.FromMilliseconds(250);
        await FakeClock.AdvanceUntilAsync(_time, () => _store.FailureTimes.Count >= 3, step, AbortToken);
        var failures = _store.FailureTimes;
        var interval = TimeSpan.FromSeconds(2);
        (failures[1] - failures[0]).Should().BeGreaterThan(interval * 1.5);
        (failures[2] - failures[1]).Should().BeGreaterThan(interval * 3.5);

        // Still open: heartbeats keep coming while the module is stale.
        var heartbeat = await FakeClock.AdvanceUntilAsync(
            _time,
            stream.NextOfTypeAsync("heartbeat", AbortToken),
            step,
            AbortToken
        );
        heartbeat.Type.Should().Be("heartbeat");

        _store.Failing = false;
        var recovered = await FakeClock.AdvanceUntilAsync(_time, _NextFreshAsync(stream), step, AbortToken);
        recovered.Json.GetProperty("module").GetString().Should().Be("diag");
    }

    [Fact]
    public async Task should_tell_needs_you_to_refresh_when_a_read_change_affects_attention()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);
        await FakeClock.WithinAsync(_store.Started(1), AbortToken);

        _store.Write("diag", "run", "r-1", affectsAttention: true);

        var change = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var attention = await FakeClock.AdvanceUntilAsync(
            _time,
            stream.NextExceptHeartbeatAsync(AbortToken),
            AbortToken
        );

        change.Type.Should().Be("change");
        attention.Type.Should().Be("attention.changed");
        attention.Json.GetProperty("module").GetString().Should().Be("diag");
    }

    [Fact]
    public async Task should_reset_a_reconnect_from_before_the_readers_stopped_and_then_send_the_change_made_meanwhile()
    {
        // The store re-reads a few sequences behind the start cursor, as a real store's overlap window does.
        _store.Overlap = 5;

        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();

        string lastSeen;
        await using (var first = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken))
        {
            await first.NextAsync(AbortToken);
            await FakeClock.WithinAsync(_store.Started(1), AbortToken);
            _store.Write("diag", "run", "r-1");

            var seen = await FakeClock.AdvanceUntilAsync(_time, first.NextExceptHeartbeatAsync(AbortToken), AbortToken);
            seen.Json.GetProperty("version").GetString().Should().Be("1");
            lastSeen = seen.Id!;
        }

        await FakeClock.WithinAsync(_store.Stopped(1), AbortToken);
        _store.Write("diag", "run", "r-1");

        await using var reconnected = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.run"),
            AbortToken,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Last-Event-ID"] = lastSeen }
        );

        (await reconnected.NextAsync(AbortToken))!.Type.Should().Be("subscribed");
        (await reconnected.NextAsync(AbortToken))!.Type.Should().Be("reset");

        var change = await FakeClock.AdvanceUntilAsync(
            _time,
            reconnected.NextExceptHeartbeatAsync(AbortToken),
            AbortToken
        );
        change.Json.GetProperty("id").GetString().Should().Be("r-1");
        change.Json.GetProperty("version").GetString().Should().Be("2");
    }

    // The module's stale state can arrive twice, from the stream's opening snapshot and from the ring.
    private static async Task<SseEvent> _NextFreshAsync(SseStream stream)
    {
        while (true)
        {
            var next = await stream.NextOfTypeAsync("module.stale", AbortToken);

            if (!next.Json.GetProperty("stale").GetBoolean())
            {
                return next;
            }
        }
    }
}

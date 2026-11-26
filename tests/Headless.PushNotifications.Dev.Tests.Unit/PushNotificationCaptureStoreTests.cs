// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class PushNotificationCaptureStoreTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 14, 20, 31, TimeSpan.Zero));

    [Fact]
    public void should_stamp_id_sequence_and_capture_time_when_adding()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);

        // when
        var first = store.Add(_Draft("first"));
        _time.Advance(TimeSpan.FromSeconds(5));
        var second = store.Add(_Draft("second"));

        // then
        first.Id.Should().NotBe(Guid.Empty);
        second.Id.Should().NotBe(first.Id);
        first.Sequence.Should().Be(1);
        second.Sequence.Should().Be(2);
        first.CapturedAt.Should().Be(new DateTimeOffset(2026, 11, 4, 14, 20, 31, TimeSpan.Zero));
        second.CapturedAt.Should().Be(first.CapturedAt.AddSeconds(5));
        store.Version.Should().Be(2);
        store.Count.Should().Be(2);
    }

    [Fact]
    public void should_list_newest_first()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);
        store.Add(_Draft("a"));
        store.Add(_Draft("b"));
        store.Add(_Draft("c"));

        // when
        var result = store.Query();

        // then
        result.Select(x => x.Title).Should().Equal("c", "b", "a");
    }

    [Fact]
    public void should_drop_the_oldest_when_full()
    {
        // given
        var store = new PushNotificationCaptureStore(2, _time);

        // when
        store.Add(_Draft("a"));
        store.Add(_Draft("b"));
        store.Add(_Draft("c"));

        // then
        store.Capacity.Should().Be(2);
        store.Count.Should().Be(2);
        store.Query().Select(x => x.Title).Should().Equal("c", "b");
    }

    [Fact]
    public void should_filter_by_sender_search_sequence_and_limit()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);
        store.Add(_Draft("Order shipped") with { SenderName = "ios" });
        var second = store.Add(_Draft("Sale") with { Body = "Half PRICE today" });
        store.Add(_Draft("Ping") with { ClientIdentifiers = ["token-abc"] });
        store.Add(
            _Draft("") with
            {
                Title = null,
                Body = null,
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "inbox" },
            }
        );

        // then
        store.Query(new() { SenderName = "ios" }).Select(x => x.Title).Should().Equal("Order shipped");
        store.Query(new() { SenderName = "" }).Should().HaveCount(3);
        store.Query(new() { Search = "price" }).Select(x => x.Title).Should().Equal("Sale");
        store.Query(new() { Search = "token-abc" }).Select(x => x.Title).Should().Equal("Ping");
        store.Query(new() { Search = "sync" }).Should().ContainSingle().Which.Data.Should().ContainKey("sync");
        store.Query(new() { Search = "inbox" }).Should().ContainSingle();
        store.Query(new() { Search = "shipped" }).Select(x => x.Title).Should().Equal("Order shipped");
        store.Query(new() { Search = "absent" }).Should().BeEmpty();
        store.Query(new() { AfterSequence = second.Sequence }).Should().HaveCount(2);
        store.Query(new() { Limit = 1 }).Should().ContainSingle().Which.Title.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_non_positive_limit(int limit)
    {
        var store = new PushNotificationCaptureStore(10, _time);

        var act = () => store.Query(new() { Limit = limit });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_get_and_delete_by_id()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);
        var kept = store.Add(_Draft("kept"));
        var removed = store.Add(_Draft("removed"));

        // when
        var deleted = store.Delete(removed.Id);

        // then
        deleted.Should().BeTrue();
        store.Version.Should().Be(3);
        store.Get(kept.Id).Should().BeSameAs(kept);
        store.Get(removed.Id).Should().BeNull();
        store.Delete(removed.Id).Should().BeFalse();
        store.Version.Should().Be(3);
    }

    [Fact]
    public void should_clear_every_item_and_bump_the_version_only_when_something_was_removed()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);
        store.Add(_Draft("a"));
        store.Add(_Draft("b"));

        // when
        var removed = store.Clear();

        // then
        removed.Should().Be(2);
        store.Count.Should().Be(0);
        store.Version.Should().Be(3);
        store.Clear().Should().Be(0);
        store.Version.Should().Be(3);
    }

    [Fact]
    public async Task should_return_at_once_when_the_known_version_is_stale()
    {
        var store = new PushNotificationCaptureStore(10, _time);
        store.Add(_Draft("a"));

        var version = await store.WaitForChangeAsync(0, AbortToken);

        version.Should().Be(1);
    }

    [Fact]
    public async Task should_complete_the_wait_when_an_item_is_captured()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);
        var wait = store.WaitForChangeAsync(store.Version, AbortToken).AsTask();
        wait.IsCompleted.Should().BeFalse();

        // when
        store.Add(_Draft("a"));

        // then
        (await wait)
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_complete_the_wait_when_an_item_is_deleted_or_cleared()
    {
        // given
        var store = new PushNotificationCaptureStore(10, _time);
        var first = store.Add(_Draft("a"));
        store.Add(_Draft("b"));
        var deleteWait = store.WaitForChangeAsync(store.Version, AbortToken).AsTask();

        // when
        store.Delete(first.Id);
        var clearWait = store.WaitForChangeAsync(store.Version, AbortToken).AsTask();
        store.Clear();

        // then
        (await deleteWait)
            .Should()
            .Be(3);
        (await clearWait).Should().Be(4);
    }

    [Fact]
    public async Task should_stop_waiting_when_canceled()
    {
        var store = new PushNotificationCaptureStore(10, _time);
        using var cts = new CancellationTokenSource();
        var wait = store.WaitForChangeAsync(store.Version, cts.Token).AsTask();

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => wait).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void should_reject_a_non_positive_capacity()
    {
        var act = () => new PushNotificationCaptureStore(0, _time);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CapturedPushNotification _Draft(string title)
    {
        return new CapturedPushNotification
        {
            SenderName = "",
            ClientIdentifiers = ["device-1"],
            Title = title,
            Body = "body",
        };
    }
}

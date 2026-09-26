// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using FirebaseAdmin.Messaging;
using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class FcmPushNotificationServiceTests : TestBase
{
    private static readonly DateTimeOffset _Now = new FakeTimeProvider(
        new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)
    ).GetUtcNow();

    private static readonly Dictionary<string, string> _SyncData = new(StringComparer.Ordinal) { ["sync"] = "1" };

    private readonly IFcmMessageSender _sender = Substitute.For<IFcmMessageSender>();

    [Fact]
    public void should_build_a_plain_notification_without_badge_or_apns_headers()
    {
        // when
        var single = FcmMessageSender.BuildMessage(new FcmMessageContent("title", "body", null), "fid-1", _Now);
        var multicast = FcmMessageSender.BuildMulticastMessage(
            new FcmMessageContent("title", "body", null),
            ["fid-1"],
            _Now
        );

        // then
        single.Notification.Title.Should().Be("title");
        single.Notification.Body.Should().Be("body");
        single.Android.Priority.Should().Be(Priority.High);
        single.Android.TimeToLive.Should().BeNull();
        single.Android.Notification.Should().BeNull();
        single.Apns.Aps.Should().BeNull();
        single.Apns.Headers.Should().BeNull();
        multicast.Notification.Title.Should().Be("title");
        multicast.Android.Priority.Should().Be(Priority.High);
        multicast.Apns.Aps.Should().BeNull();
        multicast.Apns.Headers.Should().BeNull();
    }

    [Fact]
    public void should_send_data_only_message_as_content_available_with_apns_priority_5()
    {
        // given
        var content = new FcmMessageContent(null, null, _SyncData);

        // when
        var single = FcmMessageSender.BuildMessage(content, "fid-1", _Now);
        var multicast = FcmMessageSender.BuildMulticastMessage(content, ["fid-1"], _Now);

        // then
        single.Notification.Should().BeNull();
        single.Data.Should().Contain("sync", "1");
        single.Android.Priority.Should().Be(Priority.High);
        single.Android.Notification.Should().BeNull();
        single.Apns.Aps.ContentAvailable.Should().BeTrue();
        single.Apns.Aps.Badge.Should().BeNull();
        single.Apns.Headers.Should().Contain("apns-priority", "5");
        multicast.Notification.Should().BeNull();
        multicast.Apns.Aps.ContentAvailable.Should().BeTrue();
        multicast.Apns.Aps.Badge.Should().BeNull();
        multicast.Apns.Headers.Should().Contain("apns-priority", "5");
    }

    [Fact]
    public void should_keep_apns_priority_5_for_data_only_message_even_when_high_is_requested()
    {
        // when
        var message = FcmMessageSender.BuildMessage(
            new FcmMessageContent(null, null, _SyncData, Priority: PushNotificationPriority.High),
            "fid-1",
            _Now
        );

        // then
        message.Apns.Headers.Should().Contain("apns-priority", "5");
        message.Android.Priority.Should().Be(Priority.High);
    }

    [Theory]
    [InlineData(PushNotificationPriority.Normal, Priority.Normal, "5")]
    [InlineData(PushNotificationPriority.High, Priority.High, "10")]
    public void should_map_priority_to_android_and_apns(
        PushNotificationPriority requested,
        Priority expectedAndroid,
        string expectedApns
    )
    {
        // given
        var content = new FcmMessageContent("title", "body", null, Priority: requested);

        // when
        var single = FcmMessageSender.BuildMessage(content, "fid-1", _Now);
        var multicast = FcmMessageSender.BuildMulticastMessage(content, ["fid-1"], _Now);

        // then
        single.Android.Priority.Should().Be(expectedAndroid);
        single.Apns.Headers.Should().Contain("apns-priority", expectedApns);
        multicast.Android.Priority.Should().Be(expectedAndroid);
        multicast.Apns.Headers.Should().Contain("apns-priority", expectedApns);
    }

    [Fact]
    public void should_map_time_to_live_to_android_ttl_and_apns_expiration()
    {
        // given
        var content = new FcmMessageContent("title", "body", null, TimeToLive: TimeSpan.FromHours(1));
        var expected = _Now.AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // when
        var single = FcmMessageSender.BuildMessage(content, "fid-1", _Now);
        var multicast = FcmMessageSender.BuildMulticastMessage(content, ["fid-1"], _Now);

        // then
        single.Android.TimeToLive.Should().Be(TimeSpan.FromHours(1));
        single.Apns.Headers.Should().Contain("apns-expiration", expected);
        multicast.Android.TimeToLive.Should().Be(TimeSpan.FromHours(1));
        multicast.Apns.Headers.Should().Contain("apns-expiration", expected);
    }

    [Fact]
    public void should_send_apns_expiration_zero_when_time_to_live_is_zero()
    {
        // when
        var message = FcmMessageSender.BuildMessage(
            new FcmMessageContent("title", "body", null, TimeToLive: TimeSpan.Zero),
            "fid-1",
            _Now
        );

        // then
        message.Android.TimeToLive.Should().Be(TimeSpan.Zero);
        message.Apns.Headers.Should().Contain("apns-expiration", "0");
    }

    [Fact]
    public void should_map_badge_and_sound_to_aps_and_android_notification()
    {
        // given
        var content = new FcmMessageContent("title", "body", null, Badge: 3, Sound: "default");

        // when
        var single = FcmMessageSender.BuildMessage(content, "fid-1", _Now);
        var multicast = FcmMessageSender.BuildMulticastMessage(content, ["fid-1"], _Now);

        // then
        single.Apns.Aps.Badge.Should().Be(3);
        single.Apns.Aps.Sound.Should().Be("default");
        single.Apns.Aps.ContentAvailable.Should().BeFalse();
        single.Android.Notification.NotificationCount.Should().Be(3);
        single.Android.Notification.Sound.Should().Be("default");
        multicast.Apns.Aps.Badge.Should().Be(3);
        multicast.Apns.Aps.Sound.Should().Be("default");
        multicast.Android.Notification.NotificationCount.Should().Be(3);
        multicast.Android.Notification.Sound.Should().Be("default");
    }

    [Fact]
    public void should_clear_the_ios_badge_but_leave_the_android_count_unset_when_badge_is_zero()
    {
        // when
        var message = FcmMessageSender.BuildMessage(
            new FcmMessageContent("title", "body", null, Badge: 0),
            "fid-1",
            _Now
        );

        // then
        message.Apns.Aps.Badge.Should().Be(0);
        message.Android.Notification.Should().BeNull();
    }

    [Theory]
    [InlineData("title_without_body")]
    [InlineData("body_without_title")]
    [InlineData("no_title_body_or_data")]
    [InlineData("data_only_with_badge")]
    [InlineData("data_only_with_sound")]
    [InlineData("negative_badge")]
    [InlineData("negative_time_to_live")]
    [InlineData("time_to_live_over_28_days")]
    public async Task should_throw_without_calling_the_sender_when_request_is_invalid(string scenario)
    {
        // given
        var request = _Invalid(scenario);

        // when
        var single = async () => await _CreateService().SendToDeviceAsync("fid", request, AbortToken);
        var multicast = async () => await _CreateService().SendMulticastAsync(["fid"], request, AbortToken);

        // then
        await single.Should().ThrowAsync<ArgumentException>();
        await multicast.Should().ThrowAsync<ArgumentException>();
        await _sender
            .DidNotReceive()
            .SendAsync(Arg.Any<FcmMessageContent>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sender
            .DidNotReceive()
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_pass_delivery_fields_to_sender()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "fid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("fid", "msg-1")));
        var request = _Request() with
        {
            Badge = 3,
            Sound = "default",
            Priority = PushNotificationPriority.Normal,
            TimeToLive = TimeSpan.FromHours(1),
        };

        // when
        await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        await _sender
            .Received(1)
            .SendAsync(
                Arg.Is<FcmMessageContent>(c =>
                    c.Badge == 3
                    && c.Sound == "default"
                    && c.Priority == PushNotificationPriority.Normal
                    && c.TimeToLive == TimeSpan.FromHours(1)
                ),
                "fid",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_pass_data_only_request_to_sender_without_title_or_body()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "fid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("fid", "msg-1")));
        var request = new PushNotificationRequest { Data = _SyncData };

        // when
        var result = await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        result.IsSucceeded().Should().BeTrue();
        await _sender
            .Received(1)
            .SendAsync(
                Arg.Is<FcmMessageContent>(c => c.Title == null && c.Body == null && c.Data == _SyncData),
                "fid",
                Arg.Any<CancellationToken>()
            );
    }

    private static PushNotificationRequest _Invalid(string scenario)
    {
        var dataOnly = new PushNotificationRequest { Data = _SyncData };

        return scenario switch
        {
            "title_without_body" => new PushNotificationRequest { Title = "title" },
            "body_without_title" => new PushNotificationRequest { Body = "body" },
            "no_title_body_or_data" => new PushNotificationRequest(),
            "data_only_with_badge" => dataOnly with { Badge = 1 },
            "data_only_with_sound" => dataOnly with { Sound = "default" },
            "negative_badge" => _Request() with { Badge = -1 },
            "negative_time_to_live" => _Request() with { TimeToLive = TimeSpan.FromSeconds(-1) },
            "time_to_live_over_28_days" => _Request() with
            {
                TimeToLive = TimeSpan.FromDays(28) + TimeSpan.FromTicks(1),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }

    [Fact]
    public void should_target_fid_for_single_send()
    {
        // when
        var message = FcmMessageSender.BuildMessage(new FcmMessageContent("title", "body", null), "fid-1", _Now);

        // then
        message.Fid.Should().Be("fid-1");
    }

    [Fact]
    public void should_target_fids_for_multicast_send()
    {
        // when
        var message = FcmMessageSender.BuildMulticastMessage(
            new FcmMessageContent("title", "body", null),
            ["fid-1", "fid-2"],
            _Now
        );

        // then
        message.Fids.Should().Equal("fid-1", "fid-2");
    }

    [Fact]
    public void should_set_collapse_key_for_android_and_apns_on_single_send()
    {
        // when
        var message = FcmMessageSender.BuildMessage(
            new FcmMessageContent("title", "body", null, CollapseKey: "order-42"),
            "fid-1",
            _Now
        );

        // then
        message.Android.CollapseKey.Should().Be("order-42");
        message.Apns.Headers.Should().Contain("apns-collapse-id", "order-42");
    }

    [Fact]
    public void should_set_collapse_key_for_android_and_apns_on_multicast_send()
    {
        // when
        var message = FcmMessageSender.BuildMulticastMessage(
            new FcmMessageContent("title", "body", null, CollapseKey: "order-42"),
            ["fid-1", "fid-2"],
            _Now
        );

        // then
        message.Android.CollapseKey.Should().Be("order-42");
        message.Apns.Headers.Should().Contain("apns-collapse-id", "order-42");
    }

    [Fact]
    public void should_leave_collapse_key_unset_when_request_has_none()
    {
        // when
        var single = FcmMessageSender.BuildMessage(new FcmMessageContent("title", "body", null), "fid-1", _Now);
        var multicast = FcmMessageSender.BuildMulticastMessage(
            new FcmMessageContent("title", "body", null),
            ["fid-1"],
            _Now
        );

        // then
        single.Android.CollapseKey.Should().BeNull();
        single.Apns.Headers.Should().BeNull();
        multicast.Android.CollapseKey.Should().BeNull();
        multicast.Apns.Headers.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_collapse_key_exceeds_64_utf8_bytes()
    {
        // given 33 two-byte characters = 66 UTF-8 bytes, though only 33 chars
        var request = _Request() with
        {
            CollapseKey = new string('é', 33),
        };

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
        await _sender
            .DidNotReceive()
            .SendAsync(Arg.Any<FcmMessageContent>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_collapse_key_to_sender()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "fid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("fid", "msg-1")));
        var request = _Request() with { CollapseKey = "order-42" };

        // when
        await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        await _sender
            .Received(1)
            .SendAsync(
                Arg.Is<FcmMessageContent>(c => c.CollapseKey == "order-42"),
                "fid",
                Arg.Any<CancellationToken>()
            );
    }

    private FcmPushNotificationService _CreateService()
    {
        return new(_sender);
    }

    private static PushNotificationRequest _Request(
        string title = "title",
        string body = "body",
        IReadOnlyDictionary<string, string>? data = null
    )
    {
        return new()
        {
            Title = title,
            Body = body,
            Data = data,
        };
    }

    [Fact]
    public async Task should_return_sender_outcome_for_single_send()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "fid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("fid", "msg-1")));

        // when
        var result = await _CreateService().SendToDeviceAsync("fid", _Request(), AbortToken);
        // then
        result.IsSucceeded().Should().BeTrue();
        result.MessageId.Should().Be("msg-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_throw_when_fid_is_blank(string fid)
    {
        // when
        var action = async () => await _CreateService().SendToDeviceAsync(fid, _Request(), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("from")]
    [InlineData("notification")]
    [InlineData("message_type")]
    [InlineData("google.x")]
    [InlineData("gcm.y")]
    public async Task should_throw_when_data_contains_reserved_key(string reservedKey)
    {
        // given
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { [reservedKey] = "value" };

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", _Request(data: data), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_title_exceeds_max_length()
    {
        // when
        var action = async () =>
            await _CreateService().SendToDeviceAsync("fid", _Request(title: new string('a', 101)), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_propagate_cancellation_for_single_send()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PushNotificationResponse>(new OperationCanceledException()));

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", _Request(), AbortToken);
        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_split_multicast_into_batches_of_500()
    {
        // given
        _sender
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var fids = ci.Arg<IReadOnlyList<string>>();
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. fids.Select(fid => PushNotificationResponse.Succeeded(fid, "id")),
                ];
                return Task.FromResult(outcomes);
            });
        var fids = Enumerable.Range(0, 501).Select(i => $"fid-{i}").ToList();

        // when
        var result = await _CreateService().SendMulticastAsync(fids, _Request(), AbortToken);
        // then
        result.SuccessCount.Should().Be(501);
        result.FailureCount.Should().Be(0);
        result.Responses.Should().HaveCount(501);
        await _sender
            .Received(2)
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_keep_accumulated_results_when_a_later_batch_fails()
    {
        // given
        var call = 0;
        _sender
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var fids = ci.Arg<IReadOnlyList<string>>();
                var succeed = Interlocked.Increment(ref call) == 1;
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. fids.Select(fid =>
                        succeed
                            ? PushNotificationResponse.Succeeded(fid, "id")
                            : PushNotificationResponse.Failed(fid, "boom")
                    ),
                ];
                return Task.FromResult(outcomes);
            });
        var fids = Enumerable.Range(0, 501).Select(i => $"fid-{i}").ToList();

        // when
        var result = await _CreateService().SendMulticastAsync(fids, _Request(), AbortToken);
        // then
        result.Responses.Should().HaveCount(501);
        result.SuccessCount.Should().Be(500);
        result.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_when_multicast_fids_empty()
    {
        // when
        var action = async () => await _CreateService().SendMulticastAsync([], _Request(), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }
}

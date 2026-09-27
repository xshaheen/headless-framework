// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json.Nodes;
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
    public void should_build_a_plain_notification_without_badge_or_apns_block()
    {
        // when
        var message = _Convert(_Request());

        // then
        message.Notification!.Title.Should().Be("title");
        message.Notification.Body.Should().Be("body");
        message.Android!.Priority.Should().Be(FcmAndroidPriority.High);
        message.Android.TimeToLive.Should().BeNull();
        message.Android.NotificationCount.Should().BeNull();
        message.Android.Sound.Should().BeNull();
        message.Apns.Should().BeNull();
    }

    [Fact]
    public void should_send_data_only_message_as_content_available_with_apns_priority_5()
    {
        // when
        var message = _Convert(new PushNotificationRequest { Data = _SyncData });

        // then
        message.Notification.Should().BeNull();
        message.Data.Should().Contain("sync", "1");
        message.Android!.Priority.Should().Be(FcmAndroidPriority.High);
        message.Android.NotificationCount.Should().BeNull();
        _Aps(message)["content-available"]!.GetValue<int>().Should().Be(1);
        _Aps(message).Should().NotContainKey("badge");
        message.Apns!.Headers.Should().Contain("apns-priority", "5");
    }

    [Fact]
    public void should_keep_apns_priority_5_for_data_only_message_even_when_high_is_requested()
    {
        // when
        var message = _Convert(
            new PushNotificationRequest { Data = _SyncData, Priority = PushNotificationPriority.High }
        );

        // then
        message.Apns!.Headers.Should().Contain("apns-priority", "5");
        message.Android!.Priority.Should().Be(FcmAndroidPriority.High);
    }

    [Theory]
    [InlineData(PushNotificationPriority.Normal, FcmAndroidPriority.Normal, "5")]
    [InlineData(PushNotificationPriority.High, FcmAndroidPriority.High, "10")]
    public void should_map_priority_to_android_and_apns(
        PushNotificationPriority requested,
        FcmAndroidPriority expectedAndroid,
        string expectedApns
    )
    {
        // when
        var message = _Convert(_Request() with { Priority = requested });

        // then
        message.Android!.Priority.Should().Be(expectedAndroid);
        message.Apns!.Headers.Should().Contain("apns-priority", expectedApns);
    }

    [Fact]
    public void should_map_time_to_live_to_android_ttl_and_apns_expiration()
    {
        // given
        var expected = _Now.AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // when
        var message = _Convert(_Request() with { TimeToLive = TimeSpan.FromHours(1) });

        // then
        message.Android!.TimeToLive.Should().Be(TimeSpan.FromHours(1));
        message.Apns!.Headers.Should().Contain("apns-expiration", expected);
    }

    [Fact]
    public void should_send_apns_expiration_zero_when_time_to_live_is_zero()
    {
        // when
        var message = _Convert(_Request() with { TimeToLive = TimeSpan.Zero });

        // then
        message.Android!.TimeToLive.Should().Be(TimeSpan.Zero);
        message.Apns!.Headers.Should().Contain("apns-expiration", "0");
    }

    [Fact]
    public void should_map_badge_and_sound_to_aps_and_android_notification()
    {
        // when
        var message = _Convert(_Request() with { Badge = 3, Sound = "default" });

        // then
        _Aps(message)["badge"]!.GetValue<int>().Should().Be(3);
        _Aps(message)["sound"]!.GetValue<string>().Should().Be("default");
        _Aps(message).Should().NotContainKey("content-available");
        message.Android!.NotificationCount.Should().Be(3);
        message.Android.Sound.Should().Be("default");
    }

    [Fact]
    public void should_clear_the_ios_badge_but_leave_the_android_count_unset_when_badge_is_zero()
    {
        // when
        var message = _Convert(_Request() with { Badge = 0 });

        // then
        _Aps(message)["badge"]!.GetValue<int>().Should().Be(0);
        message.Android!.NotificationCount.Should().BeNull();
    }

    [Fact]
    public void should_target_the_fid_field()
    {
        // when
        var message = FcmMessageMapper.ToMessage(_Convert(_Request()), FcmTarget.Token("fid-1"));
        var messages = FcmMessageMapper.ToMessages(_Convert(_Request()), ["fid-1", "fid-2"]);

        // then
        message.Fid.Should().Be("fid-1");
        messages.Select(m => m.Fid).Should().Equal("fid-1", "fid-2");
    }

    [Fact]
    public void should_set_collapse_key_for_android_and_apns()
    {
        // when
        var message = _Convert(_Request() with { CollapseKey = "order-42" });

        // then
        message.Android!.CollapseKey.Should().Be("order-42");
        message.Apns!.Headers.Should().Contain("apns-collapse-id", "order-42");
    }

    [Fact]
    public void should_leave_collapse_key_unset_when_request_has_none()
    {
        // when
        var message = _Convert(_Request());

        // then
        message.Android!.CollapseKey.Should().BeNull();
        message.Apns.Should().BeNull();
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
            .SendAsync(Arg.Any<FcmMessage>(), Arg.Any<FcmTarget>(), Arg.Any<CancellationToken>());
        await _sender
            .DidNotReceive()
            .SendBatchAsync(Arg.Any<FcmMessage>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_delivery_fields_to_sender()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessage>(), FcmTarget.Token("fid"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_Succeeded("fid", "msg-1")));
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
                Arg.Is<FcmMessage>(m =>
                    m.Android!.NotificationCount == 3
                    && m.Android.Sound == "default"
                    && m.Android.Priority == FcmAndroidPriority.Normal
                    && m.Android.TimeToLive == TimeSpan.FromHours(1)
                ),
                FcmTarget.Token("fid"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_pass_data_only_request_to_sender_without_title_or_body()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessage>(), FcmTarget.Token("fid"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_Succeeded("fid", "msg-1")));
        var request = new PushNotificationRequest { Data = _SyncData };

        // when
        var result = await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        result.IsSucceeded().Should().BeTrue();
        await _sender
            .Received(1)
            .SendAsync(
                Arg.Is<FcmMessage>(m => m.Notification == null && m.Data == _SyncData),
                FcmTarget.Token("fid"),
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
            .SendAsync(Arg.Any<FcmMessage>(), Arg.Any<FcmTarget>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_collapse_key_to_sender()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessage>(), FcmTarget.Token("fid"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_Succeeded("fid", "msg-1")));
        var request = _Request() with { CollapseKey = "order-42" };

        // when
        await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        await _sender
            .Received(1)
            .SendAsync(
                Arg.Is<FcmMessage>(m => m.Android!.CollapseKey == "order-42"),
                FcmTarget.Token("fid"),
                Arg.Any<CancellationToken>()
            );
    }

    private FcmPushNotificationService _CreateService()
    {
        return new(_sender, TimeProvider.System);
    }

    private static FcmMessage _Convert(PushNotificationRequest request)
    {
        return FcmPushNotificationService.ToFcmMessage(request, _Now);
    }

    private static JsonObject _Aps(FcmMessage message)
    {
        return message.Apns!.Payload!["aps"]!.AsObject();
    }

    private static FcmSendResult _Succeeded(string fid, string messageId)
    {
        return new FcmSendResult { Response = PushNotificationResponse.Succeeded(fid, messageId) };
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
            .SendAsync(Arg.Any<FcmMessage>(), FcmTarget.Token("fid"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_Succeeded("fid", "msg-1")));

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
    [InlineData("google.c.a.e")]
    [InlineData("gcm.y")]
    [InlineData("gcm.notification.title")]
    public async Task should_throw_when_data_contains_reserved_key(string reservedKey)
    {
        // given
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { [reservedKey] = "value" };

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", _Request(data: data), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("googleAnalyticsId")]
    [InlineData("gcmSender")]
    [InlineData("fromCity")]
    public async Task should_accept_data_keys_that_only_resemble_reserved_keys(string key)
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessage>(), FcmTarget.Token("fid"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_Succeeded("fid", "msg-1")));
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "value" };

        // when
        var result = await _CreateService().SendToDeviceAsync("fid", _Request(data: data), AbortToken);

        // then
        result.IsSucceeded().Should().BeTrue();
    }

    [Fact]
    public async Task should_not_limit_title_or_body_length()
    {
        // given FCM limits only the whole payload, and reports an oversized one itself
        _sender
            .SendAsync(Arg.Any<FcmMessage>(), FcmTarget.Token("fid"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_Succeeded("fid", "msg-1")));
        var request = _Request(title: new string('a', 101), body: new string('b', 4001));

        // when
        var result = await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        result.IsSucceeded().Should().BeTrue();
    }

    [Fact]
    public async Task should_propagate_cancellation_for_single_send()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessage>(), Arg.Any<FcmTarget>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FcmSendResult>(new OperationCanceledException()));

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
            .SendBatchAsync(Arg.Any<FcmMessage>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var fids = ci.Arg<IReadOnlyList<string>>();
                IReadOnlyList<FcmSendResult> outcomes = [.. fids.Select(fid => _Succeeded(fid, "id"))];
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
            .SendBatchAsync(Arg.Any<FcmMessage>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_accumulated_results_when_a_later_batch_fails()
    {
        // given
        var call = 0;
        _sender
            .SendBatchAsync(Arg.Any<FcmMessage>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var fids = ci.Arg<IReadOnlyList<string>>();
                var succeed = Interlocked.Increment(ref call) == 1;
                IReadOnlyList<FcmSendResult> outcomes =
                [
                    .. fids.Select(fid =>
                        succeed
                            ? _Succeeded(fid, "id")
                            : new FcmSendResult { Response = PushNotificationResponse.Failed(fid, "boom") }
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

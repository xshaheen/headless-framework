// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.Testing.Tests;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Exercises <see cref="IFcmPushNotificationService"/> through the real FirebaseAdmin SDK over a fake transport:
/// input validation before any request, the FCM v1 JSON each message becomes, and topic and condition sends.
/// </summary>
public sealed class FcmTypedApiTests : TestBase
{
    private static readonly FcmMessage _Message = new()
    {
        Notification = new FcmNotification { Title = "title", Body = "body" },
    };

    private readonly FcmTestRig _rig = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _rig.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_send_every_message_field_as_fcm_v1_json()
    {
        // given
        var message = new FcmMessage
        {
            Notification = new FcmNotification
            {
                Title = "Order shipped",
                Body = "On its way",
                Image = new Uri("https://cdn.example.com/n.png"),
            },
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["orderId"] = "42" },
            Android = new FcmAndroidOptions
            {
                Priority = FcmAndroidPriority.High,
                TimeToLive = TimeSpan.FromHours(1),
                CollapseKey = "orders",
                DirectBootOk = true,
                ChannelId = "shipping",
                Tag = "order-42",
                Color = "#FF0000",
                Icon = "ic_ship",
                ClickAction = "OPEN_ORDER",
                Sound = "chime",
                NotificationCount = 2,
                Visibility = FcmAndroidVisibility.Public,
                Image = new Uri("https://cdn.example.com/a.png"),
            },
            Webpush = new FcmWebpushOptions
            {
                Link = new Uri("https://example.com/orders/42"),
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Urgency"] = "high" },
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["web"] = "1" },
            },
            Apns = new FcmApnsOptions
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["apns-priority"] = "10" },
                Payload = new JsonObject
                {
                    ["aps"] = new JsonObject
                    {
                        ["badge"] = 3,
                        ["mutable-content"] = 1,
                        ["interruption-level"] = "time-sensitive",
                    },
                    ["orderRef"] = new JsonObject
                    {
                        ["id"] = 42,
                        ["tags"] = new JsonArray("a", "b"),
                        ["ratio"] = 0.5,
                    },
                },
            },
            AnalyticsLabel = "order_shipped",
            DryRun = true,
        };

        // when
        var result = await _rig.CreateService().SendAsync("fid-1", message, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        var body = _rig.Http.Requests.Should().ContainSingle().Subject.Body;
        body["validate_only"]!.GetValue<bool>().Should().BeTrue();

        var sent = body["message"]!.AsObject();
        sent["fid"]!.GetValue<string>().Should().Be("fid-1");
        sent["notification"]!
            .ToJsonString()
            .Should()
            .Be("""{"title":"Order shipped","body":"On its way","image":"https://cdn.example.com/n.png"}""");
        sent["data"]!.ToJsonString().Should().Be("""{"orderId":"42"}""");
        sent["fcm_options"]!.ToJsonString().Should().Be("""{"analytics_label":"order_shipped"}""");

        var android = sent["android"]!.AsObject();
        android["priority"]!.GetValue<string>().Should().Be("high");
        android["ttl"]!.GetValue<string>().Should().Be("3600s");
        android["collapse_key"]!.GetValue<string>().Should().Be("orders");
        android["direct_boot_ok"]!.GetValue<bool>().Should().BeTrue();
        var androidNotification = android["notification"]!.AsObject();
        androidNotification["channel_id"]!.GetValue<string>().Should().Be("shipping");
        androidNotification["tag"]!.GetValue<string>().Should().Be("order-42");
        androidNotification["color"]!.GetValue<string>().Should().Be("#FF0000");
        androidNotification["icon"]!.GetValue<string>().Should().Be("ic_ship");
        androidNotification["click_action"]!.GetValue<string>().Should().Be("OPEN_ORDER");
        androidNotification["sound"]!.GetValue<string>().Should().Be("chime");
        androidNotification["notification_count"]!.GetValue<int>().Should().Be(2);
        androidNotification["visibility"]!.GetValue<string>().Should().Be("PUBLIC");
        androidNotification["image"]!.GetValue<string>().Should().Be("https://cdn.example.com/a.png");

        var webpush = sent["webpush"]!.AsObject();
        webpush["headers"]!.ToJsonString().Should().Be("""{"Urgency":"high"}""");
        webpush["data"]!.ToJsonString().Should().Be("""{"web":"1"}""");
        webpush["fcm_options"]!["link"]!.GetValue<string>().Should().Be("https://example.com/orders/42");

        var apns = sent["apns"]!.AsObject();
        apns["headers"]!.ToJsonString().Should().Be("""{"apns-priority":"10"}""");
        JsonNode
            .DeepEquals(
                apns["payload"],
                JsonNode.Parse(
                    """
                    {
                      "aps": { "badge": 3, "mutable-content": 1, "interruption-level": "time-sensitive" },
                      "orderRef": { "id": 42, "tags": ["a", "b"], "ratio": 0.5 }
                    }
                    """
                )
            )
            .Should()
            .BeTrue(apns["payload"]!.ToJsonString());
    }

    [Fact]
    public async Task should_send_a_data_message_without_notification_or_platform_blocks()
    {
        // given
        var message = new FcmMessage
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
        };

        // when
        await _rig.CreateService().SendAsync("fid-1", message, AbortToken);

        // then
        var body = _rig.Http.Requests.Should().ContainSingle().Subject.Body;
        body["validate_only"]!.GetValue<bool>().Should().BeFalse();
        body["message"]!.ToJsonString().Should().Be("""{"fid":"fid-1","data":{"sync":"1"}}""");
    }

    [Fact]
    public async Task should_carry_the_analytics_label_and_dry_run_on_every_multicast_message()
    {
        // given
        var message = _Message with
        {
            AnalyticsLabel = "campaign_1",
            DryRun = true,
        };

        // when
        var result = await _rig.CreateService().SendMulticastAsync(["fid-1", "fid-2"], message, AbortToken);

        // then
        result.SuccessCount.Should().Be(2);
        result.Results.Select(r => r.Response.ClientIdentifier).Should().Equal("fid-1", "fid-2");
        _rig.Http.Requests.Should().HaveCount(2);
        _rig.Http.Requests.Should()
            .OnlyContain(r =>
                (string?)r.Body["message"]!["fcm_options"]!["analytics_label"] == "campaign_1"
                && (bool)r.Body["validate_only"]!
            );
    }

    [Fact]
    public async Task should_split_a_typed_multicast_into_batches_of_500_in_input_order()
    {
        // given
        var fids = Enumerable.Range(0, 501).Select(i => $"fid-{i}").ToList();

        // when
        var result = await _rig.CreateService().SendMulticastAsync(fids, _Message, AbortToken);

        // then
        result.SuccessCount.Should().Be(501);
        result.FailureCount.Should().Be(0);
        result.Results.Select(r => r.Response.ClientIdentifier).Should().Equal(fids);
        _rig.Http.Requests.Should().HaveCount(501);
    }

    [Fact]
    public async Task should_send_to_a_topic_by_its_bare_name()
    {
        // when
        var result = await _rig.CreateService().SendToTopicAsync("news.sports-1_x~%20", _Message, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        result.Response.ClientIdentifier.Should().Be("news.sports-1_x~%20");
        var sent = _rig.Http.Requests.Should().ContainSingle().Subject.Body["message"]!.AsObject();
        sent["topic"]!.GetValue<string>().Should().Be("news.sports-1_x~%20");
        sent.Should().NotContainKey("fid");
        sent.Should().NotContainKey("token");
    }

    [Fact]
    public async Task should_send_to_a_condition()
    {
        // given
        const string condition = "'news' in topics && ('sports' in topics || \"tech\" in topics)";

        // when
        var result = await _rig.CreateService().SendToConditionAsync(condition, _Message, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue(result.Response.FailureError);
        _rig.Http.Requests.Should().ContainSingle().Subject.Body["message"]!["condition"]!
            .GetValue<string>()
            .Should()
            .Be(condition);
    }

    [Fact]
    public async Task should_accept_a_condition_naming_five_topics()
    {
        // given
        const string condition = "'a' in topics || 'b' in topics || 'c' in topics || 'd' in topics || 'e' in topics";

        // when
        var result = await _rig.CreateService().SendToConditionAsync(condition, _Message, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/topics/news")]
    [InlineData("news!")]
    [InlineData("news sports")]
    public async Task should_reject_an_invalid_topic_before_sending(string topic)
    {
        // when
        var action = async () => await _rig.CreateService().SendToTopicAsync(topic, _Message, AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
        _rig.Http.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("news && sports")]
    [InlineData("'a' in topics || 'b' in topics || 'c' in topics || 'd' in topics || 'e' in topics || 'f' in topics")]
    [InlineData("'bad topic!' in topics")]
    public async Task should_reject_an_invalid_condition_before_sending(string condition)
    {
        // when
        var action = async () => await _rig.CreateService().SendToConditionAsync(condition, _Message, AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
        _rig.Http.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("reserved_data_key")]
    [InlineData("reserved_data_namespace")]
    [InlineData("analytics_label_too_long")]
    [InlineData("analytics_label_bad_character")]
    [InlineData("android_color")]
    [InlineData("android_ttl_over_28_days")]
    [InlineData("android_negative_ttl")]
    [InlineData("android_negative_count")]
    [InlineData("android_undefined_priority")]
    [InlineData("android_undefined_visibility")]
    [InlineData("android_relative_image")]
    [InlineData("notification_relative_image")]
    [InlineData("webpush_http_link")]
    [InlineData("apns_payload_without_aps")]
    public async Task should_reject_an_invalid_message_before_sending(string scenario)
    {
        // given
        var message = _Invalid(scenario);
        var service = _rig.CreateService();

        // when
        var single = async () => await service.SendAsync("fid-1", message, AbortToken);
        var multicast = async () => await service.SendMulticastAsync(["fid-1"], message, AbortToken);
        var topic = async () => await service.SendToTopicAsync("news", message, AbortToken);
        var condition = async () => await service.SendToConditionAsync("'news' in topics", message, AbortToken);

        // then
        await single.Should().ThrowAsync<ArgumentException>();
        await multicast.Should().ThrowAsync<ArgumentException>();
        await topic.Should().ThrowAsync<ArgumentException>();
        await condition.Should().ThrowAsync<ArgumentException>();
        _rig.Http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_accept_an_analytics_label_at_the_50_character_limit()
    {
        // when
        var result = await _rig.CreateService()
            .SendAsync("fid-1", _Message with { AnalyticsLabel = new string('a', 50) }, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task should_reject_a_blank_fid(string fid)
    {
        // when
        var single = async () => await _rig.CreateService().SendAsync(fid, _Message, AbortToken);
        var multicast = async () => await _rig.CreateService().SendMulticastAsync(["fid-1", fid], _Message, AbortToken);

        // then
        await single.Should().ThrowAsync<ArgumentException>();
        await multicast.Should().ThrowAsync<ArgumentException>();
        _rig.Http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_an_empty_multicast()
    {
        // when
        var action = async () => await _rig.CreateService().SendMulticastAsync([], _Message, AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_retry_a_topic_send_with_the_same_loop_as_a_device_send()
    {
        // given
        _rig.Http.Responder = static (request, _) =>
            Task.FromResult(
                request.Attempt == 1 ? FakeFcmHttpHandler.Error("INTERNAL") : FakeFcmHttpHandler.Success(request.Target)
            );
        var service = _rig.CreateService();

        // when
        var send = service.SendToTopicAsync("news", _Message, AbortToken).AsTask();
        _rig.Time.Advance(await _rig.Time.WaitForTimerAsync(1, AbortToken));
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        _rig.Http.RequestsFor("news").Should().HaveCount(2);
    }

    [Fact]
    public async Task should_return_a_failure_instead_of_throwing_when_a_condition_send_times_out()
    {
        // given
        _rig.Http.Responder = static (_, _) =>
            throw new TaskCanceledException("The request timed out.", new TimeoutException());

        // when
        var result = await _rig.CreateService().SendToConditionAsync("'news' in topics", _Message, AbortToken);

        // then
        result.Response.IsFailed().Should().BeTrue();
        result.Response.ClientIdentifier.Should().Be("'news' in topics");
        result.FailureKind.Should().Be(FcmFailureKind.Transport);
    }

    [Fact]
    public async Task should_throw_when_the_caller_cancels_a_topic_send()
    {
        // given
        _rig.Http.Responder = static async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("unreachable");
        };
        var service = _rig.CreateService();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var send = service.SendToTopicAsync("news", _Message, cts.Token).AsTask();
        await _rig.Http.WaitForRequestsAsync(1, AbortToken);
        await cts.CancelAsync();
        var action = async () => await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_send_a_shared_request_through_the_same_mapping()
    {
        // given
        var request = new PushNotificationRequest
        {
            Title = "title",
            Body = "body",
            Badge = 2,
            CollapseKey = "k",
        };

        // when
        IPushNotificationService service = _rig.CreateService();
        await service.SendToDeviceAsync("fid-1", request, AbortToken);

        // then
        var sent = _rig.Http.Requests.Should().ContainSingle().Subject.Body["message"]!.AsObject();
        sent["android"]!["notification"]!["notification_count"]!.GetValue<int>().Should().Be(2);
        sent["apns"]!["headers"]!["apns-collapse-id"]!.GetValue<string>().Should().Be("k");
        sent["apns"]!["payload"]!.ToJsonString().Should().Be("""{"aps":{"badge":2}}""");
    }

    private static FcmMessage _Invalid(string scenario)
    {
        return scenario switch
        {
            "reserved_data_key" => _Message with
            {
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "x" },
            },
            "reserved_data_namespace" => _Message with
            {
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["google.c.a"] = "x" },
            },
            "analytics_label_too_long" => _Message with { AnalyticsLabel = new string('a', 51) },
            "analytics_label_bad_character" => _Message with { AnalyticsLabel = "campaign 1" },
            "android_color" => _Message with { Android = new FcmAndroidOptions { Color = "red" } },
            "android_ttl_over_28_days" => _Message with
            {
                Android = new FcmAndroidOptions { TimeToLive = TimeSpan.FromDays(28) + TimeSpan.FromSeconds(1) },
            },
            "android_negative_ttl" => _Message with
            {
                Android = new FcmAndroidOptions { TimeToLive = TimeSpan.FromSeconds(-1) },
            },
            "android_negative_count" => _Message with { Android = new FcmAndroidOptions { NotificationCount = -1 } },
            "android_undefined_priority" => _Message with
            {
                Android = new FcmAndroidOptions { Priority = (FcmAndroidPriority)9 },
            },
            "android_undefined_visibility" => _Message with
            {
                Android = new FcmAndroidOptions { Visibility = (FcmAndroidVisibility)9 },
            },
            "android_relative_image" => _Message with
            {
                Android = new FcmAndroidOptions { Image = new Uri("/a.png", UriKind.Relative) },
            },
            "notification_relative_image" => _Message with
            {
                Notification = new FcmNotification { Title = "t", Image = new Uri("/n.png", UriKind.Relative) },
            },
            "webpush_http_link" => _Message with
            {
                Webpush = new FcmWebpushOptions { Link = new Uri("http://example.com") },
            },
            "apns_payload_without_aps" => _Message with
            {
                Apns = new FcmApnsOptions { Payload = new JsonObject { ["custom"] = 1 } },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }
}

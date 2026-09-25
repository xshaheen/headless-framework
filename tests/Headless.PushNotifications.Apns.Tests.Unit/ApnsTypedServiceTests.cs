// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ApnsTypedServiceTests : TestBase
{
    private const string _DeviceToken = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    // 2026-09-25T10:00:00Z, which is 1790330400 in Unix epoch seconds.
    private static readonly DateTimeOffset _Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private FakeApnsServer _server = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _server = await FakeApnsServer.StartAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _server.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    #region Results

    [Fact]
    public async Task should_report_unregistered_with_the_invalid_since_instant_when_apns_answers_410()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(410, "Unregistered");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, _Alert(), AbortToken);

        // then
        result.Response.IsUnregistered().Should().BeTrue();
        result.Response.ClientIdentifier.Should().Be(_DeviceToken);
        result.StatusCode.Should().Be(HttpStatusCode.Gone);
        result.Reason.Should().Be("Unregistered");
        result.InvalidSince.Should().Be(new DateTimeOffset(2025, 9, 16, 5, 20, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task should_report_no_invalid_since_when_the_410_body_has_no_timestamp()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(410, RawBody: """{"reason":"Unregistered"}""");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, _Alert(), AbortToken);

        // then
        result.Response.IsUnregistered().Should().BeTrue();
        result.Reason.Should().Be("Unregistered");
        result.InvalidSince.Should().BeNull();
    }

    [Fact]
    public async Task should_report_the_status_apns_id_and_unique_id_when_the_sandbox_accepts_the_push()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(200, UniqueId: "5b2f1c4e-8d3a-4f6b-9c7e-2a1d0e9f8b7c");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, _Alert(), AbortToken);

        // then
        var sentApnsId = _server.Requests.Should().ContainSingle().Subject.Headers["apns-id"];
        result.Response.IsSucceeded().Should().BeTrue();
        result.Response.MessageId.Should().Be(sentApnsId);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.ApnsId.Should().Be(sentApnsId);
        result.UniqueId.Should().Be("5b2f1c4e-8d3a-4f6b-9c7e-2a1d0e9f8b7c");
        result.Reason.Should().BeNull();
        result.InvalidSince.Should().BeNull();
    }

    [Fact]
    public async Task should_report_no_unique_id_when_apns_sends_none()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, _Alert(), AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        result.UniqueId.Should().BeNull();
    }

    [Fact]
    public async Task should_report_a_failure_without_a_status_when_the_endpoint_is_unreachable()
    {
        // given
        var closedPort = _GetClosedLoopbackPort();
        await using var provider = _server.CreateProvider(configureClient: c =>
            c.BaseAddress = new Uri($"http://127.0.0.1:{closedPort}")
        );
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, _Alert(), AbortToken);

        // then
        result.Response.IsFailed().Should().BeTrue();
        result.Response.FailureError.Should().Contain(nameof(HttpRequestException));
        result.StatusCode.Should().BeNull();
        result.Reason.Should().BeNull();
        result.ApnsId.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_input_order_and_counts_without_throwing_when_one_multicast_token_is_gone()
    {
        // given
        _server.Responder = r => r.DeviceToken == "t2" ? new FakeApnsReply(410, "Unregistered") : FakeApnsReply.Ok;
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendMulticastAsync(["t1", "t2", "t3"], _Alert(), AbortToken);

        // then
        result.SuccessCount.Should().Be(2);
        result.FailureCount.Should().Be(1);
        result.Results.Select(r => r.Response.ClientIdentifier).Should().Equal("t1", "t2", "t3");
        result.Results[0].Response.IsSucceeded().Should().BeTrue();
        result.Results[1].Response.IsUnregistered().Should().BeTrue();
        result.Results[1].InvalidSince.Should().NotBeNull();
        result.Results[2].Response.IsSucceeded().Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_before_sending_when_a_multicast_token_is_blank()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var act = async () => await service.SendMulticastAsync(["t1", " "], _Alert(), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    #endregion

    #region Push types

    [Fact]
    public async Task should_send_a_live_activity_update_to_the_live_activity_topic()
    {
        // given
        var clock = new FakeTimeProvider(_Now);
        await using var provider = _server.CreateProvider(configureServices: s => s.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":2}"""),
            StaleDate = _Now.AddMinutes(30),
        };

        // when
        var result = await service.SendAsync(_DeviceToken, notification, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Headers["apns-topic"].Should().Be($"{FakeApnsServer.BundleId}.push-type.liveactivity");
        sent.Headers["apns-push-type"].Should().Be("liveactivity");
        sent.Headers["apns-priority"].Should().Be("5");
        sent.Body.Should()
            .Be(
                """{"aps":{"timestamp":1790330400,"event":"update","content-state":{"score":2},"stale-date":1790332200}}"""
            );
    }

    [Fact]
    public async Task should_send_a_background_notification_as_a_background_push_at_priority_5()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.Priority = ApnsPriority.Immediate);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsBackgroundNotification
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
        };

        // when
        var result = await service.SendAsync(_DeviceToken, notification, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Headers["apns-push-type"].Should().Be("background");
        sent.Headers["apns-topic"].Should().Be(FakeApnsServer.BundleId);
        sent.Headers["apns-priority"].Should().Be("5");
        sent.Body.Should().Be("""{"aps":{"content-available":1},"sync":"1"}""");
    }

    [Fact]
    public async Task should_send_a_typed_alert_as_voip_when_the_instance_is_voip()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.PushType = ApnsPushType.Voip);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, _Alert(), AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Headers["apns-push-type"].Should().Be("voip");
        sent.Headers["apns-topic"].Should().Be($"{FakeApnsServer.BundleId}.voip");
    }

    public static TheoryData<string, ApnsNotification> NonVoipNotifications =>
        new()
        {
            {
                "background",
                new ApnsBackgroundNotification
                {
                    Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
                }
            },
            {
                "live activity",
                new ApnsLiveActivityNotification
                {
                    Event = ApnsLiveActivityEvent.Update,
                    ContentState = _Element("""{"score":1}"""),
                }
            },
        };

    [Theory]
    [MemberData(nameof(NonVoipNotifications))]
    public async Task should_throw_before_sending_when_a_voip_instance_gets_a_non_voip_push_type(
        string scenario,
        ApnsNotification notification
    )
    {
        // given
        await using var provider = _server.CreateProvider(o => o.PushType = ApnsPushType.Voip);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var single = async () => await service.SendAsync(_DeviceToken, notification, AbortToken);
        var multicast = async () => await service.SendMulticastAsync(["t1", "t2"], notification, AbortToken);

        // then
        await single.Should().ThrowAsync<ArgumentException>(scenario);
        await multicast.Should().ThrowAsync<ArgumentException>(scenario);
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_before_sending_when_the_notification_is_invalid()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsLiveActivityNotification { Event = ApnsLiveActivityEvent.Update };

        // when
        var act = async () => await service.SendAsync(_DeviceToken, notification, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static ApnsAlertNotification _Alert()
    {
        return new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Title = "Hi", Body = "There" },
        };
    }

    private static JsonElement _Element(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static int _GetClosedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    #endregion
}

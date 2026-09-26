// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ApnsBroadcastTests : TestBase
{
    private const string _ChannelId = "dHN0LXNyY2gtY2hubA==";

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

    #region Broadcast send

    [Fact]
    public async Task should_post_the_update_to_the_broadcast_path_with_apples_required_headers()
    {
        // given
        var clock = new FakeTimeProvider(_Now);
        _server.Responder = _ => new FakeApnsReply(200, UniqueId: "5b2f1c4e-8d3a-4f6b-9c7e-2a1d0e9f8b7c");
        await using var provider = _server.CreateProvider(configureServices: s => s.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, _Update(), AbortToken);

        // then
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be("POST");
        sent.Path.Should().Be($"/4/broadcasts/apps/{FakeApnsServer.BundleId}");
        sent.Headers["apns-channel-id"].Should().Be(_ChannelId);
        sent.Headers["apns-push-type"].Should().Be("liveactivity");
        sent.Headers["apns-priority"].Should().Be("5");
        // Apple marks apns-expiration required on a broadcast, and an unset one means deliver once.
        sent.Headers["apns-expiration"].Should().Be("0");
        sent.Headers["apns-request-id"]
            .Should()
            .MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        // The bundle id is in the path; Apple's broadcast request has no apns-topic.
        sent.Headers.Should().NotContainKey("apns-topic");
        sent.Bearer.Should().NotBeNullOrEmpty();
        JsonNode
            .DeepEquals(
                JsonNode.Parse(sent.Body),
                JsonNode.Parse("""{"aps":{"timestamp":1790330400,"event":"update","content-state":{"score":2}}}""")
            )
            .Should()
            .BeTrue(sent.Body);

        result.IsSucceeded.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.RequestId.Should().Be(sent.Headers["apns-request-id"]);
        result.UniqueId.Should().Be("5b2f1c4e-8d3a-4f6b-9c7e-2a1d0e9f8b7c");
        result.FailureKind.Should().BeNull();
    }

    [Fact]
    public async Task should_send_the_caller_expiration_priority_and_request_id()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var requestId = Guid.Parse("4d3c2b1a-0f9e-4d8c-b7a6-5f4e3d2c1b0a");
        var notification = _Update() with
        {
            Expiration = ApnsExpiration.At(_Now.AddHours(1)),
            Priority = ApnsPriority.Immediate,
            ApnsId = requestId,
        };

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, notification, AbortToken);

        // then
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Headers["apns-expiration"].Should().Be("1790334000");
        sent.Headers["apns-priority"].Should().Be("10");
        sent.Headers["apns-request-id"].Should().Be("4d3c2b1a-0f9e-4d8c-b7a6-5f4e3d2c1b0a");
        result.RequestId.Should().Be("4d3c2b1a-0f9e-4d8c-b7a6-5f4e3d2c1b0a");
    }

    [Fact]
    public async Task should_allow_priority_1_which_apples_broadcast_table_lists()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        await service.SendBroadcastAsync(
            _ChannelId,
            _Update() with
            {
                Priority = ApnsPriority.PowerPrioritized,
            },
            AbortToken
        );

        // then
        _server.Requests.Should().ContainSingle().Which.Headers["apns-priority"].Should().Be("1");
    }

    [Fact]
    public async Task should_send_an_end_event()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.End,
            DismissalDate = _Now.AddMinutes(10),
        };

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, notification, AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        JsonNode.Parse(_server.Requests.Single().Body)!["aps"]!["event"]!.GetValue<string>().Should().Be("end");
    }

    [Fact]
    public async Task should_refuse_a_start_event_before_any_request()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Attributes = _Element("""{"home":"A"}"""),
            Alert = new ApnsAlert { Title = "Kickoff", Body = "The match started" },
        };

        // when
        var act = async () => await service.SendBroadcastAsync(_ChannelId, notification, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*cannot start a Live Activity*");
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_start_only_fields_on_an_update()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var withAttributes = async () =>
            await service.SendBroadcastAsync(
                _ChannelId,
                _Update() with
                {
                    AttributesType = "MatchAttributes",
                    Attributes = _Element("""{"home":"A"}"""),
                },
                AbortToken
            );
        var withChannel = async () =>
            await service.SendBroadcastAsync(_ChannelId, _Update() with { InputPushChannel = _ChannelId }, AbortToken);

        // then
        await withAttributes.Should().ThrowAsync<ArgumentException>();
        await withChannel.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_collapse_id_and_a_blank_channel()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var withCollapse = async () =>
            await service.SendBroadcastAsync(_ChannelId, _Update() with { CollapseId = "score" }, AbortToken);
        var blankChannel = async () => await service.SendBroadcastAsync(" ", _Update(), AbortToken);

        // then
        await withCollapse.Should().ThrowAsync<ArgumentException>().WithMessage("*collapse id*");
        await blankChannel.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_accept_5120_bytes_and_refuse_one_more()
    {
        // given
        var overhead = System.Text.Encoding.UTF8.GetByteCount(
            """{"aps":{"timestamp":0,"event":"update","content-state":{"f":""}}}"""
        );
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1));
        await using var clockProvider = _server.CreateProvider(configureServices: s =>
            s.AddSingleton<TimeProvider>(clock)
        );
        var clockService = clockProvider.GetRequiredService<IApnsPushNotificationService>();

        // "timestamp":1 is one byte like the "0" above.
        var atLimit = _Update() with
        {
            ContentState = _Filler(5120 - overhead),
        };
        var overLimit = _Update() with { ContentState = _Filler(5120 - overhead + 1) };

        // when
        var accepted = await clockService.SendBroadcastAsync(_ChannelId, atLimit, AbortToken);
        var refused = async () => await clockService.SendBroadcastAsync(_ChannelId, overLimit, AbortToken);

        // then
        accepted.IsSucceeded.Should().BeTrue();
        await refused.Should().ThrowAsync<ArgumentException>().WithMessage("*5120-byte limit*");
        _server.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task should_classify_a_rejected_broadcast_without_throwing()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(400, "BadChannelId");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, _Update(), AbortToken);

        // then
        result.IsSucceeded.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Reason.Should().Be("BadChannelId");
        result.FailureError.Should().Be("BadChannelId (HTTP 400)");
        result.FailureKind.Should().Be(ApnsFailureKind.Payload);
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task should_mark_a_server_error_retryable_after_15_minutes()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(503, "ServiceUnavailable");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, _Update(), AbortToken);

        // then
        result.FailureKind.Should().Be(ApnsFailureKind.ServerError);
        result.IsRetryable.Should().BeTrue();
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(15));
        // A 5xx is not retried in-process: Apple asks senders to wait before retrying.
        _server.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task should_report_a_transport_failure_as_a_result()
    {
        // given
        var closedPort = _GetClosedLoopbackPort();
        await using var provider = _server.CreateProvider(configureClient: c =>
            c.BaseAddress = new Uri($"http://127.0.0.1:{closedPort}")
        );
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, _Update(), AbortToken);

        // then
        result.IsSucceeded.Should().BeFalse();
        result.StatusCode.Should().BeNull();
        result.FailureKind.Should().Be(ApnsFailureKind.Transport);
        result.FailureError.Should().StartWith(nameof(HttpRequestException) + ":");
        result.RequestId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_renew_an_expired_provider_token_once()
    {
        // given
        var clock = new FakeTimeProvider(_Now);
        await using var provider = _server.CreateProvider(configureServices: s => s.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // A first broadcast mints the token; 25 minutes later it is past the 20-minute re-mint limit.
        await service.SendBroadcastAsync(_ChannelId, _Update(), AbortToken);
        clock.Advance(TimeSpan.FromMinutes(25));
        var firstBearer = _server.Requests.Single().Bearer;
        _server.Responder = request =>
            request.Bearer == firstBearer ? new FakeApnsReply(403, "ExpiredProviderToken") : FakeApnsReply.Ok;

        // when
        var result = await service.SendBroadcastAsync(_ChannelId, _Update(), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        var requests = _server.Requests.ToArray();
        requests.Should().HaveCount(3);
        requests[2].Bearer.Should().NotBe(requests[1].Bearer);
        requests[2].Headers["apns-request-id"].Should().Be(requests[1].Headers["apns-request-id"]);
    }

    #endregion

    #region Channel management

    [Fact]
    public async Task should_create_a_channel_with_apples_body_and_return_its_id()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(
            201,
            Headers: new Dictionary<string, string>(StringComparer.Ordinal) { ["apns-channel-id"] = _ChannelId }
        );
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var channel = await channels.CreateAsync(ApnsChannelStoragePolicy.MostRecentMessageStored, AbortToken);

        // then
        channel.Id.Should().Be(_ChannelId);
        channel.StoragePolicy.Should().Be(ApnsChannelStoragePolicy.MostRecentMessageStored);
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be("POST");
        sent.Path.Should().Be($"/1/apps/{FakeApnsServer.BundleId}/channels");
        sent.Headers.Should().NotContainKey("apns-channel-id");
        sent.Headers["apns-request-id"].Should().NotBeNullOrEmpty();
        sent.Bearer.Should().NotBeNullOrEmpty();
        // Apple's create body names the push type "LiveActivity".
        JsonNode
            .DeepEquals(
                JsonNode.Parse(sent.Body),
                JsonNode.Parse("""{"message-storage-policy":1,"push-type":"LiveActivity"}""")
            )
            .Should()
            .BeTrue(sent.Body);
    }

    [Fact]
    public async Task should_write_storage_policy_0_for_no_message_stored()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(
            201,
            Headers: new Dictionary<string, string>(StringComparer.Ordinal) { ["apns-channel-id"] = _ChannelId }
        );
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        await channels.CreateAsync(ApnsChannelStoragePolicy.NoMessageStored, AbortToken);

        // then
        JsonNode.Parse(_server.Requests.Single().Body)!["message-storage-policy"]!
            .GetValue<int>()
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_throw_when_apns_creates_a_channel_without_an_id()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(201);
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var act = async () => await channels.CreateAsync(ApnsChannelStoragePolicy.NoMessageStored, AbortToken);

        // then
        await act.Should().ThrowAsync<ApnsRequestException>().WithMessage("*no apns-channel-id*");
    }

    [Fact]
    public async Task should_read_a_channel_configuration()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(
            200,
            RawBody: """{"message-storage-policy":0,"push-type":"LiveActivity"}"""
        );
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var channel = await channels.GetAsync(_ChannelId, AbortToken);

        // then
        channel.Should().Be(new ApnsBroadcastChannel(_ChannelId, ApnsChannelStoragePolicy.NoMessageStored));
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be("GET");
        sent.Path.Should().Be($"/1/apps/{FakeApnsServer.BundleId}/channels");
        sent.Headers["apns-channel-id"].Should().Be(_ChannelId);
    }

    [Fact]
    public async Task should_list_every_channel()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(200, RawBody: """{"channels":["YQ==","Yg=="]}""");
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var ids = await channels.ListAsync(AbortToken);

        // then
        ids.Should().Equal("YQ==", "Yg==");
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be("GET");
        sent.Path.Should().Be($"/1/apps/{FakeApnsServer.BundleId}/all-channels");
    }

    [Fact]
    public async Task should_delete_a_channel()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(204);
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        await channels.DeleteAsync(_ChannelId, AbortToken);

        // then
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be("DELETE");
        sent.Path.Should().Be($"/1/apps/{FakeApnsServer.BundleId}/channels");
        sent.Headers["apns-channel-id"].Should().Be(_ChannelId);
    }

    [Fact]
    public async Task should_throw_the_apns_status_reason_and_request_id_on_a_rejection()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(404, "ChannelNotFound");
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var act = async () => await channels.DeleteAsync(_ChannelId, AbortToken);

        // then
        var thrown = (await act.Should().ThrowAsync<ApnsRequestException>()).Which;
        thrown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        thrown.Reason.Should().Be("ChannelNotFound");
        thrown.RequestId.Should().Be(_server.Requests.Single().Headers["apns-request-id"]);
    }

    [Fact]
    public async Task should_refuse_a_blank_channel_id_before_any_request()
    {
        // given
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var get = async () => await channels.GetAsync(" ", AbortToken);
        var delete = async () => await channels.DeleteAsync("", AbortToken);

        // then
        await get.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ApnsEnvironment.Production, "https://api-manage-broadcast.push.apple.com:2196/")]
    [InlineData(ApnsEnvironment.Sandbox, "https://api-manage-broadcast.sandbox.push.apple.com:2195/")]
    public void should_select_apples_channel_management_endpoint_for_the_environment(
        ApnsEnvironment environment,
        string expected
    )
    {
        // given
        var options = new ApnsOptions { BundleId = FakeApnsServer.BundleId, Environment = environment };

        // when
        var address = SetupApnsPushNotifications.GetChannelManagementAddress(options);

        // then
        address.ToString().Should().Be(expected);
    }

    [Fact]
    public void should_resolve_a_channel_service_for_the_default_and_a_named_instance()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(_Options);
            setup.AddNamed("ios", instance => instance.UseApns(_Options));
        });
        using var provider = services.BuildServiceProvider();

        // when
        var defaultService = provider.GetService<IApnsBroadcastChannelService>();
        var named = provider.GetKeyedService<IApnsBroadcastChannelService>("ios");

        // then
        defaultService.Should().NotBeNull();
        named.Should().NotBeNull().And.NotBeSameAs(defaultService);
    }

    #endregion

    #region Helpers

    private void _Options(ApnsOptions options)
    {
        options.KeyId = FakeApnsServer.KeyId;
        options.TeamId = FakeApnsServer.TeamId;
        options.PrivateKey = _server.PrivateKeyPem;
        options.BundleId = FakeApnsServer.BundleId;
    }

    private static ApnsLiveActivityNotification _Update()
    {
        return new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":2}"""),
        };
    }

    private static JsonElement _Filler(int valueLength)
    {
        return _Element($$"""{"f":"{{new string('x', valueLength)}}"}""");
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

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ApnsPushNotificationServiceTests : TestBase
{
    private const string _DeviceToken = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

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

    #region Request shape

    [Fact]
    public async Task should_send_http2_post_with_signed_token_and_headers_when_sending_to_a_device()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        var request = _server.Requests.Should().ContainSingle().Subject;
        request.Protocol.Should().Be("HTTP/2");
        request.Method.Should().Be("POST");
        request.Path.Should().Be($"/3/device/{_DeviceToken}");
        request.Bearer.Should().NotBeNull();
        _server.VerifyJwt(request.Bearer!).Should().BeTrue();
        request.Headers["apns-topic"].Should().Be(FakeApnsServer.BundleId);
        request.Headers["apns-push-type"].Should().Be("alert");
        request.Headers["apns-priority"].Should().Be("10");
        request.Headers.Should().NotContainKey("apns-collapse-id");

        var apnsId = request.Headers["apns-id"];
        Guid.TryParseExact(apnsId, "D", out _).Should().BeTrue();
        apnsId.Should().Be(apnsId.ToLowerInvariant());

        response.IsSucceeded().Should().BeTrue();
        response.ClientIdentifier.Should().Be(_DeviceToken);
        response.MessageId.Should().Be(apnsId);
    }

    [Fact]
    public async Task should_write_alert_under_aps_and_data_as_top_level_keys_when_request_has_data()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();
        var request = PushNotificationRequests.Valid("Hi", "There") with
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["k1"] = "v1" },
        };

        // when
        await service.SendToDeviceAsync(_DeviceToken, request, AbortToken);

        // then
        using var body = JsonDocument.Parse(_server.Requests.Should().ContainSingle().Subject.Body);
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("aps", "k1");
        root.GetProperty("aps").EnumerateObject().Select(p => p.Name).Should().Equal("alert");
        root.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString().Should().Be("Hi");
        root.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString().Should().Be("There");
        root.GetProperty("k1").GetString().Should().Be("v1");
    }

    [Fact]
    public async Task should_send_collapse_id_header_when_request_has_collapse_key()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();
        var request = PushNotificationRequests.Valid() with { CollapseKey = "c1" };

        // when
        await service.SendToDeviceAsync(_DeviceToken, request, AbortToken);

        // then
        _server.Requests.Should().ContainSingle().Subject.Headers["apns-collapse-id"].Should().Be("c1");
    }

    [Fact]
    public async Task should_send_voip_topic_and_push_type_and_accept_larger_payload_when_push_type_is_voip()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.PushType = ApnsPushType.Voip);
        var service = provider.GetRequiredService<IPushNotificationService>();
        var request = PushNotificationRequests.Valid(body: new string('x', 5000 - 50));

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, request, AbortToken);

        // then
        response.IsSucceeded().Should().BeTrue();
        var sent = _server.Requests.Should().ContainSingle().Subject;
        sent.Headers["apns-topic"].Should().Be($"{FakeApnsServer.BundleId}.voip");
        sent.Headers["apns-push-type"].Should().Be("voip");
        Encoding.UTF8.GetByteCount(sent.Body).Should().BeGreaterThan(4096).And.BeLessThanOrEqualTo(5120);
    }

    [Fact]
    public async Task should_reject_a_payload_over_4096_bytes_when_push_type_is_alert()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();
        var request = PushNotificationRequests.Valid(body: new string('x', 5000 - 50));

        // when
        var act = async () => await service.SendToDeviceAsync(_DeviceToken, request, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_priority_5_when_priority_is_power_considerate()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.Priority = ApnsPriority.PowerConsiderate);
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        _server.Requests.Should().ContainSingle().Subject.Headers["apns-priority"].Should().Be("5");
    }

    #endregion

    #region Outcome mapping

    [Theory]
    [InlineData("Unregistered")]
    [InlineData("ExpiredToken")]
    [InlineData("SomeFutureReason")]
    public async Task should_report_unregistered_when_apns_answers_410(string reason)
    {
        // given
        _server.Responder = _ => new FakeApnsReply(410, reason);
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsUnregistered().Should().BeTrue();
        response.ClientIdentifier.Should().Be(_DeviceToken);
    }

    [Fact]
    public async Task should_report_failure_naming_bad_device_token_when_opt_in_is_off()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(400, "BadDeviceToken");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain("BadDeviceToken").And.Contain("400");
    }

    [Fact]
    public async Task should_report_unregistered_for_bad_device_token_when_opt_in_is_on()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(400, "BadDeviceToken");
        await using var provider = _server.CreateProvider(o => o.TreatBadDeviceTokenAsUnregistered = true);
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsUnregistered().Should().BeTrue();
    }

    [Fact]
    public async Task should_report_failure_for_device_token_not_for_topic_even_when_opt_in_is_on()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(400, "DeviceTokenNotForTopic");
        await using var provider = _server.CreateProvider(o => o.TreatBadDeviceTokenAsUnregistered = true);
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain("DeviceTokenNotForTopic");
    }

    [Fact]
    public async Task should_report_failure_naming_503_after_bounded_retries_when_apns_is_unavailable()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(503, "ServiceUnavailable");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain("ServiceUnavailable").And.Contain("503");
        _server.Requests.Should().HaveCount(3, "the initial attempt plus two retries");
    }

    [Fact]
    public async Task should_succeed_when_apns_answers_503_once_then_200()
    {
        // given
        _server.Responder = r => r.Attempt == 1 ? new FakeApnsReply(503, "ServiceUnavailable") : FakeApnsReply.Ok;
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsSucceeded().Should().BeTrue();
        _server.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(429, "TooManyProviderTokenUpdates")]
    [InlineData(403, "InvalidProviderToken")]
    public async Task should_report_failure_after_one_request_when_rejection_is_not_retryable(int status, string reason)
    {
        // given
        _server.Responder = _ => new FakeApnsReply(status, reason);
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain(reason);
        _server.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("")]
    public async Task should_report_failure_naming_http_status_when_error_body_has_no_reason(string rawBody)
    {
        // given
        _server.Responder = _ => new FakeApnsReply(400, RawBody: rawBody);
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain("HTTP 400");
    }

    [Fact]
    public async Task should_report_failure_per_token_when_endpoint_is_unreachable()
    {
        // given
        var closedPort = _GetClosedLoopbackPort();
        await using var provider = _server.CreateProvider(configureClient: c =>
            c.BaseAddress = new Uri($"http://127.0.0.1:{closedPort}")
        );
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var result = await service.SendMulticastAsync(["t1", "t2"], PushNotificationRequests.Valid(), AbortToken);

        // then
        result.FailureCount.Should().Be(2);
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal("t1", "t2");
        result.Responses.Should().AllSatisfy(r => r.FailureError.Should().Contain(nameof(HttpRequestException)));
    }

    [Fact]
    public async Task should_fail_without_sending_when_endpoint_is_cleartext_and_not_loopback()
    {
        // given
        await using var provider = _server.CreateProvider(configureClient: c =>
            c.BaseAddress = new Uri("http://apns.example.com")
        );
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain("http://apns.example.com").And.Contain("not HTTPS");
        _server.Requests.Should().BeEmpty();
    }

    #endregion

    #region Provider token expiry

    [Fact]
    public async Task should_remint_and_retry_once_when_token_older_than_20_minutes_is_rejected_as_expired()
    {
        // given
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        await using var provider = _server.CreateProvider(configureServices: s => s.AddSingleton<TimeProvider>(time));
        var rejected = await _GetCurrentTokenAsync(provider);
        _server.Responder = r =>
            string.Equals(r.Bearer, rejected, StringComparison.Ordinal)
                ? new FakeApnsReply(403, ApnsResponseMapper.ExpiredProviderTokenReason)
                : FakeApnsReply.Ok;
        time.Advance(TimeSpan.FromMinutes(25));
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsSucceeded().Should().BeTrue();
        var requests = _server.Requests;
        requests.Should().HaveCount(2);
        requests[0].Bearer.Should().Be(rejected);
        requests[1].Bearer.Should().NotBe(rejected);
        _server.VerifyJwt(requests[1].Bearer!).Should().BeTrue();
    }

    [Fact]
    public async Task should_remint_exactly_once_when_concurrent_sends_hit_the_same_expired_token()
    {
        // given
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        await using var provider = _server.CreateProvider(configureServices: s => s.AddSingleton<TimeProvider>(time));
        var rejected = await _GetCurrentTokenAsync(provider);
        _server.Responder = r =>
            string.Equals(r.Bearer, rejected, StringComparison.Ordinal)
                ? new FakeApnsReply(403, ApnsResponseMapper.ExpiredProviderTokenReason)
                : FakeApnsReply.Ok;
        // Past the 20-minute re-mint floor but short of the 50-minute scheduled refresh, so only the rejection
        // can drive the re-mint.
        time.Advance(TimeSpan.FromMinutes(30));
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(i =>
                    service.SendToDeviceAsync($"device-{i}", PushNotificationRequests.Valid(), AbortToken).AsTask()
                )
        );

        // then
        responses.Should().AllSatisfy(r => r.IsSucceeded().Should().BeTrue());
        var bearers = _server.DistinctBearers();
        bearers.Should().HaveCount(2);
        bearers.Should().Contain(rejected);
        _server.Requests.Where(r => r.Attempt == 1).Should().AllSatisfy(r => r.Bearer.Should().Be(rejected));
    }

    [Fact]
    public async Task should_report_failure_after_exactly_two_requests_when_apns_rejects_every_token_as_expired()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(403, ApnsResponseMapper.ExpiredProviderTokenReason);
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain(ApnsResponseMapper.ExpiredProviderTokenReason).And.Contain("403");
        _server.Requests.Should().HaveCount(2);
    }

    #endregion

    #region Multicast

    [Fact]
    public async Task should_return_every_response_in_input_order_without_exceeding_max_concurrency()
    {
        // given
        _server.ResponseDelay = TimeSpan.FromMilliseconds(10);
        await using var provider = _server.CreateProvider(o => o.MaxConcurrency = 10);
        var service = provider.GetRequiredService<IPushNotificationService>();
        var tokens = Enumerable.Range(0, 250).Select(i => $"device-{i:D3}").ToArray();

        // when
        var result = await service.SendMulticastAsync(tokens, PushNotificationRequests.Valid(), AbortToken);

        // then
        result.SuccessCount.Should().Be(250);
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal(tokens);
        _server.MaxInFlight.Should().BeInRange(1, 10);
    }

    [Fact]
    public async Task should_keep_input_order_and_counts_when_multicast_outcomes_are_mixed()
    {
        // given
        _server.Responder = r =>
            r.DeviceToken switch
            {
                "gone" => new FakeApnsReply(410, "Unregistered"),
                "bad" => new FakeApnsReply(400, "BadDeviceToken"),
                "broken" => new FakeApnsReply(500, "InternalServerError"),
                _ => FakeApnsReply.Ok,
            };
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();
        string[] tokens = ["ok", "gone", "bad", "broken"];

        // when
        var result = await service.SendMulticastAsync(tokens, PushNotificationRequests.Valid(), AbortToken);

        // then
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(3);
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal(tokens);
        result.Responses[0].IsSucceeded().Should().BeTrue();
        result.Responses[1].IsUnregistered().Should().BeTrue();
        result.Responses[2].IsFailed().Should().BeTrue();
        result.Responses[3].IsFailed().Should().BeTrue();
        _server.Requests.Count(r => r.DeviceToken == "broken").Should().Be(3);
    }

    [Fact]
    public async Task should_return_failure_for_every_token_in_order_without_throwing_during_an_outage()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(503, "ServiceUnavailable");
        await using var provider = _server.CreateProvider(o => o.MaxConcurrency = 100);
        var service = provider.GetRequiredService<IPushNotificationService>();
        var tokens = Enumerable.Range(0, 250).Select(i => $"device-{i:D3}").ToArray();

        // when
        var result = await service.SendMulticastAsync(tokens, PushNotificationRequests.Valid(), AbortToken);

        // then
        result.FailureCount.Should().Be(250);
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal(tokens);
        result.Responses.Should().AllSatisfy(r => r.IsFailed().Should().BeTrue());
        // Fewer requests than 250 x 3 attempts proves the breaker opened and its rejections became failures.
        _server.Requests.Count.Should().BeLessThan(750);
        result.Responses.Should().Contain(r => r.FailureError!.Contains("BrokenCircuitException"));
    }

    [Fact]
    public async Task should_not_retry_or_open_the_breaker_when_apns_throttles_device_tokens()
    {
        // given
        _server.Responder = r =>
            r.DeviceToken == "fresh" ? FakeApnsReply.Ok : new FakeApnsReply(429, "TooManyRequests");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();
        var tokens = Enumerable.Range(0, 150).Select(i => $"device-{i:D3}").ToArray();

        // when
        var throttled = await service.SendMulticastAsync(tokens, PushNotificationRequests.Valid(), AbortToken);
        var later = await service.SendToDeviceAsync("fresh", PushNotificationRequests.Valid(), AbortToken);

        // then
        throttled.FailureCount.Should().Be(150);
        throttled.Responses.Should().AllSatisfy(r => r.FailureError.Should().Contain("TooManyRequests"));
        _server.Requests.Count(r => r.DeviceToken != "fresh").Should().Be(150);
        later.IsSucceeded().Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_for_every_token_when_two_large_multicasts_run_concurrently()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.MaxConcurrency = 1000);
        var service = provider.GetRequiredService<IPushNotificationService>();
        var first = Enumerable.Range(0, 1000).Select(i => $"a-{i:D4}").ToArray();
        var second = Enumerable.Range(0, 1000).Select(i => $"b-{i:D4}").ToArray();

        // when
        var results = await Task.WhenAll(
            service.SendMulticastAsync(first, PushNotificationRequests.Valid(), AbortToken).AsTask(),
            service.SendMulticastAsync(second, PushNotificationRequests.Valid(), AbortToken).AsTask()
        );

        // then
        results.Should().AllSatisfy(r => r.SuccessCount.Should().Be(1000));
        results[0].Responses.Select(r => r.ClientIdentifier).Should().Equal(first);
        results[1].Responses.Select(r => r.ClientIdentifier).Should().Equal(second);
    }

    [Fact]
    public async Task should_throw_before_sending_when_a_multicast_token_is_blank()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var act = async () =>
            await service.SendMulticastAsync(["t1", " ", "t3"], PushNotificationRequests.Valid(), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_operation_canceled_when_caller_cancels_mid_multicast()
    {
        // given
        _server.ResponseDelay = TimeSpan.FromMilliseconds(100);
        await using var provider = _server.CreateProvider(o => o.MaxConcurrency = 5);
        var service = provider.GetRequiredService<IPushNotificationService>();
        var tokens = Enumerable.Range(0, 100).Select(i => $"device-{i:D3}").ToArray();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(250));

        // when
        var act = async () => await service.SendMulticastAsync(tokens, PushNotificationRequests.Valid(), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _server.Requests.Count.Should().BeLessThan(tokens.Length);
    }

    #endregion

    #region Validation

    public static TheoryData<string, PushNotificationRequest> InvalidRequests =>
        new()
        {
            {
                "aps data key",
                _Request(data: new Dictionary<string, string>(StringComparer.Ordinal) { ["aps"] = "x" })
            },
            { "65-byte collapse key", PushNotificationRequests.Valid() with { CollapseKey = new string('c', 65) } },
            { "4097-byte payload", _RequestOfPayloadSize(4097) },
            { "blank title", PushNotificationRequests.Valid(title: " ") },
            { "blank body", PushNotificationRequests.Valid(body: " ") },
        };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task should_throw_argument_exception_without_sending_when_request_is_invalid(
        string scenario,
        PushNotificationRequest request
    )
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var act = async () => await service.SendToDeviceAsync(_DeviceToken, request, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>(scenario);
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_accept_a_payload_of_exactly_4096_bytes_and_a_64_byte_collapse_key()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();
        var request = _RequestOfPayloadSize(4096) with { CollapseKey = new string('c', 64) };

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, request, AbortToken);

        // then
        response.IsSucceeded().Should().BeTrue();
        Encoding.UTF8.GetByteCount(_server.Requests.Should().ContainSingle().Subject.Body).Should().Be(4096);
    }

    [Fact]
    public async Task should_throw_argument_exception_without_sending_when_a_multicast_request_is_null()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var act = async () => await service.SendMulticastAsync(["t1"], null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
        _server.Requests.Should().BeEmpty();
    }

    #endregion

    #region Logging

    [Fact]
    public async Task should_never_log_the_raw_device_token()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        _server.Responder = r =>
            r.Attempt == 1 && r.Body.Contains("fail", StringComparison.Ordinal)
                ? new FakeApnsReply(400, "BadDeviceToken")
                : FakeApnsReply.Ok;
        await using var provider = _server.CreateProvider(loggerProvider: logs);
        var service = provider.GetRequiredService<IPushNotificationService>();
        const string otherToken = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

        // when
        var succeeded = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);
        var failed = await service.SendToDeviceAsync(otherToken, PushNotificationRequests.Valid("fail"), AbortToken);

        // then
        succeeded.IsSucceeded().Should().BeTrue();
        failed.IsFailed().Should().BeTrue();
        logs.Entries.Should().NotBeEmpty();
        logs.Entries.Should().NotContain(e => e.Contains(_DeviceToken, StringComparison.Ordinal));
        logs.Entries.Should().NotContain(e => e.Contains(otherToken, StringComparison.Ordinal));
    }

    #endregion

    #region Helpers

    private static async Task<string> _GetCurrentTokenAsync(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<ApnsOptions>>().Get(Options.DefaultName);
        var token = await provider.GetRequiredService<ApnsTokenSource>().GetTokenAsync(options, AbortToken);

        return token.Value;
    }

    private static PushNotificationRequest _Request(IReadOnlyDictionary<string, string>? data = null)
    {
        return PushNotificationRequests.Valid() with { Data = data };
    }

    private static PushNotificationRequest _RequestOfPayloadSize(int bytes)
    {
        // The body fills whatever the fixed envelope leaves, with one-byte characters.
        var overhead = """{"aps":{"alert":{"title":"T","body":""}}}""".Length;

        return PushNotificationRequests.Valid("T", new string('x', bytes - overhead));
    }

    private static int _GetClosedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyList<string> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(_entries);
        }

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                // The rendered message, every structured value, and the exception all count as logged text.
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                    : "";
                entries.Enqueue($"{formatter(state, exception)} {values} {exception}");
            }
        }
    }

    #endregion
}

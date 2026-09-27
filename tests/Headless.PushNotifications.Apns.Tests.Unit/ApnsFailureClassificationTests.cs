// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Failure-classification tests: every reason in Apple's response table maps onto
/// <see cref="ApnsFailureKind"/>, an unknown reason maps by status class, and a transport fault with no
/// answer is <see cref="ApnsFailureKind.Transport"/>.
/// </summary>
public sealed class ApnsFailureClassificationTests : TestBase
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

    public static TheoryData<string, int, ApnsFailureKind, bool> ReasonKinds { get; } =
        new()
        {
            // 400: bad request shaped problems. Apple: "Most notifications with the status code 4XX can be retried
            // after you fix the error noted in the reason field."
            { "BadCollapseId", 400, ApnsFailureKind.Payload, false },
            { "BadDeviceToken", 400, ApnsFailureKind.DeviceTokenInvalid, false },
            { "BadExpirationDate", 400, ApnsFailureKind.Payload, false },
            { "BadMessageId", 400, ApnsFailureKind.Payload, false },
            { "BadPriority", 400, ApnsFailureKind.Payload, false },
            { "BadTopic", 400, ApnsFailureKind.Configuration, false },
            { "DeviceTokenNotForTopic", 400, ApnsFailureKind.Configuration, false },
            { "DuplicateHeaders", 400, ApnsFailureKind.Payload, false },
            { "IdleTimeout", 400, ApnsFailureKind.Transport, true },
            { "InvalidPushType", 400, ApnsFailureKind.Payload, false },
            { "MissingDeviceToken", 400, ApnsFailureKind.Payload, false },
            { "MissingTopic", 400, ApnsFailureKind.Configuration, false },
            { "PayloadEmpty", 400, ApnsFailureKind.Payload, false },
            { "TopicDisallowed", 400, ApnsFailureKind.Configuration, false },
            // 403: certificate or token problems.
            { "BadCertificate", 403, ApnsFailureKind.Configuration, false },
            { "BadCertificateEnvironment", 403, ApnsFailureKind.Configuration, false },
            { "ExpiredProviderToken", 403, ApnsFailureKind.Authentication, true },
            { "Forbidden", 403, ApnsFailureKind.Configuration, false },
            { "InvalidProviderToken", 403, ApnsFailureKind.Authentication, false },
            { "MissingProviderToken", 403, ApnsFailureKind.Authentication, false },
            { "UnrelatedKeyIdInToken", 403, ApnsFailureKind.Authentication, false },
            { "BadEnvironmentKeyIdInToken", 403, ApnsFailureKind.Authentication, false },
            // 404 / 405: request targeting problems.
            { "BadPath", 404, ApnsFailureKind.Payload, false },
            { "MethodNotAllowed", 405, ApnsFailureKind.Payload, false },
            // 410: the token is gone.
            { "ExpiredToken", 410, ApnsFailureKind.DeviceTokenInvalid, false },
            { "Unregistered", 410, ApnsFailureKind.DeviceTokenInvalid, false },
            // 413: payload size.
            { "PayloadTooLarge", 413, ApnsFailureKind.Payload, false },
            // 429: throttling.
            { "TooManyProviderTokenUpdates", 429, ApnsFailureKind.Authentication, false },
            { "TooManyRequests", 429, ApnsFailureKind.Throttled, true },
            // 5xx: Apple's rule: "After 15 minutes, you can retry JSON payloads that receive response status codes
            // that begin with 5XX."
            { "InternalServerError", 500, ApnsFailureKind.ServerError, true },
            { "ServiceUnavailable", 503, ApnsFailureKind.ServerError, true },
            { "Shutdown", 503, ApnsFailureKind.ServerError, true },
        };

    [Theory]
    [MemberData(nameof(ReasonKinds))]
    public async Task should_classify_every_documented_reason(string reason, int status, ApnsFailureKind kind, bool _)
    {
        // given
        _server.Responder = _ => new FakeApnsReply(status, reason);
        using var logs = new CapturingLoggerProvider();
        await using var provider = _server.CreateProvider(loggerProvider: logs);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(kind);
        result.StatusCode.Should().Be((HttpStatusCode)status);
        result.Reason.Should().Be(reason);
    }

    [Theory]
    [MemberData(nameof(ReasonKinds))]
    public async Task should_report_retryability_for_every_documented_reason(
        string reason,
        int status,
        ApnsFailureKind _1,
        bool retryable
    )
    {
        // given
        _server.Responder = _ => new FakeApnsReply(status, reason);
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.IsRetryable.Should().Be(retryable);
    }

    [Fact]
    public async Task should_report_fifteen_minutes_retry_after_for_server_errors()
    {
        // given - Apple: "After 15 minutes, you can retry JSON payloads that receive response status codes that
        // begin with 5XX."
        _server.Responder = _ => new FakeApnsReply(500, "InternalServerError");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task should_report_retry_after_from_header_when_apns_throttles_with_one()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(429, "TooManyRequests", RetryAfterSeconds: 30);
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(ApnsFailureKind.Throttled);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task should_report_null_retry_after_when_apns_throttles_without_a_header()
    {
        // given - Apple's response-header table does not list a Retry-After for 429.
        _server.Responder = _ => new FakeApnsReply(429, "TooManyRequests");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(ApnsFailureKind.Throttled);
        result.IsRetryable.Should().BeTrue();
        result.RetryAfter.Should().BeNull();
    }

    [Theory]
    [InlineData(400, ApnsFailureKind.Payload)]
    [InlineData(403, ApnsFailureKind.Authentication)]
    [InlineData(404, ApnsFailureKind.Payload)]
    [InlineData(405, ApnsFailureKind.Payload)]
    [InlineData(409, ApnsFailureKind.Payload)]
    [InlineData(410, ApnsFailureKind.DeviceTokenInvalid)]
    [InlineData(413, ApnsFailureKind.Payload)]
    [InlineData(429, ApnsFailureKind.Throttled)]
    [InlineData(500, ApnsFailureKind.ServerError)]
    [InlineData(502, ApnsFailureKind.ServerError)]
    [InlineData(503, ApnsFailureKind.ServerError)]
    public async Task should_classify_an_unknown_reason_by_status_class(int status, ApnsFailureKind kind)
    {
        // given
        _server.Responder = _ => new FakeApnsReply(status, "SomeFutureReason");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(kind);
        result.Reason.Should().Be("SomeFutureReason");
    }

    [Fact]
    public async Task should_classify_a_transport_failure_as_retryable_and_keep_the_documented_failure_error()
    {
        // given
        var closedPort = _GetClosedLoopbackPort();
        await using var provider = _server.CreateProvider(configureClient: c =>
            c.BaseAddress = new Uri($"http://127.0.0.1:{closedPort}")
        );
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(ApnsFailureKind.Transport);
        result.IsRetryable.Should().BeTrue();
        result.StatusCode.Should().BeNull();
        result.Reason.Should().BeNull();
        result.RetryAfter.Should().BeNull();
        // FailureError keeps its documented "<ExceptionType>: <message>" shape; the duplicate risk is documented on
        // ApnsFailureKind.Transport rather than appended to the message.
        result.Response.FailureError.Should().StartWith(nameof(HttpRequestException) + ":");
    }

    [Fact]
    public async Task should_report_no_failure_kind_on_success()
    {
        // given
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().BeNull();
        result.IsRetryable.Should().BeFalse();
        result.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task should_leave_the_shared_response_unchanged_by_classification()
    {
        // given - the shared PushNotificationResponse keeps its Succeeded/Unregistered/Failure shape; only the typed
        // result gains the classification.
        _server.Responder = _ => new FakeApnsReply(410, "Unregistered");
        await using var provider = _server.CreateProvider();
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.Response.IsUnregistered().Should().BeTrue();
        result.Response.FailureError.Should().BeNull();
        result.FailureKind.Should().Be(ApnsFailureKind.DeviceTokenInvalid);
    }

    [Fact]
    public async Task should_log_a_configuration_error_for_invalid_provider_token()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(403, "InvalidProviderToken");
        using var logs = new CapturingLoggerProvider();
        await using var provider = _server.CreateProvider(loggerProvider: logs);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(ApnsFailureKind.Authentication);
        logs.Entries.Should()
            .Contain(e =>
                e.Contains("InvalidProviderToken", StringComparison.Ordinal)
                && e.Contains("configuration", StringComparison.OrdinalIgnoreCase)
            );
    }

    [Theory]
    [InlineData("InvalidProviderToken")]
    [InlineData("MissingProviderToken")]
    [InlineData("UnrelatedKeyIdInToken")]
    public async Task should_log_a_distinct_configuration_error_for_token_configuration_rejections(string reason)
    {
        // given
        _server.Responder = _ => new FakeApnsReply(403, reason);
        using var logs = new CapturingLoggerProvider();
        await using var provider = _server.CreateProvider(loggerProvider: logs);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then - the event names the reason and calls it a configuration problem to fix, distinct from the generic
        // rejection warning.
        logs.Entries.Should().Contain(e => e.Contains(reason, StringComparison.Ordinal));
        logs.Entries.Should().Contain(e => e.Contains("ApnsTokenConfigurationError", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_log_a_distinct_error_naming_the_twenty_minute_rule_for_token_update_throttling()
    {
        // given - Apple: "Update the authentication token no more than once every 20 minutes."
        _server.Responder = _ => new FakeApnsReply(429, "TooManyProviderTokenUpdates");
        using var logs = new CapturingLoggerProvider();
        await using var provider = _server.CreateProvider(loggerProvider: logs);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.FailureKind.Should().Be(ApnsFailureKind.Authentication);
        logs.Entries.Should()
            .Contain(e =>
                e.Contains("TooManyProviderTokenUpdates", StringComparison.Ordinal)
                && e.Contains("20 minutes", StringComparison.Ordinal)
            );
    }

    private static int _GetClosedLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    /// <summary>Captures rendered log messages, structured values, and exception text as one string per entry.</summary>
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
                // The rendered message, the event name, every structured value, and the exception all count as
                // logged text.
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                    : "";
                entries.Enqueue($"{eventId.Name} {formatter(state, exception)} {values} {exception}");
            }
        }
    }
}

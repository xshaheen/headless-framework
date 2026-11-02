// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Tests;

/// <summary>
/// APNs' own retry rules must hold when the host adds the standard resilience handler to every client (as
/// ServiceDefaults does). That handler would otherwise wrap the provider's pipeline: its stock retry resends a push
/// on 429 and 5xx, which APNs does not deduplicate, and Apple asks senders to wait about 15 minutes after a 5xx.
/// </summary>
public sealed class ApnsResilienceTests : TestBase
{
    // A well-formed 64-hex-digit token; built at run time because a literal of that shape reads as a secret to scanners.
    private static readonly string _DeviceToken = new('a', 64);

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

    [Theory]
    [InlineData(429, "TooManyRequests")]
    [InlineData(500, "InternalServerError")]
    [InlineData(503, "ServiceUnavailable")]
    public async Task should_send_push_once_on_throttle_or_server_error_when_a_host_wide_handler_is_configured(
        int status,
        string reason
    )
    {
        // given
        _server.Responder = _ => new FakeApnsReply(status, reason);
        await using var provider = _server.CreateProvider(configureServices: _AddHostWideHandler);
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        _server.Requests.Should().ContainSingle("the host-wide handler must not wrap the APNs pipeline");
    }

    [Fact]
    public async Task should_send_channel_create_once_on_server_error_when_a_host_wide_handler_is_configured()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(503, "ServiceUnavailable");
        await using var provider = _server.CreateProvider(configureServices: _AddHostWideHandler);
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        var act = async () => await channels.CreateAsync(ApnsChannelStoragePolicy.NoMessageStored, AbortToken);

        // then
        await act.Should().ThrowAsync<ApnsRequestException>();
        _server.Requests.Should().ContainSingle("the channel client follows the same rules as the push client");
    }

    [Fact]
    public async Task should_still_retry_a_pre_send_fault_when_a_host_wide_handler_is_configured()
    {
        // given - a refused connect proves the push never left, so APNs' own rule retries it.
        using var connectFailures = new ConnectFailingHandler(failures: 1);
        await using var provider = _server.CreateProvider(
            configureServices: _AddHostWideHandler,
            postConfigureServices: s =>
                s.AddHttpClient(SetupApnsPushNotifications.HttpClientName).AddHttpMessageHandler(() => connectFailures)
        );
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsSucceeded().Should().BeTrue();
        connectFailures.Attempts.Should().Be(2);
        _server.Requests.Should().ContainSingle();
    }

    // A fixed 5ms delay keeps a regression (a retried send) fast to report instead of waiting out the stock backoff.
    private static void _AddHostWideHandler(IServiceCollection services)
    {
        services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = DelayBackoffType.Constant;
                options.Retry.UseJitter = false;
                options.Retry.Delay = TimeSpan.FromMilliseconds(5);
            })
        );
    }

    /// <summary>Fails the first <c>failures</c> attempts the way a refused TCP connect does, before any byte is sent.</summary>
    private sealed class ConnectFailingHandler(int failures) : DelegatingHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Interlocked.Increment(ref _attempts) <= failures
                ? Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.")
                )
                : base.SendAsync(request, cancellationToken);
        }
    }
}

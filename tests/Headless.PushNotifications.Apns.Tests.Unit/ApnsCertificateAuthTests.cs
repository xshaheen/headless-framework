// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ApnsCertificateAuthTests : TestBase
{
    private const string _Password = "p12-test-password";
    private const string _DeviceToken = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    private X509Certificate2 _serverCertificate = null!;
    private X509Certificate2 _clientCertificate = null!;
    private string _clientPkcs12 = null!;
    private FakeApnsServer _server = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        _serverCertificate = TestCertificates.CreateServer(now.AddDays(-1), now.AddDays(30));
        _clientCertificate = TestCertificates.CreateClient(now.AddDays(-1), now.AddDays(300));
        _clientPkcs12 = TestCertificates.ToPkcs12Base64(_clientCertificate, _Password);
        _server = await FakeApnsServer.StartTlsAsync(_serverCertificate, _clientCertificate.Thumbprint, AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _server.DisposeAsync();
        _serverCertificate.Dispose();
        _clientCertificate.Dispose();
        await base.DisposeAsyncCore();
    }

    #region Sending

    [Fact]
    public async Task should_present_the_client_certificate_without_a_bearer_when_using_certificate_mode()
    {
        // given
        await using var provider = _server.CreateCertificateProvider(_clientPkcs12, _Password);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        result.Response.IsSucceeded().Should().BeTrue();
        var request = _server.Requests.Should().ContainSingle().Subject;
        request.Protocol.Should().Be("HTTP/2");
        request.ClientCertificateThumbprint.Should().BeEquivalentTo(_clientCertificate.Thumbprint);
        request.Bearer.Should().BeNull();
        request.Headers.Should().NotContainKey("authorization");
        request.Headers["apns-topic"].Should().Be(FakeApnsServer.BundleId);
    }

    public static TheoryData<ApnsNotification> TokenOnlyNotifications =>
        new()
        {
            new ApnsLocationNotification(),
            new ApnsFileProviderNotification { ContainerIdentifier = "c", Domain = "d" },
            new ApnsLiveActivityNotification { Event = ApnsLiveActivityEvent.End },
            new ApnsWidgetsNotification(),
            new ApnsControlsNotification(),
        };

    [Theory]
    [MemberData(nameof(TokenOnlyNotifications))]
    public async Task should_refuse_a_token_only_push_type_before_any_request_when_using_certificate_mode(
        ApnsNotification notification
    )
    {
        // given
        await using var provider = _server.CreateCertificateProvider(_clientPkcs12, _Password);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var single = async () => await service.SendAsync(_DeviceToken, notification, AbortToken);
        var multicast = async () => await service.SendMulticastAsync([_DeviceToken], notification, AbortToken);

        // then
        (await single.Should().ThrowAsync<ArgumentException>()).WithMessage("*certificate*");
        await multicast.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    public static TheoryData<ApnsNotification, bool> CertificateNotifications =>
        new()
        {
            {
                new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
                false
            },
            { new ApnsBackgroundNotification(), false },
            {
                new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Call" } },
                true
            },
            { new ApnsPushToTalkNotification(), false },
            { new ApnsComplicationNotification(), false },
        };

    [Theory]
    [MemberData(nameof(CertificateNotifications))]
    public async Task should_send_the_push_types_a_certificate_allows(ApnsNotification notification, bool voip)
    {
        // given
        await using var provider = _server.CreateCertificateProvider(
            _clientPkcs12,
            _Password,
            o => o.PushType = voip ? ApnsPushType.Voip : ApnsPushType.Alert
        );
        var service = provider.GetRequiredService<IApnsPushNotificationService>();

        // when
        var result = await service.SendAsync(_DeviceToken, notification, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue(result.Response.FailureError);
        var request = _server.Requests.Should().ContainSingle().Subject;
        request.Bearer.Should().BeNull();
        request.ClientCertificateThumbprint.Should().BeEquivalentTo(_clientCertificate.Thumbprint);
    }

    [Fact]
    public async Task should_map_expired_provider_token_to_failure_without_retry_or_token_source_when_using_certificate_mode()
    {
        // given
        var tokenSourceCreations = 0;
        _server.Responder = _ => new FakeApnsReply(403, ApnsResponseMapper.ExpiredProviderTokenReason);
        await using var provider = _server.CreateCertificateProvider(
            _clientPkcs12,
            _Password,
            configureServices: services =>
                services.AddSingleton(_ =>
                {
                    Interlocked.Increment(ref tokenSourceCreations);

                    return new ApnsTokenSource(TimeProvider.System);
                })
        );
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var response = await service.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsFailed().Should().BeTrue();
        response.FailureError.Should().Contain(ApnsResponseMapper.ExpiredProviderTokenReason);
        _server.Requests.Should().ContainSingle();
        tokenSourceCreations.Should().Be(0);
    }

    [Fact]
    public async Task should_never_construct_the_token_source_when_resolving_certificate_mode_services()
    {
        // given
        var tokenSourceCreations = 0;
        await using var provider = _server.CreateCertificateProvider(
            _clientPkcs12,
            _Password,
            configureServices: services =>
                services.AddSingleton(_ =>
                {
                    Interlocked.Increment(ref tokenSourceCreations);

                    return new ApnsTokenSource(TimeProvider.System);
                })
        );

        // when
        var typed = provider.GetRequiredService<IApnsPushNotificationService>();
        var shared = provider.GetRequiredService<IPushNotificationService>();
        await shared.SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        // then
        typed.Should().BeSameAs(shared);
        tokenSourceCreations.Should().Be(0);
    }

    #endregion

    #region Expiry check

    [Fact]
    public async Task should_warn_at_host_start_when_the_certificate_expires_within_30_days()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        using var certificate = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(10)
        );
        var pkcs12 = TestCertificates.ToPkcs12Base64(certificate, _Password);
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildHost(clock, pkcs12, logs);

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        var warning = logs.Entries.Should().ContainSingle(e => e.EventName == "ApnsCertificateExpiringSoon").Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        logs.Entries.Should().NotContain(e => e.Text.Contains(_Password, StringComparison.Ordinal));
        var certificatePrefix = pkcs12[..40];
        logs.Entries.Should().NotContain(e => e.Text.Contains(certificatePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_not_warn_at_host_start_when_the_certificate_expires_after_30_days()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        using var certificate = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(60)
        );
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildHost(clock, TestCertificates.ToPkcs12Base64(certificate, _Password), logs);

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        logs.Entries.Should().NotContain(e => e.EventName == "ApnsCertificateExpiringSoon");
    }

    [Fact]
    public async Task should_fail_host_start_when_the_certificate_has_expired()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        using var certificate = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(-1)
        );
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildHost(clock, TestCertificates.ToPkcs12Base64(certificate, _Password), logs);

        // when
        var act = async () => await host.StartAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("expired")
            .And.NotContain(_Password);
    }

    [Fact]
    public async Task should_fail_the_expiry_check_start_when_the_certificate_has_expired()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        using var certificate = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(-1)
        );
        var options = new ApnsOptions
        {
            BundleId = FakeApnsServer.BundleId,
            Certificate = TestCertificates.ToPkcs12Base64(certificate, _Password),
            CertificatePassword = _Password,
        };
        var monitor = new TestOptionsMonitor(options);
        using var holder = _CreateHolder(monitor, clock);
        var check = new ApnsCertificateExpiryCheck(
            monitor,
            name: null,
            () => holder,
            clock,
            NullLogger<ApnsCertificateExpiryCheck>.Instance
        );

        // when
        var act = async () => await check.StartAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("expired");
    }

    [Fact]
    public async Task should_skip_the_expiry_check_when_using_token_mode()
    {
        // given
        var options = new ApnsOptions
        {
            BundleId = FakeApnsServer.BundleId,
            KeyId = FakeApnsServer.KeyId,
            TeamId = FakeApnsServer.TeamId,
            PrivateKey = "unused",
        };
        var check = new ApnsCertificateExpiryCheck(
            new TestOptionsMonitor(options),
            name: null,
            () => throw new InvalidOperationException("The holder must not be resolved in token mode."),
            TimeProvider.System,
            NullLogger<ApnsCertificateExpiryCheck>.Instance
        );

        // when
        var act = async () => await check.StartAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Rotation

    [Fact]
    public async Task should_present_the_renewed_certificate_on_new_connections_after_the_configuration_reloads()
    {
        // given
        using var renewed = TestCertificates.CreateClient(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365)
        );
        _server.AcceptClientCertificate(renewed.Thumbprint);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [nameof(ApnsOptions.BundleId)] = FakeApnsServer.BundleId,
                    [nameof(ApnsOptions.Certificate)] = _clientPkcs12,
                    [nameof(ApnsOptions.CertificatePassword)] = _Password,
                }
            )
            .Build();
        // The provider opens a new connection per request, so each send shows the certificate a new connection
        // presents at that moment.
        await using var provider = _server.CreateCertificateProvider(configuration);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } };
        (await service.SendAsync(_DeviceToken, notification, AbortToken)).Response.IsSucceeded().Should().BeTrue();

        // when
        configuration[nameof(ApnsOptions.Certificate)] = TestCertificates.ToPkcs12Base64(renewed, "renewed-password");
        configuration[nameof(ApnsOptions.CertificatePassword)] = "renewed-password";
        configuration.Reload();
        var result = await service.SendAsync(_DeviceToken, notification, AbortToken);

        // then
        result.Response.IsSucceeded().Should().BeTrue(result.Response.FailureError);
        _server
            .Requests.Select(r => r.ClientCertificateThumbprint)
            .Should()
            .Equal(_clientCertificate.Thumbprint, renewed.Thumbprint);
    }

    public static TheoryData<string> InvalidRenewals =>
        new() { "expired", "wrong_password", "no_private_key", "not_base64", "no_certificate" };

    [Theory]
    [MemberData(nameof(InvalidRenewals))]
    public void should_keep_the_current_certificate_and_log_an_error_when_the_renewed_certificate_is_invalid(
        string scenario
    )
    {
        // given
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new TestOptionsMonitor(_CertificateOptions(_clientPkcs12));
        using var logs = new CapturingLoggerProvider();
        using var holder = _CreateHolder(monitor, clock, logs);
        using var expired = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(-1)
        );
        const string renewedPassword = "renewed-password";
        var renewed = scenario switch
        {
            "expired" => _CertificateOptions(
                TestCertificates.ToPkcs12Base64(expired, renewedPassword),
                renewedPassword
            ),
            "wrong_password" => _CertificateOptions(_clientPkcs12, renewedPassword),
            "no_private_key" => _CertificateOptions(
                TestCertificates.ToPublicOnlyPkcs12Base64(_clientCertificate, renewedPassword),
                renewedPassword
            ),
            "not_base64" => _CertificateOptions("not base64 " + renewedPassword, renewedPassword),
            "no_certificate" => new ApnsOptions
            {
                BundleId = FakeApnsServer.BundleId,
                KeyId = FakeApnsServer.KeyId,
                TeamId = FakeApnsServer.TeamId,
                PrivateKey = renewedPassword,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        // when
        monitor.Change(renewed);

        // then
        holder.Certificate.Thumbprint.Should().Be(_clientCertificate.Thumbprint);
        var error = logs.Entries.Should().ContainSingle(e => e.EventName == "ApnsCertificateReloadFailed").Subject;
        error.Level.Should().Be(LogLevel.Error);
        logs.Entries.Should().NotContain(e => e.Text.Contains(renewedPassword, StringComparison.Ordinal));
        logs.Entries.Should().NotContain(e => e.Text.Contains(_Password, StringComparison.Ordinal));
        var certificatePrefix = _clientPkcs12[..40];
        logs.Entries.Should().NotContain(e => e.Text.Contains(certificatePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void should_swap_in_a_valid_renewed_certificate_and_keep_the_replaced_one_usable()
    {
        // given
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new TestOptionsMonitor(_CertificateOptions(_clientPkcs12));
        using var logs = new CapturingLoggerProvider();
        using var holder = _CreateHolder(monitor, clock, logs);
        var original = holder.Certificate;
        using var renewed = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-1),
            clock.GetUtcNow().AddDays(365)
        );

        // when
        monitor.Change(_CertificateOptions(TestCertificates.ToPkcs12Base64(renewed, _Password)));

        // then
        holder.Certificate.Thumbprint.Should().Be(renewed.Thumbprint);
        // An in-flight handshake may still hold the replaced certificate, so it is not disposed yet.
        original.HasPrivateKey.Should().BeTrue();
        original.Thumbprint.Should().Be(_clientCertificate.Thumbprint);
        logs.Entries.Should().ContainSingle(e => e.EventName == "ApnsCertificateReloaded");
        logs.Entries.Should().NotContain(e => e.Text.Contains(_Password, StringComparison.Ordinal));
    }

    [Fact]
    public void should_not_reload_when_a_change_leaves_the_certificate_fields_alone()
    {
        // given
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new TestOptionsMonitor(_CertificateOptions(_clientPkcs12));
        using var logs = new CapturingLoggerProvider();
        using var holder = _CreateHolder(monitor, clock, logs);
        var original = holder.Certificate;
        var changed = _CertificateOptions(_clientPkcs12);
        changed.Environment = ApnsEnvironment.Sandbox;
        changed.MaxConcurrency = 7;

        // when
        monitor.Change(changed);

        // then
        holder.Certificate.Should().BeSameAs(original);
        logs.Entries.Should()
            .NotContain(e =>
                e.EventName != null && e.EventName.StartsWith("ApnsCertificate", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void should_ignore_a_change_to_another_instance()
    {
        // given
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new TestOptionsMonitor(_CertificateOptions(_clientPkcs12));
        using var holder = _CreateHolder(monitor, clock);
        var original = holder.Certificate;
        using var renewed = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-1),
            clock.GetUtcNow().AddDays(365)
        );

        // when
        monitor.Change(_CertificateOptions(TestCertificates.ToPkcs12Base64(renewed, _Password)), name: "other");

        // then
        holder.Certificate.Should().BeSameAs(original);
    }

    [Fact]
    public void should_stop_observing_changes_when_disposed()
    {
        // given
        var monitor = new TestOptionsMonitor(_CertificateOptions(_clientPkcs12));
        var holder = _CreateHolder(monitor, TimeProvider.System);
        monitor.ListenerCount.Should().Be(1);

        // when
        holder.Dispose();

        // then
        monitor.ListenerCount.Should().Be(0);
    }

    [Fact]
    public async Task should_never_construct_the_certificate_holder_when_using_token_mode()
    {
        // given: the holder refuses to build from token-mode options, so starting and sending prove it is never built
        await using var server = await FakeApnsServer.StartAsync(AbortToken);
        await using var provider = server.CreateProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ApnsOptions>>();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        // when
        foreach (var hosted in hostedServices)
        {
            await hosted.StartAsync(AbortToken);
        }

        var response = await provider
            .GetRequiredService<IPushNotificationService>()
            .SendToDeviceAsync(_DeviceToken, PushNotificationRequests.Valid(), AbortToken);

        foreach (var hosted in hostedServices)
        {
            await hosted.StopAsync(AbortToken);
        }

        // then
        response.IsSucceeded().Should().BeTrue(response.FailureError);
        server.Requests.Should().ContainSingle().Which.Bearer.Should().NotBeNull();
        var buildHolder = () => _CreateHolder(monitor, TimeProvider.System);
        buildHolder.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region Periodic expiry check

    [Fact]
    public async Task should_warn_on_a_later_check_once_30_days_remain_and_log_an_error_once_expired()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        using var certificate = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(32).AddHours(12)
        );
        var pkcs12 = TestCertificates.ToPkcs12Base64(certificate, _Password);
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildHost(clock, pkcs12, logs);
        await host.StartAsync(AbortToken);
        logs.Entries.Should().NotContain(e => e.EventName == "ApnsCertificateExpiringSoon");

        // when: two daily checks bring the expiry within 30 days
        clock.Advance(ApnsCertificateExpiryCheck.RecheckPeriod);
        clock.Advance(ApnsCertificateExpiryCheck.RecheckPeriod);
        clock.Advance(ApnsCertificateExpiryCheck.RecheckPeriod);

        // then
        var warning = logs.Entries.Should().ContainSingle(e => e.EventName == "ApnsCertificateExpiringSoon").Subject;
        warning.Level.Should().Be(LogLevel.Warning);

        // when: the checks run past the expiry
        for (var day = 0; day < 31; day++)
        {
            clock.Advance(ApnsCertificateExpiryCheck.RecheckPeriod);
        }

        // then
        logs.Entries.Where(e => e.EventName == "ApnsCertificateExpired")
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(e => e.Level == LogLevel.Error);
        logs.Entries.Should().NotContain(e => e.Text.Contains(_Password, StringComparison.Ordinal));
        var certificatePrefix = pkcs12[..40];
        logs.Entries.Should().NotContain(e => e.Text.Contains(certificatePrefix, StringComparison.Ordinal));

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_check_the_current_certificate_and_stop_checking_after_stop()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        using var certificate = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-300),
            clock.GetUtcNow().AddDays(20)
        );
        using var renewed = TestCertificates.CreateClient(
            clock.GetUtcNow().AddDays(-1),
            clock.GetUtcNow().AddDays(365)
        );
        var monitor = new TestOptionsMonitor(
            _CertificateOptions(TestCertificates.ToPkcs12Base64(certificate, _Password))
        );
        using var logs = new CapturingLoggerProvider();
        using var holder = _CreateHolder(monitor, clock, logs);
        using var loggerFactory = new LoggerFactory([logs]);
        using var check = new ApnsCertificateExpiryCheck(
            monitor,
            name: null,
            () => holder,
            clock,
            loggerFactory.CreateLogger<ApnsCertificateExpiryCheck>()
        );
        await check.StartAsync(AbortToken);
        logs.Entries.Count(e => e.EventName == "ApnsCertificateExpiringSoon").Should().Be(1);

        // when: the certificate is renewed, so the next check reads the renewed one
        monitor.Change(_CertificateOptions(TestCertificates.ToPkcs12Base64(renewed, _Password)));
        clock.Advance(ApnsCertificateExpiryCheck.RecheckPeriod);

        // then
        logs.Entries.Count(e => e.EventName == "ApnsCertificateExpiringSoon").Should().Be(1);

        // when: stopped, no later check runs even once the renewed certificate would warn
        await check.StopAsync(AbortToken);
        clock.Advance(TimeSpan.FromDays(400));

        // then
        logs.Entries.Count(e => e.EventName == "ApnsCertificateExpiringSoon").Should().Be(1);
        logs.Entries.Should().NotContain(e => e.EventName == "ApnsCertificateExpired");
    }

    #endregion

    #region Options

    [Fact]
    public void should_hide_the_certificate_and_password_from_to_string_and_json()
    {
        // given
        var options = new ApnsOptions
        {
            BundleId = FakeApnsServer.BundleId,
            Certificate = _clientPkcs12,
            CertificatePassword = _Password,
        };

        // when
        var text = options.ToString();
        var json = JsonSerializer.Serialize(options);

        // then
        text.Should().NotContain(_clientPkcs12).And.NotContain(_Password).And.Contain("[REDACTED]");
        json.Should().NotContain(_clientPkcs12).And.NotContain(_Password);
        json.Should().NotContain(nameof(ApnsOptions.Certificate));
    }

    [Fact]
    public async Task should_carry_certificate_mode_through_a_prebuilt_options_instance()
    {
        // given
        var tokenSourceCreations = 0;
        var options = new ApnsOptions
        {
            BundleId = FakeApnsServer.BundleId,
            Certificate = _clientPkcs12,
            CertificatePassword = _Password,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ =>
        {
            Interlocked.Increment(ref tokenSourceCreations);

            return new ApnsTokenSource(TimeProvider.System);
        });
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(options);
            setup.AddNamed("named", i => i.UseApns(options));
        });
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        var monitor = provider.GetRequiredService<IOptionsMonitor<ApnsOptions>>();

        // when
        var resolved = new[] { monitor.Get(Options.DefaultName), monitor.Get("named") };
        provider.GetRequiredService<IPushNotificationService>();
        provider.GetRequiredKeyedService<IPushNotificationService>("named");

        // then
        resolved.Should().AllSatisfy(r => r.Should().BeEquivalentTo(options));
        resolved.Should().AllSatisfy(r => r.Certificate.Should().Be(_clientPkcs12));
        resolved.Should().AllSatisfy(r => r.CertificatePassword.Should().Be(_Password));
        tokenSourceCreations.Should().Be(0);
    }

    #endregion

    #region Helpers

    private static IHost _BuildHost(FakeTimeProvider clock, string pkcs12, ILoggerProvider logs)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddHeadlessPushNotifications(setup =>
            setup.UseApns(o =>
            {
                o.BundleId = FakeApnsServer.BundleId;
                o.Certificate = pkcs12;
                o.CertificatePassword = _Password;
            })
        );

        return builder.Build();
    }

    private static ApnsCertificateHolder _CreateHolder(
        IOptionsMonitor<ApnsOptions> monitor,
        TimeProvider clock,
        ILoggerProvider? logs = null
    )
    {
        ILoggerFactory loggerFactory = logs is null ? NullLoggerFactory.Instance : new LoggerFactory([logs]);

        return new ApnsCertificateHolder(
            monitor,
            name: null,
            clock,
            loggerFactory.CreateLogger<ApnsCertificateHolder>()
        );
    }

    private static ApnsOptions _CertificateOptions(string pkcs12, string? password = _Password)
    {
        return new ApnsOptions
        {
            BundleId = FakeApnsServer.BundleId,
            Certificate = pkcs12,
            CertificatePassword = password,
        };
    }

    /// <summary>An options monitor whose value the test replaces, raising the change listeners on demand.</summary>
    private sealed class TestOptionsMonitor(ApnsOptions options) : IOptionsMonitor<ApnsOptions>
    {
        private readonly Lock _gate = new();
        private readonly List<Action<ApnsOptions, string?>> _listeners = [];
        public ApnsOptions CurrentValue { get; private set; } = options;

        public int ListenerCount
        {
            get
            {
                lock (_gate)
                {
                    return _listeners.Count;
                }
            }
        }

        public ApnsOptions Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable OnChange(Action<ApnsOptions, string?> listener)
        {
            lock (_gate)
            {
                _listeners.Add(listener);
            }

            return new Subscription(this, listener);
        }

        /// <summary>Replaces the value and notifies the listeners for <paramref name="name"/>, as a reload does.</summary>
        public void Change(ApnsOptions changed, string name = "")
        {
            CurrentValue = changed;
            Action<ApnsOptions, string?>[] listeners;

            lock (_gate)
            {
                listeners = [.. _listeners];
            }

            foreach (var listener in listeners)
            {
                listener(changed, name);
            }
        }

        private sealed class Subscription(TestOptionsMonitor owner, Action<ApnsOptions, string?> listener) : IDisposable
        {
            public void Dispose()
            {
                lock (owner._gate)
                {
                    owner._listeners.Remove(listener);
                }
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string? EventName, string Text);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(_entries);
        }

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
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
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                    : "";
                entries.Enqueue(
                    new LogEntry(logLevel, eventId.Name, $"{formatter(state, exception)} {values} {exception}")
                );
            }
        }
    }

    #endregion
}

/// <summary>Self-signed certificates generated in process, so no certificate file or secret is committed.</summary>
internal static class TestCertificates
{
    private const string _ServerAuthOid = "1.3.6.1.5.5.7.3.1";
    private const string _ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    public static X509Certificate2 CreateServer(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        return _Create("CN=localhost", _ServerAuthOid, notBefore, notAfter, addLoopbackNames: true);
    }

    public static X509Certificate2 CreateClient(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        return _Create("CN=Apple Push Services: com.example.app", _ClientAuthOid, notBefore, notAfter, false);
    }

    public static string ToPkcs12Base64(X509Certificate2 certificate, string? password)
    {
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, password));
    }

    /// <summary>A PKCS#12 file that carries only the public certificate, with no private key.</summary>
    public static string ToPublicOnlyPkcs12Base64(X509Certificate2 certificate, string? password)
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);

        return Convert.ToBase64String(publicOnly.Export(X509ContentType.Pkcs12, password));
    }

    private static X509Certificate2 _Create(
        string subject,
        string usageOid,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        bool addLoopbackNames
    )
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usageOid)], critical: false));

        if (addLoopbackNames)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(System.Net.IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }

        using var created = request.CreateSelfSigned(notBefore, notAfter);

        // Round-tripped through PKCS#12 so every platform's TLS stack can use the key; Exportable keeps the key
        // exportable after macOS imports it into a keychain, so the tests can build .p12 files from it.
        return X509CertificateLoader.LoadPkcs12(
            created.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable
        );
    }
}

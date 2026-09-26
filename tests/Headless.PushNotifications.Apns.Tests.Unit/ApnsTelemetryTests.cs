// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Metrics and tracing tests, driven with <see cref="MeterListener"/> and <see cref="ActivityListener"/> so no
/// OpenTelemetry SDK is required. Asserts on the meter name, the instruments, their tags, and that a device token or
/// payload never appears as a tag.
/// </summary>
/// <remarks>
/// Both listeners are process-global and other test classes send in parallel, so every assertion filters to this
/// test's own measurements: the meter callback only captures instruments of the APNs meter, and the activity
/// assertions filter on tags unique to this test's instance (a sandbox environment).
/// </remarks>
public sealed class ApnsTelemetryTests : TestBase
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

    [Fact]
    public async Task should_count_sends_tagged_with_outcome_kind_reason_push_type_and_environment()
    {
        // given - the sandbox tag is unique to this test's instance, so parallel sends from other classes are not
        // counted.
        _server.Responder = r =>
            r.DeviceToken switch
            {
                "ok" => FakeApnsReply.Ok,
                "gone" => new FakeApnsReply(410, "Unregistered"),
                "broken" => new FakeApnsReply(500, "InternalServerError"),
                _ => FakeApnsReply.Ok,
            };
        await using var provider = _server.CreateProvider(o => o.Environment = ApnsEnvironment.Sandbox);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        using var capture = new ApnsMeterCapture();
        using var listener = capture.Start();

        // when
        await service.SendMulticastAsync(
            ["ok", "gone", "broken"],
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then - the unregistered and server-error outcomes are unique to this test's sends, and the succeeded one
        // is isolated by its reason tag carrying the push type triplet this test sends, so parallel sends from other
        // tests in this class do not count.
        var sends = capture
            .Sends()
            .Where(m =>
                _HasTag(m, ApnsTags.Environment, "sandbox")
                && (
                    _HasTag(m, ApnsTags.Reason, "Unregistered")
                    || _HasTag(m, ApnsTags.Reason, "InternalServerError")
                    || _LacksTag(m, ApnsTags.Reason)
                )
            )
            .ToArray();
        sends.Length.Should().Be(3);

        var ok = sends.Single(m => _HasTag(m, ApnsTags.Outcome, "succeeded"));
        _HasTag(ok, ApnsTags.PushType, "alert").Should().BeTrue();
        _HasTag(ok, ApnsTags.Environment, "sandbox").Should().BeTrue();
        _LacksTag(ok, ApnsTags.FailureKind).Should().BeTrue();

        var gone = sends.Single(m => _HasTag(m, ApnsTags.Outcome, "unregistered"));
        _HasTag(gone, ApnsTags.FailureKind, "device_token_invalid").Should().BeTrue();
        _HasTag(gone, ApnsTags.Reason, "Unregistered").Should().BeTrue();

        var broken = sends.Single(m => _HasTag(m, ApnsTags.Outcome, "failed"));
        _HasTag(broken, ApnsTags.FailureKind, "server_error").Should().BeTrue();
        _HasTag(broken, ApnsTags.Reason, "InternalServerError").Should().BeTrue();
    }

    [Fact]
    public async Task should_record_send_duration_with_push_type_and_environment()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.Environment = ApnsEnvironment.Sandbox);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        using var capture = new ApnsMeterCapture();
        using var listener = capture.Start();

        // when
        await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then - filter to the sandbox environment, which only this class sends with, so parallel production sends
        // from other test classes do not count.
        var durations = capture
            .Measurements("headless.apns.send.duration")
            .Where(m => _HasTag(m, ApnsTags.Environment, "sandbox"))
            .ToArray();
        durations.Length.Should().Be(1);
        _HasTag(durations[0], ApnsTags.PushType, "alert").Should().BeTrue();
    }

    [Fact]
    public async Task should_count_minted_provider_tokens()
    {
        // given - sandbox filters this test's sends out of other classes' parallel production sends; the token
        // mint is counted once per key identity, not per environment, so a unique key keeps it isolated too.
        await using var provider = _server.CreateProvider(o => o.Environment = ApnsEnvironment.Sandbox);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        using var capture = new ApnsMeterCapture();
        using var listener = capture.Start();

        // when - the first send mints the provider token; the second reuses it.
        await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );
        await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Again" } },
            AbortToken
        );

        // then - the counter is process-global and other test classes may mint concurrently, so the count is a
        // lower bound; the single distinct bearer proves this source minted once and reused it.
        capture.Measurements("headless.apns.provider_tokens.minted").Length.Should().BeGreaterThanOrEqualTo(1);
        _server.DistinctBearers().Should().ContainSingle();
    }

    [Fact]
    public async Task should_start_one_activity_per_device_send_and_never_tag_the_token_or_payload()
    {
        // given - the sandbox environment tag distinguishes this test's sends from other classes' parallel ones.
        await using var provider = _server.CreateProvider(o => o.Environment = ApnsEnvironment.Sandbox);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        using var listener = _StartSandboxActivityListener(out var stopped);

        // when
        await service.SendMulticastAsync(
            [_DeviceToken, "ffeeddccbbaa99887766554433221100"],
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "secret-body" } },
            AbortToken
        );

        // then
        var sends = _TakeStoppedSends(stopped);
        sends.Length.Should().Be(2);

        foreach (var send in sends)
        {
            var values = send
                .Tags.Select(t => t.Value?.ToString())
                .Concat(send.TagObjects.Select(t => t.Value?.ToString()));

            foreach (var value in values)
            {
                value.Should().NotContain("secret-body");
                value.Should().NotContain(_DeviceToken[..8]);
            }

            send.Status.Should().Be(ActivityStatusCode.Ok);
        }
    }

    [Fact]
    public async Task should_mark_the_activity_error_and_tag_the_reason_when_apns_rejects()
    {
        // given
        _server.Responder = _ => new FakeApnsReply(400, "BadDeviceToken");
        await using var provider = _server.CreateProvider(o => o.Environment = ApnsEnvironment.Sandbox);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        using var listener = _StartSandboxActivityListener(out var stopped);

        // when
        await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then
        var send = _TakeStoppedSends(stopped).Should().ContainSingle().Subject;
        send.Status.Should().Be(ActivityStatusCode.Error);
        send.Tags.Should().Contain(t => t.Key == ApnsTags.Reason && (string?)t.Value == "BadDeviceToken");
        send.Tags.Should().Contain(t => t.Key == ApnsTags.FailureKind && (string?)t.Value == "device_token_invalid");
    }

    [Fact]
    public async Task should_name_the_meter_and_every_instrument_after_the_package()
    {
        // given
        await using var provider = _server.CreateProvider(o => o.Environment = ApnsEnvironment.Sandbox);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        using var capture = new ApnsMeterCapture();
        using var listener = capture.Start();

        // when
        await service.SendAsync(
            _DeviceToken,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then - consumers subscribe by this name, so it is the contract, and every captured measurement must come
        // from a meter carrying it.
        ApnsDiagnostics.SourceName.Should().Be("Headless.PushNotifications.Apns");
        capture.MeterNames().Should().NotBeEmpty().And.AllBe("Headless.PushNotifications.Apns");
    }

    private static ActivityListener _StartSandboxActivityListener(out ConcurrentBag<Activity> stopped)
    {
        stopped = [];
        var listener = new ActivityListener
        {
            ActivityStopped = stopped.Add,
            ShouldListenTo = source => source.Name == ApnsDiagnostics.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    /// <summary>The finished <c>apns.send</c> activities of a sandbox instance, which only this class sends with.</summary>
    private static Activity[] _TakeStoppedSends(ConcurrentBag<Activity> stopped)
    {
        return stopped
            .Where(a =>
                a.OperationName == "apns.send"
                && a.Tags.Any(t => t.Key == ApnsTags.Environment && (string?)t.Value == "sandbox")
            )
            .ToArray();
    }

    private static bool _HasTag(ApnsMeterCapture.Measurement measurement, string key, string? value)
    {
        var tag = measurement.Tags.SingleOrDefault(t => t.Key == key);

        return tag.Key is not null && (string?)tag.Value == value;
    }

    private static bool _LacksTag(ApnsMeterCapture.Measurement measurement, string key)
    {
        return measurement.Tags.All(t => t.Key != key);
    }

    /// <summary>
    /// Captures the APNs meter's long and double measurements with their instrument and meter names. Only
    /// instruments of <see cref="ApnsDiagnostics.SourceName"/> are enabled, so foreign meters in the process are
    /// never captured or asserted on.
    /// </summary>
    private sealed class ApnsMeterCapture : IDisposable
    {
        private readonly ConcurrentBag<Measurement> _measurements = [];
        private MeterListener? _listener;

        internal sealed record Measurement(
            string MeterName,
            string InstrumentName,
            KeyValuePair<string, object?>[] Tags
        );

        public IReadOnlyList<Measurement> this[string instrumentName] =>
            [.. _measurements.Where(m => m.InstrumentName == instrumentName)];

        public Measurement[] Measurements(string instrumentName) =>
            [.. _measurements.Where(m => m.InstrumentName == instrumentName)];

        public Measurement[] Sends() => Measurements("headless.apns.sends");

        public string[] MeterNames() => [.. _measurements.Select(m => m.MeterName).Distinct()];

        public MeterListener Start()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (string.Equals(instrument.Meter.Name, ApnsDiagnostics.SourceName, StringComparison.Ordinal))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>(
                (instrument, _, tags, _) =>
                    _measurements.Add(new Measurement(instrument.Meter.Name, instrument.Name, tags.ToArray()))
            );
            _listener.SetMeasurementEventCallback<double>(
                (instrument, _, tags, _) =>
                    _measurements.Add(new Measurement(instrument.Meter.Name, instrument.Name, tags.ToArray()))
            );
            _listener.Start();

            return _listener;
        }

        public void Dispose()
        {
            _listener?.Dispose();
        }
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Fakes;

/// <summary>
/// Builds the Firebase sender and service over the real FirebaseAdmin SDK and a <see cref="FakeFcmHttpHandler"/>. Every
/// rig uses its own instance name, the <see cref="FcmTags.Instance"/> tag, so a test can pick its own measurements out
/// of the process-wide meter.
/// </summary>
internal sealed class FcmTestRig : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    public FcmTestRig()
    {
        Http = new FakeFcmHttpHandler(Time);
    }

    public string InstanceName { get; } = "test-" + Guid.NewGuid().ToString("N");

    public RecordingTimeProvider Time { get; } = new();

    public FakeFcmHttpHandler Http { get; }

    public FcmMessageSender CreateSender(Action<FirebaseOptions>? configure = null)
    {
        var options = new FirebaseOptions { Json = FakeServiceAccount.Json };
        configure?.Invoke(options);
        var monitor = Substitute.For<IOptionsMonitor<FirebaseOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(options);

        var sender = new FcmMessageSender(
            monitor,
            InstanceName,
            Time,
            NullLogger<FcmMessageSender>.Instance,
            new FakeHttpClientFactory(Http)
        );

        _owned.Add(sender);

        return sender;
    }

    public FcmPushNotificationService CreateService(Action<FirebaseOptions>? configure = null)
    {
        return new FcmPushNotificationService(CreateSender(configure), Time);
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }
    }
}

/// <summary>One measurement of a Firebase instrument.</summary>
internal sealed record FcmMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)
{
    public object? Tag(string name) => Tags.GetValueOrDefault(name);
}

/// <summary>
/// Records the Firebase instruments' measurements tagged with one instance name. The meter is process-wide, so the
/// instance tag is what keeps parallel tests from seeing each other's values.
/// </summary>
internal sealed class FcmMetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<FcmMeasurement> _measurements = new();
    private readonly string _instanceName;

    public FcmMetricRecorder(string instanceName)
    {
        _instanceName = instanceName;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, FcmDiagnostics.SourceName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => _Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => _Add(instrument, value, tags));
        _listener.Start();
    }

    public IReadOnlyList<FcmMeasurement> Of(string instrument)
    {
        return [.. _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal))];
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void _Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        if (!string.Equals(copy.GetValueOrDefault(FcmTags.Instance) as string, _instanceName, StringComparison.Ordinal))
        {
            return;
        }

        _measurements.Enqueue(new FcmMeasurement(instrument.Name, value, copy));
    }
}

/// <summary>
/// Records the Firebase activities started under one test's own root activity, so parallel tests never see each
/// other's spans.
/// </summary>
internal sealed class FcmActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly Activity _root;

    public FcmActivityRecorder()
    {
        _root = new Activity("fcm-test-root");
        _root.SetIdFormat(ActivityIdFormat.W3C);
        _root.Start();
        var traceId = _root.TraceId;

        _listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, FcmDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    _activities.Enqueue(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Activities => [.. _activities];

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }
}

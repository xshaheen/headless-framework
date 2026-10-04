// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Provides diagnostic sources, meters, and activities for the APNs push notification provider.
/// </summary>
[PublicAPI]
public static class ApnsDiagnostics
{
    /// <summary>Defines the diagnostic source name for the APNs provider.</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "PushNotifications.Apns";

    /// <summary>Shared <see cref="ActivitySource"/> for APNs send traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource(
        "PushNotifications.Apns"
    );

    /// <summary>Shared <see cref="Meter"/> for APNs send metrics.</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("PushNotifications.Apns");

    /// <summary>
    /// Starts a new <see cref="Activity"/> when listeners are attached.
    /// </summary>
    /// <param name="name">The activity operation name.</param>
    /// <param name="kind">The activity kind.</param>
    /// <returns>The started activity, or <see langword="null"/> when no listener is attached.</returns>
    internal static Activity? Start(string name, ActivityKind kind = ActivityKind.Client)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}

/// <summary>Defines metric and tracing tag names emitted by the APNs provider.</summary>
[PublicAPI]
public static class ApnsTags
{
    /// <summary>Send outcome: <c>succeeded</c>, <c>unregistered</c>, or <c>failed</c>.</summary>
    public const string Outcome = "headless.apns.outcome";

    /// <summary>
    /// Failure category on a failed send in lower snake case, or omitted when successful.
    /// </summary>
    public const string FailureKind = "headless.apns.failure_kind";

    /// <summary>The APNs reason code from the rejection response, or <c>none</c> when no response arrived.</summary>
    public const string Reason = "headless.apns.reason";

    /// <summary>The <c>apns-push-type</c> header value of the notification.</summary>
    public const string PushType = "headless.apns.push_type";

    /// <summary>The target APNs environment: <c>production</c> or <c>sandbox</c>.</summary>
    public const string Environment = "headless.apns.environment";
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Centralises the <see cref="ActivitySource"/> and <see cref="Meter"/> the APNs provider emits through. Consumers
/// subscribe via <see cref="SourceName"/> (<c>AddSource</c>/<c>AddMeter</c>); the instruments live in
/// <c>ApnsMetrics</c> and the span attribute names in <see cref="ApnsTags"/>.
/// </summary>
/// <remarks>
/// Follows the repository's native-emission OpenTelemetry convention: BCL primitives only, no OpenTelemetry package
/// dependency, and bespoke <c>headless.apns.*</c> instrument and attribute names because no messaging-style
/// semantic convention covers push delivery. The device token is never a tag: it is a stable device identifier.
/// </remarks>
[PublicAPI]
public static class ApnsDiagnostics
{
    /// <summary>The full activity-source and meter name used by the APNs provider (<c>Headless.PushNotifications.Apns</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "PushNotifications.Apns";

    /// <summary>Shared <see cref="ActivitySource"/> for APNs send traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource(
        "PushNotifications.Apns"
    );

    /// <summary>Shared <see cref="Meter"/> for APNs send metrics (see <c>ApnsMetrics</c> for the instruments).</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("PushNotifications.Apns");

    /// <summary>
    /// Starts a new <see cref="Activity"/> with the given <paramref name="name"/> if a listener is attached,
    /// otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="name">The activity operation name (for example <c>apns.send</c>).</param>
    /// <param name="kind">The activity kind; defaults to <see cref="ActivityKind.Client"/>.</param>
    /// <returns>The started activity, or <see langword="null"/> when no listener is subscribed.</returns>
    internal static Activity? Start(string name, ActivityKind kind = ActivityKind.Client)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}

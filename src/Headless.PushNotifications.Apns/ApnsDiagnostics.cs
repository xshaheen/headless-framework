// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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

/// <summary>Well-known APNs tag names emitted by the framework on metric instruments and activity spans.</summary>
[PublicAPI]
public static class ApnsTags
{
    /// <summary>Send outcome: <c>succeeded</c>, <c>unregistered</c>, or <c>failed</c>.</summary>
    public const string Outcome = "headless.apns.outcome";

    /// <summary>
    /// Failure category on a failed send; the <see cref="ApnsFailureKind"/> name in lower snake case, or
    /// <see langword="null"/>-omitted on success.
    /// </summary>
    public const string FailureKind = "headless.apns.failure_kind";

    /// <summary>The APNs <c>reason</c> the rejection carried, or <c>none</c> when there was no answer.</summary>
    /// <remarks>
    /// Bounded in practice: it comes from APNs' documented response error string table, and an unknown reason is a
    /// rare forward-compatibility case, not caller input.
    /// </remarks>
    public const string Reason = "headless.apns.reason";

    /// <summary>The <c>apns-push-type</c> of the sent notification, such as <c>alert</c> or <c>voip</c>.</summary>
    public const string PushType = "headless.apns.push_type";

    /// <summary>The environment the instance delivers to: <c>production</c> or <c>sandbox</c>.</summary>
    public const string Environment = "headless.apns.environment";
}

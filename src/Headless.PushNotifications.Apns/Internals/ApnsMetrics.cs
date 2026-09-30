// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Metric instruments for the APNs provider, registered against <see cref="ApnsDiagnostics.Meter"/>. Instrument and
/// attribute names are bespoke <c>headless.apns.*</c> per the repository's OpenTelemetry conventions, because no
/// semantic convention covers push delivery.
/// </summary>
/// <remarks>
/// Instruments are created directly on the <see cref="Meter"/> (rather than through a source generator) so the hot
/// path can read each instrument's <c>Enabled</c> flag and short-circuit before building a <see cref="TagList"/>
/// when no listener is attached. The device token is never a tag: it is a stable device identifier.
/// </remarks>
internal static class ApnsMetrics
{
    // --- Instrument names ----------------------------------------------------------------------------------------

    internal const string SendsName = "headless.apns.sends";
    internal const string SendDurationName = "headless.apns.send.duration";
    internal const string ProviderTokensMintedName = "headless.apns.provider_tokens.minted";
    internal const string CertificateReloadsName = "headless.apns.certificates.reloaded";
    internal const string BroadcastsName = "headless.apns.broadcasts";

    private static readonly Counter<long> _Sends = ApnsDiagnostics.Meter.CreateCounter<long>(
        SendsName,
        unit: "{send}",
        description: "APNs sends per device token, by outcome, failure kind, reason, push type, and environment."
    );

    private static readonly Histogram<double> _SendDuration = ApnsDiagnostics.Meter.CreateHistogram<double>(
        SendDurationName,
        unit: "ms",
        description: "Duration of one APNs send to one device token."
    );

    private static readonly Counter<long> _ProviderTokensMinted = ApnsDiagnostics.Meter.CreateCounter<long>(
        ProviderTokensMintedName,
        unit: "{token}",
        description: "APNs provider tokens minted. Apple rejects a key whose token changes more than once every 20 minutes."
    );

    private static readonly Counter<long> _CertificateReloads = ApnsDiagnostics.Meter.CreateCounter<long>(
        CertificateReloadsName,
        unit: "{reload}",
        description: "APNs provider certificate reloads, by outcome."
    );

    private static readonly Counter<long> _Broadcasts = ApnsDiagnostics.Meter.CreateCounter<long>(
        BroadcastsName,
        unit: "{broadcast}",
        description: "APNs channel broadcasts, by outcome, failure kind, reason, and environment."
    );

    /// <summary>Counts one finished broadcast with its outcome tags.</summary>
    internal static void RecordBroadcast(ApnsBroadcastResult result, ApnsEnvironment environment)
    {
        if (!_Broadcasts.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { ApnsTags.Outcome, result.IsSucceeded ? "succeeded" : "failed" },
            { ApnsTags.Environment, environment == ApnsEnvironment.Sandbox ? "sandbox" : "production" },
        };

        if (!result.IsSucceeded)
        {
            tags.Add(ApnsTags.FailureKind, result.FailureKind is { } kind ? ToTagValue(kind) : null);
            tags.Add(ApnsTags.Reason, result.Reason ?? "none");
        }

        _Broadcasts.Add(1, tags);
    }

    /// <summary>Whether any APNs instrument currently has a subscribed listener.</summary>
    internal static bool AnyEnabled =>
        _Sends.Enabled || _SendDuration.Enabled || _ProviderTokensMinted.Enabled || _CertificateReloads.Enabled;

    /// <summary>Counts one finished send with its outcome tags.</summary>
    internal static void RecordSend(ApnsSendResult result, string pushType, ApnsEnvironment environment)
    {
        if (!_Sends.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { ApnsTags.Outcome, _OutcomeTag(result) },
            { ApnsTags.PushType, pushType },
            { ApnsTags.Environment, environment == ApnsEnvironment.Sandbox ? "sandbox" : "production" },
        };

        if (result.FailureKind is { } kind)
        {
            tags.Add(ApnsTags.FailureKind, ToTagValue(kind));
            tags.Add(ApnsTags.Reason, result.Reason ?? "none");
        }

        _Sends.Add(1, tags);
    }

    /// <summary>Records how long one send took.</summary>
    internal static void RecordSendDuration(TimeSpan duration, string pushType, ApnsEnvironment environment)
    {
        if (!_SendDuration.Enabled)
        {
            return;
        }

        _SendDuration.Record(
            duration.TotalMilliseconds,
            new TagList
            {
                { ApnsTags.PushType, pushType },
                { ApnsTags.Environment, environment == ApnsEnvironment.Sandbox ? "sandbox" : "production" },
            }
        );
    }

    /// <summary>Counts one minted provider token.</summary>
    internal static void RecordProviderTokenMinted()
    {
        if (!_ProviderTokensMinted.Enabled)
        {
            return;
        }

        _ProviderTokensMinted.Add(1);
    }

    /// <summary>Counts one certificate reload, by whether the renewed certificate was accepted.</summary>
    internal static void RecordCertificateReload(bool accepted)
    {
        if (!_CertificateReloads.Enabled)
        {
            return;
        }

        _CertificateReloads.Add(1, new TagList { { ApnsTags.Outcome, accepted ? "accepted" : "rejected" } });
    }

    private static string _OutcomeTag(ApnsSendResult result)
    {
        return result.Response.Status switch
        {
            PushNotificationResponseStatus.Success => "succeeded",
            PushNotificationResponseStatus.Unregistered => "unregistered",
            _ => "failed",
        };
    }

    /// <summary>
    /// The <see cref="ApnsFailureKind"/> tag value shared by metrics and spans, in lower snake case to match the
    /// repository's telemetry tag style.
    /// </summary>
    internal static string ToTagValue(ApnsFailureKind kind)
    {
        return kind switch
        {
            ApnsFailureKind.DeviceTokenInvalid => "device_token_invalid",
            ApnsFailureKind.Throttled => "throttled",
            ApnsFailureKind.ServerError => "server_error",
            ApnsFailureKind.Authentication => "authentication",
            ApnsFailureKind.Configuration => "configuration",
            ApnsFailureKind.Payload => "payload",
            ApnsFailureKind.Transport => "transport",
            _ => "unknown",
        };
    }
}

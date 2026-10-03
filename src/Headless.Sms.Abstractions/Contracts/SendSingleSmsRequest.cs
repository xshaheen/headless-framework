// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Describes a single SMS message to one recipient, sent through <see cref="ISmsSender"/>.</summary>
/// <remarks>
/// Each request targets exactly one recipient (<see cref="Destination"/>). To send the same text to many
/// recipients in one provider call, use a provider that implements <see cref="IBulkSmsSender"/> and its
/// <see cref="SendBulkSmsRequest"/> instead.
/// </remarks>
[PublicAPI]
public sealed class SendSingleSmsRequest
{
    /// <summary>
    /// Caller-supplied correlation id for the message. When provided, providers that accept a client
    /// message id (for example VictoryLink's <c>SMSID</c> or Infobip's per-message id) will forward it to
    /// the upstream API; others ignore it. May be <see langword="null"/>.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// The single recipient: a dial-code (<see cref="SmsRequestDestination.Code"/>) and a local subscriber
    /// number (<see cref="SmsRequestDestination.Number"/>).
    /// </summary>
    public required SmsRequestDestination Destination { get; init; }

    /// <summary>The plain-text body of the SMS message.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Optional provider-specific or application-specific metadata. Providers may read well-known keys
    /// from this dictionary; unrecognized keys are ignored.
    /// </summary>
    public IDictionary<string, object>? Properties { get; init; }
}

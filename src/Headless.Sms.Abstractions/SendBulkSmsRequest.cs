// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Represents a single SMS message sent to multiple recipients in one provider call.</summary>
/// <remarks>
/// Used with <see cref="IBulkSmsSender"/>. The provider sends <see cref="Text"/> to each entry in
/// <see cref="Destinations"/>. The operation returns a <see cref="SendBulkSmsResponse"/> containing one result per
/// recipient. Providers that cannot report per-recipient outcomes apply the aggregate result to every recipient.
/// </remarks>
[PublicAPI]
public sealed class SendBulkSmsRequest
{
    /// <summary>
    /// Gets the caller-supplied correlation identifier for the batch. Providers that accept a client message
    /// identifier forward it (deriving per-recipient identifiers where the API requires them); others ignore
    /// it. May be <see langword="null"/>.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>Gets the recipients for the message. Must contain at least one destination.</summary>
    public required IReadOnlyList<SmsRequestDestination> Destinations { get; init; }

    /// <summary>Gets the plain-text body of the SMS message.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets optional provider-specific or application-specific metadata. Providers can read well-known keys from this
    /// dictionary. Providers ignore unrecognized keys.
    /// </summary>
    public IDictionary<string, object>? Properties { get; init; }
}

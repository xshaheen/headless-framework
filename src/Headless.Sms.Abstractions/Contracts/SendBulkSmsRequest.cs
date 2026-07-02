// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>Describes a single SMS message delivered to many recipients in one provider call.</summary>
/// <remarks>
/// Used with <see cref="IBulkSmsSender"/>. The same <see cref="Text"/> is sent to every entry in
/// <see cref="Destinations"/>. The outcome is a <see cref="SendBulkSmsResponse"/> with one result per
/// recipient; providers whose API cannot report per-recipient outcomes apply the same aggregate result to
/// every recipient.
/// </remarks>
[PublicAPI]
public sealed class SendBulkSmsRequest
{
    /// <summary>
    /// Caller-supplied correlation id for the batch. Providers that accept a client message id forward it
    /// (deriving per-recipient ids where required); others ignore it. May be <see langword="null"/>.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>The recipients. Must contain at least one destination.</summary>
    public required IReadOnlyList<SmsRequestDestination> Destinations { get; init; }

    /// <summary>The plain-text body of the SMS message.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Optional provider-specific or application-specific metadata. Providers may read well-known keys from
    /// this dictionary; unrecognized keys are ignored.
    /// </summary>
    public IDictionary<string, object>? Properties { get; init; }
}

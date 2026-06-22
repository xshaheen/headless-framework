// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>Describes a single SMS message to be sent through <see cref="ISmsSender"/>.</summary>
/// <remarks>
/// A request targets one or more recipients via <see cref="Destinations"/>. When more than one destination
/// is specified the request is treated as a batch send (<see cref="IsBatch"/> is <see langword="true"/>);
/// providers that do not support batch sends natively will reject such requests with a failed response.
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
    /// One or more recipients. Each entry contains a dial-code (<see cref="SmsRequestDestination.Code"/>) and
    /// a local subscriber number (<see cref="SmsRequestDestination.Number"/>).
    /// </summary>
    public required IReadOnlyList<SmsRequestDestination> Destinations { get; init; }

    /// <summary>The plain-text body of the SMS message.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Optional provider-specific or application-specific metadata. Providers may read well-known keys
    /// from this dictionary; unrecognized keys are ignored.
    /// </summary>
    public IDictionary<string, object>? Properties { get; init; }

    /// <summary>
    /// <see langword="true"/> when the request targets more than one recipient; <see langword="false"/> for
    /// single-recipient sends. Providers that do not support batch sends natively will return a failed
    /// response for batch requests.
    /// </summary>
    public bool IsBatch => Destinations.Count > 1;
}

/// <summary>A single SMS recipient identified by a dial code and a subscriber number.</summary>
/// <param name="Code">The international dial code without the leading <c>+</c> (for example <c>20</c> for Egypt, <c>1</c> for the US).</param>
/// <param name="Number">The local subscriber number without the country code or leading zeros.</param>
[PublicAPI]
public sealed record SmsRequestDestination(int Code, string Number)
{
    /// <summary>Returns the E.164-style number without a leading <c>+</c> (for example <c>201234567890</c>).</summary>
    public override string ToString() => ToString(hasPlusPrefix: false);

    /// <summary>Returns the number formatted as <c>{Code}{Number}</c> or <c>+{Code}{Number}</c>.</summary>
    /// <param name="hasPlusPrefix">When <see langword="true"/>, prepends a <c>+</c> sign to produce an E.164 number.</param>
    public string ToString(bool hasPlusPrefix)
    {
        var format = hasPlusPrefix ? $"+{Code}{Number}" : (FormattableString)$"{Code}{Number}";

        return format.ToString(CultureInfo.InvariantCulture);
    }
}

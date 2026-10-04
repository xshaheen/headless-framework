// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Represents a single SMS message sent to one recipient through <see cref="ISmsSender"/>.</summary>
/// <remarks>
/// Each request targets one recipient in <see cref="Destination"/>. To send the same text to multiple recipients in
/// one provider call, use <see cref="IBulkSmsSender"/> with <see cref="SendBulkSmsRequest"/>.
/// </remarks>
[PublicAPI]
public sealed class SendSingleSmsRequest
{
    /// <summary>
    /// Gets the caller-supplied correlation identifier for the message. When provided, providers that accept
    /// a client message identifier (for example VictoryLink's <c>SMSID</c> or Infobip's per-message id)
    /// forward it to the upstream API; others ignore it. May be <see langword="null"/>.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Gets the single recipient: a dial code (<see cref="SmsRequestDestination.Code"/>) and a local
    /// subscriber number (<see cref="SmsRequestDestination.Number"/>).
    /// </summary>
    public required SmsRequestDestination Destination { get; init; }

    /// <summary>Gets the plain-text body of the SMS message.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets optional provider-specific or application-specific metadata. Providers can read well-known keys from this
    /// dictionary. Providers ignore unrecognized keys.
    /// </summary>
    public IDictionary<string, object>? Properties { get; init; }
}

/// <summary>Represents a single SMS recipient identified by a dial code and a subscriber number.</summary>
/// <param name="Code">The international dial code without a leading plus sign, such as 20 or 1.</param>
/// <param name="Number">The local subscriber number without country code or leading zeros.</param>
[PublicAPI]
public sealed record SmsRequestDestination(int Code, string Number)
{
    /// <summary>Returns the E.164-style number without a leading plus sign, for example <c>201234567890</c>.</summary>
    /// <returns>The formatted phone number string.</returns>
    public override string ToString()
    {
        return ToString(hasPlusPrefix: false);
    }

    /// <summary>Returns the number formatted as <c>{Code}{Number}</c> or <c>+{Code}{Number}</c>.</summary>
    /// <param name="hasPlusPrefix">
    /// When <see langword="true"/>, prepends a plus sign to produce an E.164 number.
    /// </param>
    /// <returns>The formatted phone number string.</returns>
    public string ToString(bool hasPlusPrefix)
    {
        var format = hasPlusPrefix ? $"+{Code}{Number}" : (FormattableString)$"{Code}{Number}";

        return format.ToString(CultureInfo.InvariantCulture);
    }
}

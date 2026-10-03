// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>A single SMS recipient identified by a dial code and a subscriber number.</summary>
/// <param name="Code">The international dial code without the leading <c>+</c> (for example <c>20</c> for Egypt, <c>1</c> for the US).</param>
/// <param name="Number">The local subscriber number without the country code or leading zeros.</param>
[PublicAPI]
public sealed record SmsRequestDestination(int Code, string Number)
{
    /// <summary>Returns the E.164-style number without a leading <c>+</c> (for example <c>201234567890</c>).</summary>
    public override string ToString()
    {
        return ToString(hasPlusPrefix: false);
    }

    /// <summary>Returns the number formatted as <c>{Code}{Number}</c> or <c>+{Code}{Number}</c>.</summary>
    /// <param name="hasPlusPrefix">When <see langword="true"/>, prepends a <c>+</c> sign to produce an E.164 number.</param>
    public string ToString(bool hasPlusPrefix)
    {
        var format = hasPlusPrefix ? $"+{Code}{Number}" : (FormattableString)$"{Code}{Number}";

        return format.ToString(CultureInfo.InvariantCulture);
    }
}

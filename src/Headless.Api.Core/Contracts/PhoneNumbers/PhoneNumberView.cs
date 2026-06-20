// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API response view for a phone number. Maps the domain <see cref="PhoneNumber"/> primitive to a
/// serializable record exposing the ITU country calling code and local subscriber number separately.
/// </summary>
/// <param name="Code">The ITU-T country calling code (e.g., <c>1</c> for US, <c>44</c> for UK).</param>
/// <param name="Number">The local subscriber number, excluding the country code.</param>
public sealed record PhoneNumberView(int Code, string Number)
{
    /// <summary>
    /// Maps a domain <see cref="PhoneNumber"/> to a <see cref="PhoneNumberView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static PhoneNumberView? FromPhoneNumber(PhoneNumber? operand) => operand;

    /// <summary>
    /// Implicitly converts a domain <see cref="PhoneNumber"/> to a <see cref="PhoneNumberView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumberView?(PhoneNumber? operand)
    {
        return operand is null ? null : new(operand.CountryCode, operand.Number);
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Text;

/// <summary>
/// Normalizes user names, emails, and phone numbers into a canonical form suitable for case-insensitive lookups
/// and uniqueness checks (for example as normalized index keys).
/// </summary>
[PublicAPI]
public static class LookupNormalizer
{
    /// <summary>
    /// Normalizes a user name by trimming whitespace, applying Unicode normalization, and uppercasing it using the
    /// invariant culture.
    /// </summary>
    /// <param name="name">The user name to normalize.</param>
    /// <returns>The normalized user name, or <see langword="null"/> when <paramref name="name"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(name))]
    public static string? NormalizeUserName(string? name)
    {
        return name?.NullableTrim()?.Normalize().ToUpperInvariant();
    }

    /// <summary>
    /// Normalizes an email address using the same rules as <see cref="NormalizeUserName"/> (trim, Unicode normalize,
    /// uppercase invariant).
    /// </summary>
    /// <param name="email">The email address to normalize.</param>
    /// <returns>The normalized email, or <see langword="null"/> when <paramref name="email"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(email))]
    public static string? NormalizeEmail(string? email)
    {
        return NormalizeUserName(email);
    }

    /// <summary>
    /// Normalizes a phone number by trimming whitespace, removing spaces, converting digits to their invariant
    /// (ASCII) form, and stripping a single trailing <c>0</c>.
    /// </summary>
    /// <param name="number">The phone number to normalize.</param>
    /// <returns>The normalized phone number, or <see langword="null"/> when <paramref name="number"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(number))]
    public static string? NormalizePhoneNumber(string? number)
    {
        return number
            ?.NullableTrim()
            ?.RemoveCharacter(' ')
            .ToInvariantDigits()
            .RemovePostfix(StringComparison.Ordinal, "0");
    }
}

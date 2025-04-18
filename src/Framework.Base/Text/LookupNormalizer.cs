// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Text;

public static class LookupNormalizer
{
    [return: NotNullIfNotNull(nameof(name))]
    public static string? NormalizeUserName(string? name)
    {
        return name?.NullableTrim()?.Normalize().ToUpperInvariant();
    }

    [return: NotNullIfNotNull(nameof(email))]
    public static string? NormalizeEmail(string? email)
    {
        return NormalizeUserName(email);
    }

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

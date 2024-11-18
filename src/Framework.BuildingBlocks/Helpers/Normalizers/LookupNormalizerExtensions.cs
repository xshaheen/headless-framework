// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.BuildingBlocks.Helpers.Normalizers;

public static class LookupNormalizerExtensions
{
    [return: NotNullIfNotNull(nameof(name))]
    public static string? NormalizeName(this string? name)
    {
        return name?.NullableTrim()?.Normalize().ToUpperInvariant();
    }

    [return: NotNullIfNotNull(nameof(email))]
    public static string? NormalizeEmail(this string? email)
    {
        return NormalizeName(email);
    }

    [return: NotNullIfNotNull(nameof(number))]
    public static string? NormalizePhoneNumber(this string? number)
    {
        return number
            ?.NullableTrim()
            ?.Replace(' ', '\0')
            .ToInvariantDigits()
            .RemovePostfix(StringComparison.Ordinal, "0");
    }
}

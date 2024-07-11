using System.Diagnostics.CodeAnalysis;

namespace Framework.BuildingBlocks.Extensions.Normalizers;

public static class LookupNormalizerExtensions
{
    [return: NotNullIfNotNull(nameof(name))]
    public static string? NormalizeName(this string? name) => name?.NullableTrim()?.Normalize().ToUpperInvariant();

    [return: NotNullIfNotNull(nameof(email))]
    public static string? NormalizeEmail(this string? email) => NormalizeName(email);

    [return: NotNullIfNotNull(nameof(number))]
    public static string? NormalizePhoneNumber(this string? number) =>
        number?.NullableTrim()?.Replace(' ', '\0').ToInvariantDigits().RemovePostfix(StringComparison.Ordinal, "0");
}

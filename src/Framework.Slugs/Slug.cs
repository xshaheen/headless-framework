// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Slugs;

public static class Slug
{
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Create(string? text, SlugOptions? options = null)
    {
        if (text is null)
        {
            return null;
        }

        // Prevent DoS with huge inputs
        const int maxInputLength = 10_000;
        if (text.Length > maxInputLength)
        {
            throw new ArgumentException($"Input exceeds maximum length of {maxInputLength} characters", nameof(text));
        }

        options ??= SlugOptions.Default;
        text = text.Normalize(NormalizationForm.FormD);

        foreach (var (value, replacement) in options.Replacements)
        {
            text = text.Replace(value, replacement, StringComparison.Ordinal);
        }

        var textLength = options.MaximumLength > 0 ? Math.Min(text.Length, options.MaximumLength) : text.Length;
        var sb = new StringBuilder(textLength);
        var hasPreviousDash = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(rune.Value);

            if (options.IsAllowed(rune))
            {
                var transformed = options.CasingTransformation switch
                {
                    CasingTransformation.ToLowerCase => options.Culture is null
                        ? Rune.ToLowerInvariant(rune)
                        : Rune.ToLower(rune, options.Culture),
                    CasingTransformation.ToUpperCase => options.Culture is null
                        ? Rune.ToUpperInvariant(rune)
                        : Rune.ToUpper(rune, options.Culture),
                    _ => rune,
                };
                sb.Append(transformed);
                hasPreviousDash = false;
            }
            else if (
                unicodeCategory != UnicodeCategory.NonSpacingMark
                && options.Separator is not null
                && !_EndsWith(sb, options.Separator)
            )
            {
                if (!hasPreviousDash && sb.Length > 0)
                {
                    sb.Append(options.Separator);
                    hasPreviousDash = true;
                }
            }

        }

        text = sb.ToString();

        if (options.MaximumLength > 0 && text.Length >= options.MaximumLength)
        {
            text = text[..options.MaximumLength];
        }

        if (options is { CanEndWithSeparator: false, Separator: not null })
        {
            while (text.EndsWith(options.Separator, StringComparison.Ordinal))
            {
                text = text[..^options.Separator.Length];
            }
        }

        return text.Normalize(NormalizationForm.FormC);
    }

    private static bool _EndsWith(StringBuilder stringBuilder, string suffix)
    {
        if (stringBuilder.Length < suffix.Length)
        {
            return false;
        }

        for (var index = 0; index < suffix.Length; index++)
        {
            if (stringBuilder[stringBuilder.Length - 1 - index] != suffix[suffix.Length - 1 - index])
            {
                return false;
            }
        }

        return true;
    }
}

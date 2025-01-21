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

        options ??= SlugOptions.Default;
        text = text.Normalize(NormalizationForm.FormD);

        foreach (var (value, replacement) in options.Replacements)
        {
            var newValue = replacement.EndsWith(' ') ? replacement : replacement + " ";
            newValue = replacement.StartsWith(' ') ? newValue : " " + newValue;

            text = text.Replace(value, newValue, StringComparison.Ordinal);
        }

        var textLength = options.MaximumLength > 0 ? Math.Min(text.Length, options.MaximumLength) : text.Length;
        var sb = new StringBuilder(textLength);
        var hasPreviousDash = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(rune.Value);

            if (options.IsAllowed(rune))
            {
                sb.Append(options.Replace(rune));
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

            if (options.MaximumLength > 0 && sb.Length >= options.MaximumLength)
            {
                break;
            }
        }

        text = sb.ToString();

        if (options.MaximumLength > 0 && text.Length > options.MaximumLength)
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

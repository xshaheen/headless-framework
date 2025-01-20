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

        var textLength = options.MaximumLength > 0 ? Math.Min(text.Length, options.MaximumLength) : text.Length;
        var sb = new StringBuilder(textLength);

        foreach (var rune in text.EnumerateRunes())
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(rune.Value);

            if (options.IsAllowed(rune))
            {
                sb.Append(options.Replace(rune));
            }
            else if (
                unicodeCategory != UnicodeCategory.NonSpacingMark
                && options.Separator is not null
                && !_EndsWith(sb, options.Separator)
            )
            {
                sb.Append(options.Separator);
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

        if (
            !options.CanEndWithSeparator
            && options.Separator is not null
            && text.EndsWith(options.Separator, StringComparison.Ordinal)
        )
        {
            text = text[..^options.Separator.Length];
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

// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Slugs;

/// <summary>Entry point for URL-slug generation.</summary>
public static class Slug
{
    /// <summary>
    /// Converts <paramref name="text"/> to a URL-friendly slug using the supplied options.
    /// </summary>
    /// <param name="text">The source text to slugify. Returns <see langword="null"/> when <see langword="null"/>.</param>
    /// <param name="options">
    /// Slug generation options. When <see langword="null"/>, a default <see cref="SlugOptions"/> instance is used
    /// (lower-case, hyphen separator, 80-character limit, Latin + Arabic allowed ranges).
    /// </param>
    /// <returns>
    /// The slugified string in Unicode NFC form, or <see langword="null"/> when <paramref name="text"/> is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The method normalizes the input to NFD before processing so that diacritic marks can be stripped as
    /// non-spacing marks. Replacement pairs in <see cref="SlugOptions.Replacements"/> are applied first (for
    /// example <c>&amp;</c> becomes <c> and </c>). Characters outside
    /// <see cref="SlugOptions.AllowedRanges"/> are collapsed to a single separator. The result is truncated
    /// to <see cref="SlugOptions.MaximumLength"/> characters, and trailing separators are removed unless
    /// <see cref="SlugOptions.CanEndWithSeparator"/> is <see langword="true"/>.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Create(string? text, SlugOptions? options = null)
    {
        if (text is null)
        {
            return null;
        }

        options ??= new();

        // Normalizing to NFD lets diacritic marks be stripped as non-spacing marks below. Skip the allocation
        // when the input is already NFD (the common ASCII case) — IsNormalized is much cheaper than Normalize.
        if (!text.IsNormalized(NormalizationForm.FormD))
        {
            text = text.Normalize(NormalizationForm.FormD);
        }

        foreach (var (value, replacement) in options.Replacements)
        {
            // string.Replace allocates a new string even when the token is absent; most inputs contain none
            // of the replacement tokens, so guard the (cheaper) Contains scan first.
            if (text.Contains(value, StringComparison.Ordinal))
            {
                text = text.Replace(value, replacement, StringComparison.Ordinal);
            }
        }

        var textLength = options.MaximumLength > 0 ? Math.Min(text.Length, options.MaximumLength) : text.Length;
        var sb = new StringBuilder(textLength);
        var hasPreviousDash = false;

        foreach (var rune in text.EnumerateRunes())
        {
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
            // GetUnicodeCategory is only needed for disallowed runes, so compute it lazily here (not per rune).
            else if (
                CharUnicodeInfo.GetUnicodeCategory(rune.Value) != UnicodeCategory.NonSpacingMark
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

        // Skip the final NFC allocation when the result is already NFC (the common case once disallowed
        // characters have been collapsed); IsNormalized short-circuits to the identical result.
        return text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
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

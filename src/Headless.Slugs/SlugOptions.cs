// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Unicode;

namespace Headless.Slugs;

/// <summary>Controls how <see cref="Slug.Create"/> generates a URL slug from source text.</summary>
public sealed class SlugOptions
{
    /// <summary>The default maximum slug length (80 characters).</summary>
    public const int DefaultMaximumLength = 80;

    /// <summary>The default word separator (<c>-</c>).</summary>
    public const string DefaultSeparator = "-";

    /// <summary>
    /// Maximum number of characters in the produced slug. Characters beyond this limit are dropped.
    /// A value of <c>0</c> or less disables truncation. Default is <see cref="DefaultMaximumLength"/>.
    /// </summary>
    public int MaximumLength { get; init; } = DefaultMaximumLength;

    /// <summary>
    /// Determines whether alphabetic characters are case-folded. Default is
    /// <see cref="CasingTransformation.ToLowerCase"/>.
    /// </summary>
    public CasingTransformation CasingTransformation { get; init; } = CasingTransformation.ToLowerCase;

    /// <summary>
    /// The separator between words in the slug. Default is "-".
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when value is null or empty.</exception>
    public string Separator
    {
        get;
        init =>
            field = string.IsNullOrEmpty(value)
                ? throw new ArgumentException("Separator cannot be null or empty", nameof(value))
                : value;
    } = DefaultSeparator;

    /// <summary>
    /// The culture used when <see cref="CasingTransformation"/> is <see cref="CasingTransformation.ToLowerCase"/>
    /// or <see cref="CasingTransformation.ToUpperCase"/>. When <see langword="null"/> (the default), invariant
    /// casing is applied.
    /// </summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the slug may end with the <see cref="Separator"/>. When
    /// <see langword="false"/> (the default), trailing separators are stripped from the result.
    /// </summary>
    public bool CanEndWithSeparator { get; init; }

    /// <summary>
    /// Unicode ranges allowed in slugs.
    /// </summary>
    /// <remarks>
    /// Default ranges:
    /// <list type="bullet">
    /// <item>A-Z: Latin uppercase letters</item>
    /// <item>a-z: Latin lowercase letters</item>
    /// <item>0-9: Digits</item>
    /// <item>U+0620-U+064A: Arabic letters</item>
    /// </list>
    /// </remarks>
    public IReadOnlyList<UnicodeRange> AllowedRanges { get; init; } =
    [
        UnicodeRange.Create('A', 'Z'),
        UnicodeRange.Create('a', 'z'),
        UnicodeRange.Create('0', '9'),
        UnicodeRange.Create('ؠ', 'ي'),
    ];

    /// <summary>
    /// Verbatim string substitutions applied before slug processing. Each key is replaced with its
    /// corresponding value using ordinal comparison. Defaults to a small set of common symbol expansions:
    /// <c>&amp;</c> to <c> and </c>, <c>+</c> to <c> plus </c>, <c>.</c> to <c> dot </c>, and
    /// <c>%</c> to <c> percent </c>.
    /// </summary>
    public FrozenDictionary<string, string> Replacements { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "&", " and " },
            { "+", " plus " },
            { ".", " dot " },
            { "%", " percent " },
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="character"/> falls within at least one of the
    /// <see cref="AllowedRanges"/>. When <see cref="AllowedRanges"/> is empty, all characters are allowed.
    /// </summary>
    /// <param name="character">The Unicode scalar value to test.</param>
    /// <returns><see langword="true"/> if the character is allowed in the slug; otherwise <see langword="false"/>.</returns>
    public bool IsAllowed(Rune character)
    {
        if (AllowedRanges.Count == 0)
        {
            return true;
        }

        foreach (var t in AllowedRanges)
        {
            if (_IsInRange(t, character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool _IsInRange(UnicodeRange range, Rune rune)
    {
        return rune.Value >= range.FirstCodePoint && rune.Value < range.FirstCodePoint + range.Length;
    }
}

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
    /// Gets the maximum number of characters in the produced slug. Characters beyond this limit are dropped.
    /// A value of 0 or less disables truncation. The default is <see cref="DefaultMaximumLength"/>.
    /// </summary>
    public int MaximumLength { get; init; } = DefaultMaximumLength;

    /// <summary>
    /// Gets a value indicating whether alphabetic characters are case-folded. The default is
    /// <see cref="CasingTransformation.ToLowerCase"/>.
    /// </summary>
    public CasingTransformation CasingTransformation { get; init; } = CasingTransformation.ToLowerCase;

    /// <summary>
    /// Gets the separator between words in the slug. The default is <c>"-"</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/> or empty.</exception>
    public string Separator
    {
        get;
        init =>
            field = string.IsNullOrEmpty(value)
                ? throw new ArgumentException("Separator cannot be null or empty", nameof(value))
                : value;
    } = DefaultSeparator;

    /// <summary>
    /// Gets the culture used when <see cref="CasingTransformation"/> is <see cref="CasingTransformation.ToLowerCase"/>
    /// or <see cref="CasingTransformation.ToUpperCase"/>. When <see langword="null"/>, invariant
    /// casing is applied.
    /// </summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets a value indicating whether the slug may end with <see cref="Separator"/>. When
    /// <see langword="false"/>, trailing separators are stripped from the result.
    /// </summary>
    public bool CanEndWithSeparator { get; init; }

    /// <summary>
    /// Gets the Unicode ranges allowed in slugs.
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

    private static readonly FrozenDictionary<string, string> _DefaultReplacements = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        { "&", " and " },
        { "+", " plus " },
        { ".", " dot " },
        { "%", " percent " },
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Gets verbatim string substitutions applied before slug processing. Each key is replaced with its
    /// corresponding value using ordinal comparison. Accepts any <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
    /// The assigned dictionary is frozen internally using ordinal key comparison for lookup performance, and
    /// each key is normalized to Unicode NFD so that a composed key such as <c>ä</c> matches whether the input
    /// arrives composed or decomposed.
    /// Defaults to common symbol expansions:
    /// <c>&amp;</c> to <c> and </c>, <c>+</c> to <c> plus </c>, <c>.</c> to <c> dot </c>, and
    /// <c>%</c> to <c> percent </c>.
    /// </summary>
    /// <exception cref="ArgumentException">Two keys are the same text once normalized to NFD.</exception>
    public IReadOnlyDictionary<string, string> Replacements
    {
        get;
        init => field = _NormalizeKeys(value);
    } = _DefaultReplacements;

    /// <summary>
    /// Determines whether <paramref name="character"/> falls within at least one of the
    /// <see cref="AllowedRanges"/>. When <see cref="AllowedRanges"/> is empty, all characters are allowed.
    /// </summary>
    /// <param name="character">The Unicode scalar value to test.</param>
    /// <returns><see langword="true"/> if the character is allowed in the slug; otherwise, <see langword="false"/>.</returns>
    public bool IsAllowed(Rune character)
    {
        if (AllowedRanges.Count == 0)
        {
            return true;
        }

        // Indexed rather than foreach: enumerating the IReadOnlyList interface allocates an enumerator, and this
        // runs once per character of every slug.
        var ranges = AllowedRanges;

        for (var i = 0; i < ranges.Count; i++)
        {
            if (_IsInRange(ranges[i], character))
            {
                return true;
            }
        }

        return false;
    }

    // Slug.Create decomposes the input to NFD before it applies replacements, so a key must be NFD too or a
    // composed key (U+00E4) never matches the decomposed text (a + U+0308).
    private static FrozenDictionary<string, string> _NormalizeKeys(IReadOnlyDictionary<string, string> replacements)
    {
        if (
            replacements is FrozenDictionary<string, string> frozen
            && frozen.Keys.All(key => key.IsNormalized(NormalizationForm.FormD))
        )
        {
            return frozen;
        }

        var normalized = new Dictionary<string, string>(replacements.Count, StringComparer.Ordinal);

        foreach (var (key, value) in replacements)
        {
            // Add, not the indexer: two keys that normalize to the same text would otherwise drop one silently.
            normalized.Add(key.Normalize(NormalizationForm.FormD), value);
        }

        return normalized.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static bool _IsInRange(UnicodeRange range, Rune rune)
    {
        return rune.Value >= range.FirstCodePoint && rune.Value < range.FirstCodePoint + range.Length;
    }
}

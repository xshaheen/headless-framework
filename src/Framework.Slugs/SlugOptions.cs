// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Unicode;

namespace Framework.Slugs;

public sealed class SlugOptions
{
    public const int DefaultMaximumLength = 80;
    public const string DefaultSeparator = "-";

    public int MaximumLength { get; init; } = DefaultMaximumLength;

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

    public CultureInfo? Culture { get; init; }

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

    public FrozenDictionary<string, string> Replacements { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "&", " and " },
            { "+", " plus " },
            { ".", " dot " },
            { "%", " percent " },
        }.ToFrozenDictionary(StringComparer.Ordinal);

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

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Unicode;

namespace Framework.Slugs;

public sealed class SlugOptions
{
    public const int DefaultMaximumLength = 80;
    public const string DefaultSeparator = "-";

    internal static SlugOptions Default { get; } = new();

    public int MaximumLength { get; set; } = DefaultMaximumLength;

    public CasingTransformation CasingTransformation { get; set; } = CasingTransformation.ToLowerCase;

    public string Separator { get; set; } = DefaultSeparator;

    public CultureInfo? Culture { get; set; }

    public bool CanEndWithSeparator { get; set; }

    public List<UnicodeRange> AllowedRanges { get; } =
        [
            UnicodeRange.Create('A', 'Z'),
            UnicodeRange.Create('a', 'z'),
            UnicodeRange.Create('0', '9'),
            UnicodeRange.Create('ؠ', 'ي'),
            UnicodeRange.Create('٠', '٩'),
        ];

    public bool IsAllowed(Rune character)
    {
        return AllowedRanges.Count == 0 || AllowedRanges.Exists(range => _IsInRange(range, character));
    }

    public string Replace(Rune rune)
    {
        rune = CasingTransformation switch
        {
            CasingTransformation.ToLowerCase => Culture is null
                ? Rune.ToLowerInvariant(rune)
                : Rune.ToLower(rune, Culture),
            CasingTransformation.ToUpperCase => Culture is null
                ? Rune.ToUpperInvariant(rune)
                : Rune.ToUpper(rune, Culture),
            _ => rune,
        };

        return rune.ToString();
    }

    private static bool _IsInRange(UnicodeRange range, Rune rune)
    {
        return rune.Value >= range.FirstCodePoint && rune.Value < range.FirstCodePoint + range.Length;
    }
}

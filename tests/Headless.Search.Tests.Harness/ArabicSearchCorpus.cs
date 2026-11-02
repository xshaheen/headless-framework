// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Search;

namespace Tests;

/// <summary>One golden row: a text and the search key a person derived for it from the rule table.</summary>
public sealed record ArabicSearchCorpusRow(int Line, string Input, string Expected)
{
    public override string ToString() => $"line {Line.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Inputs both search implementations run: the hand-checked golden corpus, every BMP character alone and inside a word,
/// and seeded random strings. The expected keys in the corpus come from the rule table, never from either
/// implementation, so the corpus pins both.
/// </summary>
public static class ArabicSearchCorpus
{
    /// <summary>The golden corpus of <see cref="ArabicSearchVersion.V1" />.</summary>
    public static IReadOnlyList<ArabicSearchCorpusRow> V1 { get; } = _Load("arabic-search-corpus.v1.tsv");

    /// <summary>
    /// Every BMP scalar value (no surrogate and no U+0000, which PostgreSQL text cannot hold), alone and between two
    /// letters of each script, so a deleted character is told apart from a separator.
    /// </summary>
    public static IReadOnlyList<string> EveryBmpCharacter()
    {
        var inputs = new List<string>(3 * char.MaxValue);

        for (var c = 1; c <= char.MaxValue; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var s = ((char)c).ToString();
            inputs.Add(s);
            inputs.Add("ب" + s + "ب");
            inputs.Add("a" + s + "a");
        }

        return inputs;
    }

    /// <summary>Characters outside the BMP: an emoji, Arabic mathematical letters (not folded in V1), and the last scalar.</summary>
    public static IReadOnlyList<string> SupplementaryCharacters()
    {
        int[] scalars = [0x1F600, 0x1EE00, 0x1EE01, 0x1EE21, 0x1EEFF, 0x10FFFF];

        return
        [
            .. scalars.SelectMany(static s => new[] { char.ConvertFromUtf32(s), "ب" + char.ConvertFromUtf32(s) + "ب" }),
        ];
    }

    /// <summary>
    /// Inputs longer than the C# normalizer's stack buffer: repeated words with marks, a run of the longest expansion,
    /// and seeded random strings of several hundred characters.
    /// </summary>
    public static IReadOnlyList<string> LongStrings()
    {
        string[] fixedInputs =
        [
            string.Join(' ', Enumerable.Repeat("أَحْمَد", 300)),
            new string('\uFDF2', 300),
            string.Concat(Enumerable.Repeat("عبد-الله\n", 120)),
        ];
        var random = Random(200, seed: 20261030);
        var longRandom = random.Chunk(40).Select(static chunk => string.Concat(chunk));

        return [.. fixedInputs, .. longRandom];
    }

    /// <summary>
    /// Seeded random strings mixing characters that carry a rule, separators, and arbitrary BMP characters, so
    /// interactions between rules (a deletion next to a separator, an expansion at a word edge) are exercised.
    /// </summary>
    public static IReadOnlyList<string> Random(int count, int seed)
    {
        var rules = ArabicSearchRuleSet.V1;
        char[] ruled =
        [
            .. rules.OutputAlphabet,
            .. rules.Mappings.Keys,
            .. rules.Expansions.Keys,
            .. rules.Deletions,
            ' ',
            '\n',
            '\t',
            '-',
            '\'',
            '.',
            '،',
        ];
        var random = new Random(seed);
        var inputs = new List<string>(count);
        var builder = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            builder.Clear();
            var length = random.Next(1, 24);

            for (var j = 0; j < length; j++)
            {
                if (random.Next(5) == 0)
                {
                    var c = random.Next(1, char.MaxValue + 1);
                    builder.Append(c is >= 0xD800 and <= 0xDFFF ? ' ' : (char)c);
                }
                else
                {
                    builder.Append(ruled[random.Next(ruled.Length)]);
                }
            }

            inputs.Add(builder.ToString());
        }

        return inputs;
    }

    private static IReadOnlyList<ArabicSearchCorpusRow> _Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", fileName);
        var rows = new List<ArabicSearchCorpusRow>();
        var lines = File.ReadAllLines(path, Encoding.UTF8);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            var input = tab < 0 ? line : line[..tab];
            var expected = tab < 0 ? string.Empty : line[(tab + 1)..];
            rows.Add(new ArabicSearchCorpusRow(i + 1, _Unescape(input), _Unescape(expected)));
        }

        return rows;
    }

    private static string _Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                builder.Append(value[i]);

                continue;
            }

            var next = value[++i];

            switch (next)
            {
                case 'n':
                    builder.Append('\n');

                    break;
                case 't':
                    builder.Append('\t');

                    break;
                case 'r':
                    builder.Append('\r');

                    break;
                case 'u':
                    builder.Append(
                        (char)int.Parse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    );
                    i += 4;

                    break;
                default:
                    builder.Append(next);

                    break;
            }
        }

        return builder.ToString();
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;

namespace Headless.Search;

/// <summary>
/// One closed, versioned table of Arabic search rules: what each character becomes in a search key. It is the single
/// source both the C# normalizer and every database function are built from, so the two never disagree.
/// </summary>
/// <remarks>
/// <para>
/// A search key is built in four steps: each <see cref="Expansions" /> character becomes its string; each
/// <see cref="Mappings" /> character becomes its target and each <see cref="Deletions" /> character is removed; every run
/// of characters outside <see cref="OutputAlphabet" /> becomes one space; leading and trailing spaces are trimmed.
/// </para>
/// <para>
/// The rules use no Unicode categories, normalization, or culture-dependent case mapping, so a database and .NET built
/// on different Unicode versions produce the same key. A released version never changes; new rules ship as a new
/// <see cref="ArabicSearchVersion" />.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ArabicSearchRuleSet
{
    private const char _SeparatorAction = '\0';
    private const char _DeleteAction = '\u0001';
    private const char _ExpandAction = '\u0002';

    private readonly char[] _actions;

    internal ArabicSearchRuleSet(
        ArabicSearchVersion version,
        IReadOnlyList<char> outputAlphabet,
        IReadOnlyDictionary<char, char> mappings,
        IReadOnlyDictionary<char, string> expansions,
        IReadOnlyList<char> deletions
    )
    {
        Version = version;
        OutputAlphabet = outputAlphabet;
        Mappings = mappings;
        Expansions = expansions;
        Deletions = deletions;

        // One action per UTF-16 code unit: the output character itself, or a marker. The markers are control
        // characters, which are never in the output alphabet, so they cannot collide with an output.
        _actions = new char[char.MaxValue + 1];

        foreach (var c in outputAlphabet)
        {
            _actions[c] = c;
        }

        foreach (var (source, target) in mappings)
        {
            _actions[source] = target;
        }

        foreach (var source in expansions.Keys)
        {
            _actions[source] = _ExpandAction;
        }

        foreach (var source in deletions)
        {
            _actions[source] = _DeleteAction;
        }
    }

    /// <summary>The first rule set.</summary>
    public static ArabicSearchRuleSet V1 { get; } = ArabicSearchRulesV1.Create();

    /// <summary>Returns the rule set of <paramref name="version" />.</summary>
    /// <param name="version">The rule version.</param>
    /// <returns>The rule set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version" /> is not a defined version.</exception>
    public static ArabicSearchRuleSet For(ArabicSearchVersion version)
    {
        return version switch
        {
            ArabicSearchVersion.V1 => V1,
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown Arabic search version."),
        };
    }

    /// <summary>The rule version.</summary>
    public ArabicSearchVersion Version { get; }

    /// <summary>The characters a search key may contain besides the single space between words, in code point order.</summary>
    public IReadOnlyList<char> OutputAlphabet { get; }

    /// <summary>The characters replaced one-to-one, each by a character of <see cref="OutputAlphabet" />.</summary>
    public IReadOnlyDictionary<char, char> Mappings { get; }

    /// <summary>The characters replaced by a string of <see cref="OutputAlphabet" /> characters.</summary>
    public IReadOnlyDictionary<char, string> Expansions { get; }

    /// <summary>The characters removed without a space, so a mark inside a word keeps the word joined, in code point order.</summary>
    public IReadOnlyList<char> Deletions { get; }

    internal string Normalize(string value)
    {
        // An expansion is at most four characters ("الله"), so four output characters per input bound the result.
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length * 4] : new char[value.Length * 4];
        var length = 0;
        var pendingSpace = false;

        foreach (var c in value)
        {
            var action = _actions[c];

            switch (action)
            {
                case _DeleteAction:
                    continue;
                case _SeparatorAction:
                    pendingSpace = length > 0;

                    continue;
            }

            if (pendingSpace)
            {
                buffer[length++] = ' ';
                pendingSpace = false;
            }

            if (action == _ExpandAction)
            {
                var expansion = Expansions[c];
                expansion.AsSpan().CopyTo(buffer[length..]);
                length += expansion.Length;
            }
            else
            {
                buffer[length++] = action;
            }
        }

        return new string(buffer[..length]);
    }

    internal static ArabicSearchRuleSet Create(
        ArabicSearchVersion version,
        SortedSet<char> outputAlphabet,
        Dictionary<char, char> mappings,
        Dictionary<char, string> expansions,
        SortedSet<char> deletions
    )
    {
        return new(
            version,
            [.. outputAlphabet],
            mappings.ToFrozenDictionary(),
            expansions.ToFrozenDictionary(),
            [.. deletions]
        );
    }
}

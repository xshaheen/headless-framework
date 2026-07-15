// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Primitives;

namespace Headless.Text;

/// <summary>
/// This class is used to extract dynamic values from a formatted string.
/// It works as reverse of <see cref="string.Format(string,object)"/>
/// </summary>
/// <example>
/// Say that str is "My name is Neo." and format is "My name is {name}.".
/// Then Extract method gets "Neo" as "name".
/// </example>
/// <remarks>
/// <para>
/// Matching is greedy on the first occurrence: each constant separator is matched against the
/// earliest position it appears in the remaining input. As a consequence, a dynamic value that
/// itself contains the literal text of the following separator is truncated at that first
/// occurrence. For example, extracting "{a}-{b}" from "x-y-z" yields a="x" and b="y-z" (the second
/// "-" is treated as part of the trailing value), while "x-y-z" against "{a}-{b}-{c}" yields
/// a="x", b="y", c="z". This greedy-first-match behavior is intentional and not configurable.
/// </para>
/// </remarks>
[PublicAPI]
public static class FormattedStringValueExtractor
{
    /// <summary>Extracts dynamic values from a formatted string.</summary>
    /// <param name="str">String including dynamic values</param>
    /// <param name="format">Format of the string</param>
    /// <param name="ignoreCase">True, to search case-insensitive.</param>
    /// <returns>
    /// A <see cref="FormattedStringExtractionResult"/> whose <see cref="FormattedStringExtractionResult.IsMatch"/>
    /// indicates whether <paramref name="str"/> matched <paramref name="format"/>, and whose
    /// <see cref="FormattedStringExtractionResult.Matches"/> holds the captured name/value pairs.
    /// </returns>
    /// <remarks>
    /// The whole input must be consumed for a successful match: if the format ends with constant
    /// text, any input remaining after that final constant causes <see cref="FormattedStringExtractionResult.IsMatch"/>
    /// to be <see langword="false"/>. See the type-level remarks for the greedy-first-match limitation.
    /// </remarks>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="format"/> has invalid syntax, such as mismatched or nested curly braces.
    /// </exception>
    public static FormattedStringExtractionResult Extract(string str, string format, bool ignoreCase = false)
    {
        var stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(str, format, StringComparison.Ordinal))
        {
            return new FormattedStringExtractionResult(isMatch: true);
        }

        var formatTokens = FormatStringTokenizer.Tokenize(format);

        if (formatTokens.IsNullOrEmpty())
        {
            return new FormattedStringExtractionResult(string.IsNullOrEmpty(str));
        }

        var result = new FormattedStringExtractionResult(isMatch: true);

        for (var i = 0; i < formatTokens.Count; i++)
        {
            var currentToken = formatTokens[i];
            var previousToken = i > 0 ? formatTokens[i - 1] : null;

            if (currentToken.Type == FormatStringTokenType.ConstantText)
            {
                if (i == 0)
                {
                    if (!str.StartsWith(currentToken.Text, stringComparison))
                    {
                        result.IsMatch = false;

                        return result;
                    }

                    str = str[currentToken.Text.Length..];
                }
                else
                {
                    var matchIndex = str.IndexOf(currentToken.Text, stringComparison);

                    if (matchIndex < 0)
                    {
                        result.IsMatch = false;

                        return result;
                    }

                    Debug.Assert(previousToken is not null, "previousToken can not be null since i > 0 here");

                    result.AddMatch(new NameValue { Name = previousToken.Text, Value = str[..matchIndex] });
                    str = str[(matchIndex + currentToken.Text.Length)..];
                }
            }
        }

        var lastToken = formatTokens[^1];

        if (lastToken.Type is FormatStringTokenType.DynamicValue)
        {
            // The trailing dynamic value greedily captures whatever input remains.
            result.AddMatch(new NameValue { Name = lastToken.Text, Value = str });
        }
        else if (str.Length > 0)
        {
            // The format ends with constant text but the input has unmatched trailing characters,
            // so the input is not fully consumed and therefore does not match.
            result.IsMatch = false;
        }

        return result;
    }

    /// <summary>
    /// Checks if given <paramref name="str"/> fits to given <paramref name="format"/>.
    /// Also gets extracted values.
    /// </summary>
    /// <param name="str">String including dynamic values</param>
    /// <param name="format">Format of the string</param>
    /// <param name="values">Array of extracted values if matched; an empty array when not matched.</param>
    /// <param name="ignoreCase">True, to search case-insensitive</param>
    /// <returns>True, if matched.</returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="format"/> has invalid syntax, such as mismatched or nested curly braces.
    /// </exception>
    public static bool IsMatch(string str, string format, out string[] values, bool ignoreCase = false)
    {
        var result = Extract(str, format, ignoreCase);

        if (!result.IsMatch)
        {
            values = [];

            return false;
        }

        values = [.. result.Matches.Select(m => m.Value)];

        return true;
    }
}

#region Types

[PublicAPI]
public sealed class FormattedStringExtractionResult
{
    private readonly List<NameValue> _matches = [];

    internal FormattedStringExtractionResult(bool isMatch)
    {
        IsMatch = isMatch;
    }

    /// <summary>Is fully matched.</summary>
    public bool IsMatch { get; internal set; }

    /// <summary>List of matched dynamic values.</summary>
    public IReadOnlyList<NameValue> Matches => _matches;

    internal void AddMatch(NameValue match)
    {
        _matches.Add(match);
    }
}

#endregion

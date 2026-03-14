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
public static class FormattedStringValueExtractor
{
    /// <summary>Extracts dynamic values from a formatted string.</summary>
    /// <param name="str">String including dynamic values</param>
    /// <param name="format">Format of the string</param>
    /// <param name="ignoreCase">True, to search case-insensitive.</param>
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

                    result.Matches.Add(new NameValue { Name = previousToken.Text, Value = str[..matchIndex] });
                    str = str[(matchIndex + currentToken.Text.Length)..];
                }
            }
        }

        var lastToken = formatTokens[^1];

        if (lastToken.Type is FormatStringTokenType.DynamicValue)
        {
            result.Matches.Add(new NameValue { Name = lastToken.Text, Value = str });
        }

        return result;
    }

    /// <summary>
    /// Checks if given <paramref name="str"/> fits to given <paramref name="format"/>.
    /// Also gets extracted values.
    /// </summary>
    /// <param name="str">String including dynamic values</param>
    /// <param name="format">Format of the string</param>
    /// <param name="values">Array of extracted values if matched</param>
    /// <param name="ignoreCase">True, to search case-insensitive</param>
    /// <returns>True, if matched.</returns>
    public static bool IsMatch(string str, string format, out string[] values, bool ignoreCase = false)
    {
        var result = Extract(str, format, ignoreCase);

        if (!result.IsMatch)
        {
            values = [];

            return false;
        }

        values = result.Matches.Select(m => m.Value).ToArray();

        return true;
    }
}

#region Types

public sealed class FormattedStringExtractionResult
{
    internal FormattedStringExtractionResult(bool isMatch)
    {
        IsMatch = isMatch;
        Matches = [];
    }

    /// <summary>Is fully matched.</summary>
    public bool IsMatch { get; internal set; }

    /// <summary>List of matched dynamic values.</summary>
    public List<NameValue> Matches { get; }
}

#endregion

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Headless.Checks;
using RegexPatterns = Headless.Constants.RegexPatterns;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>General-purpose extension methods for <see cref="string"/> and character sequences.</summary>
public static class StringExtensions
{
    /// <summary>
    /// Limits string length to a specified value by discarding any trailing characters after the specified length.
    /// </summary>
    /// <param name="input">The <see cref="string" /> value to limit to a specified length.</param>
    /// <param name="maxLength">The maximum length allowed for <paramref name="input"/>.</param>
    /// <returns>
    /// The <paramref name="input" /> string if its length is lesser or equal than
    /// <paramref name="maxLength"/>; otherwise first <c>n</c> characters of the <paramref name="input" />,
    /// where <c>n</c> is equal to <paramref name="maxLength"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? TruncateEnd(this string? input, [NonNegativeValue] int maxLength)
    {
        return input is null ? null
            : input.Length <= maxLength ? input
            : input[..maxLength];
    }

    /// <summary>
    /// Limits string length to a specified value by discarding a number of trailing characters and adds a specified
    /// suffix (ex "...") if any characters were discarded.
    /// </summary>
    /// <param name="input">The <see cref="string" /> value to limit to a specified length.</param>
    /// <param name="maxLength">The maximum length allowed for <paramref name="input"/>.</param>
    /// <param name="suffix">The suffix added to the result if any characters are discarded.</param>
    /// <returns>
    /// The <paramref name="input" /> string if its length is lesser or equal than
    /// <paramref name="maxLength"/>; otherwise first <c>n</c> characters of the <paramref name="input" />
    /// followed by <paramref name="suffix" />, where <c>n</c> is equal to <paramref name="maxLength"/> - length of
    /// <paramref name="suffix"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? TruncateEnd(this string? input, [NonNegativeValue] int maxLength, string suffix)
    {
        if (input is null)
        {
            return null;
        }

        if (input.Length == 0 || maxLength == 0)
        {
            return string.Empty;
        }

        if (input.Length <= maxLength)
        {
            return input;
        }

        if (maxLength <= suffix.Length)
        {
            return suffix[..maxLength];
        }

        return input[..(maxLength - suffix.Length)] + suffix;
    }

    /// <summary>
    /// Limits string length to a specified value by discarding any starting characters before the specified length.
    /// </summary>
    /// <param name="input">The <see cref="string" /> value to limit to a specified length.</param>
    /// <param name="maxLength">The maximum length allowed for <paramref name="input"/>.</param>
    /// <returns>
    /// The <paramref name="input" /> string if its length is lesser or equal than
    /// <paramref name="maxLength"/>; otherwise last <c>n</c> characters of the <paramref name="input" />,
    /// where <c>n</c> is equal to <paramref name="maxLength"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? TruncateStart(this string? input, [NonNegativeValue] int maxLength)
    {
        if (input is null)
        {
            return null;
        }

        return input.Length <= maxLength ? input : input.Substring(input.Length - maxLength, maxLength);
    }

    /// <summary>
    /// Indicates whether the specified string is <see langword="null"/> or an <see cref="string.Empty">Empty</see> string.
    /// </summary>
    /// <param name="input">The string to test.</param>
    /// <returns>
    /// <see langword="true"/> if the <paramref name="input"/> is <see langword="null"/> or an empty string (""); otherwise, <see langword="false"/>.
    /// </returns>
    /// <seealso cref="string.IsNullOrEmpty" />
    [SystemPure]
    [JetBrainsPure]
    public static bool IsNullOrEmpty([NotNullWhen(false)] this string? input)
    {
        return string.IsNullOrEmpty(input);
    }

    /// <summary>
    /// Indicates whether a specified string is <see langword="null"/>, empty, or consists only of white-space characters.
    /// </summary>
    /// <param name="input">A <see cref="string" /> value.</param>
    /// <returns>
    /// <see langword="true"/> if the value parameter is <see langword="null"/> or <see cref="string.Empty" />, or if <paramref name="input"/> consists exclusively of white-space characters.
    /// </returns>
    /// <seealso cref="string.IsNullOrWhiteSpace" />
    [SystemPure]
    [JetBrainsPure]
    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? input)
    {
        return string.IsNullOrWhiteSpace(input);
    }

    /// <summary>Return the specified string if it is not <see cref="string.Empty">Empty</see>, or <see langword="null"/> otherwise.</summary>
    /// <param name="input">The string to test.</param>
    /// <example>
    /// <c>var displayName = name.NullIfEmpty() ?? "Unknown";</c>
    /// </example>
    /// <returns>
    /// <paramref name="input"/> if it is not <see langword="null"/> or empty; otherwise, <see langword="null"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string? NullIfEmpty(this string? input)
    {
        return !string.IsNullOrEmpty(input) ? input : null;
    }

    /// <summary>Return the specified string if it is not white-space characters, <see cref="string.Empty">Empty</see>, or <see langword="null"/> otherwise.</summary>
    /// <param name="input">The string to test.</param>
    /// <example>
    /// <c>var displayName = name.NullIfWhiteSpace() ?? "Unknown";</c>
    /// </example>
    /// <returns>
    /// <paramref name="input"/> if it is not <see langword="null"/>, empty, or exclusively white-space characters; otherwise, <see langword="null"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string? NullIfWhiteSpace(this string? input)
    {
        return !string.IsNullOrWhiteSpace(input) ? input : null;
    }

    /// <summary>Converts all line endings in the string to <see cref="Environment.NewLine"/>.</summary>
    /// <param name="value">The string whose line endings are normalized.</param>
    /// <returns>A copy of <paramref name="value"/> with every <c>\r\n</c>, <c>\r</c>, and <c>\n</c> replaced by <see cref="Environment.NewLine"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string NormalizeLineEndings(this string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    /// <summary>Splits the string into lines using <see cref="Environment.NewLine"/> as the delimiter.</summary>
    /// <param name="input">The string to split.</param>
    /// <param name="options">Options to control whether empty entries are omitted from the result.</param>
    /// <returns>An array of substrings produced by splitting on <see cref="Environment.NewLine"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string[] SplitToLines(this string input, StringSplitOptions options = StringSplitOptions.None)
    {
        return input.Split(Environment.NewLine, options);
    }

    /// <summary>Ensures the string starts with <paramref name="c"/>, prepending it if not already present.</summary>
    /// <param name="input">The string to check.</param>
    /// <param name="c">The character that must appear at the start.</param>
    /// <param name="comparisonType">The comparison rule used to test for the leading character.</param>
    /// <returns><paramref name="input"/> unchanged if it already starts with <paramref name="c"/>; otherwise <paramref name="c"/> prepended to <paramref name="input"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string EnsureStartsWith(
        this string input,
        char c,
        StringComparison comparisonType = StringComparison.Ordinal
    )
    {
        // Compare via a single-char span to avoid allocating a one-char string for the non-ordinal path.
        ReadOnlySpan<char> needle = [c];

        var startsWithChar =
            comparisonType == StringComparison.Ordinal
                ? input.StartsWith(c)
                : input.AsSpan().StartsWith(needle, comparisonType);

        return startsWithChar ? input : c + input;
    }

    /// <summary>Ensures the string starts with <paramref name="suffix"/>, prepending it if not already present.</summary>
    /// <param name="input">The string to check.</param>
    /// <param name="suffix">The prefix string that must appear at the start.</param>
    /// <param name="comparisonType">The comparison rule used to test for the leading prefix.</param>
    /// <returns><paramref name="input"/> unchanged if it already starts with <paramref name="suffix"/>; otherwise <paramref name="suffix"/> prepended to <paramref name="input"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string EnsureStartsWith(
        this string input,
        string suffix,
        StringComparison comparisonType = StringComparison.Ordinal
    )
    {
        return input.StartsWith(suffix, comparisonType) ? input : suffix + input;
    }

    /// <summary>Ensures the string ends with <paramref name="suffix"/>, appending it if not already present.</summary>
    /// <param name="input">The string to check.</param>
    /// <param name="suffix">The character that must appear at the end.</param>
    /// <param name="comparisonType">The comparison rule used to test for the trailing character.</param>
    /// <returns><paramref name="input"/> unchanged if it already ends with <paramref name="suffix"/>; otherwise <paramref name="input"/> with <paramref name="suffix"/> appended.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string EnsureEndsWith(
        this string input,
        char suffix,
        StringComparison comparisonType = StringComparison.Ordinal
    )
    {
        // Compare via a single-char span to avoid allocating a one-char string for the non-ordinal path.
        ReadOnlySpan<char> needle = stackalloc char[1] { suffix };

        var endsWithChar =
            comparisonType == StringComparison.Ordinal
                ? input.EndsWith(suffix)
                : input.AsSpan().EndsWith(needle, comparisonType);

        return endsWithChar ? input : input + suffix;
    }

    /// <summary>Ensures the string ends with <paramref name="suffix"/>, appending it if not already present.</summary>
    /// <param name="input">The string to check.</param>
    /// <param name="suffix">The suffix string that must appear at the end.</param>
    /// <param name="comparisonType">The comparison rule used to test for the trailing suffix.</param>
    /// <returns><paramref name="input"/> unchanged if it already ends with <paramref name="suffix"/>; otherwise <paramref name="input"/> with <paramref name="suffix"/> appended.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string EnsureEndsWith(
        this string input,
        string suffix,
        StringComparison comparisonType = StringComparison.Ordinal
    )
    {
        return input.EndsWith(suffix, comparisonType) ? input : input + suffix;
    }

    /// <summary>Removes the first matching postfix string from the end of the string using ordinal comparison.</summary>
    /// <param name="input">The string.</param>
    /// <param name="postfixes">One or more candidate postfixes; the first match is removed.</param>
    /// <returns>The string without its trailing matched postfix, or the same string if none of <paramref name="postfixes"/> match, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePostfix(this string? input, params ReadOnlySpan<string> postfixes)
    {
        return input.RemovePostfix(StringComparison.Ordinal, postfixes);
    }

    /// <summary>
    /// Removes the first matching postfix string from the end of the string using the specified comparison.
    /// </summary>
    /// <param name="input">The string.</param>
    /// <param name="comparisonType">The comparison rule used to match each postfix.</param>
    /// <param name="postfixes">One or more candidate postfixes; the first match is removed.</param>
    /// <returns>The string without its trailing matched postfix, or the same string if none of <paramref name="postfixes"/> match, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePostfix(
        this string? input,
        StringComparison comparisonType,
        params ReadOnlySpan<string> postfixes
    )
    {
        if (input is null)
        {
            return null;
        }

        if (postfixes.IsEmpty)
        {
            return input;
        }

        foreach (var postfix in postfixes)
        {
            if (input.EndsWith(postfix, comparisonType))
            {
                return input[..^postfix.Length];
            }
        }

        return input;
    }

    /// <summary>Removes the given postfix character from the end of the string if it is present.</summary>
    /// <param name="input">The string.</param>
    /// <param name="postfix">The postfix character to remove.</param>
    /// <returns>
    /// The string without its trailing <paramref name="postfix"/>, the same string if it does not end with
    /// <paramref name="postfix"/>, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePostfix(this string? input, char postfix)
    {
        if (input is null)
        {
            return null;
        }

        return input.EndsWith(postfix) ? input[..^1] : input;
    }

    /// <summary>Removes every occurrence of the given character from the string.</summary>
    /// <param name="input">The string.</param>
    /// <param name="character">The character to remove.</param>
    /// <returns>
    /// A copy of <paramref name="input"/> with all occurrences of <paramref name="character"/> removed, or
    /// <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemoveCharacter(this string? input, char character)
    {
        if (input is null)
        {
            return null;
        }

        // Single-pass copy of the retained chars; Split+Concat would allocate an intermediate array and substrings.
        var sb = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            if (c != character)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Removes every occurrence of the given characters from the string.</summary>
    /// <param name="input">The string.</param>
    /// <param name="unwantedCharacters">The characters to remove.</param>
    /// <returns>
    /// A copy of <paramref name="input"/> with all occurrences of any character in
    /// <paramref name="unwantedCharacters"/> removed, or <see langword="null"/> if <paramref name="input"/> is
    /// <see langword="null"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemoveCharacters(this string? input, params ReadOnlySpan<char> unwantedCharacters)
    {
        if (input is null)
        {
            return null;
        }

        // Single-pass copy of the retained chars; Split+Concat would allocate an intermediate array and substrings.
        var sb = new StringBuilder(input.Length);

        // Matches string.Split(ReadOnlySpan<char>): an empty separator set strips whitespace instead.
        var stripWhitespace = unwantedCharacters.IsEmpty;

        foreach (var c in input)
        {
            var unwanted = stripWhitespace ? char.IsWhiteSpace(c) : unwantedCharacters.Contains(c);

            if (!unwanted)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes the first matching prefix string from the beginning of the string using ordinal comparison.
    /// </summary>
    /// <param name="input">The string.</param>
    /// <param name="prefixes">One or more candidate prefixes; the first match is removed.</param>
    /// <returns>The string without its leading matched prefix, or the same string if none of <paramref name="prefixes"/> match, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePrefix(this string? input, params ReadOnlySpan<string> prefixes)
    {
        return input.RemovePrefix(StringComparison.Ordinal, prefixes);
    }

    /// <summary>
    /// Removes the first matching prefix string from the beginning of the string using the specified comparison.
    /// </summary>
    /// <param name="input">The string.</param>
    /// <param name="comparisonType">The comparison rule used to match each prefix.</param>
    /// <param name="prefixes">One or more candidate prefixes; the first match is removed.</param>
    /// <returns>The string without its leading matched prefix, or the same string if none of <paramref name="prefixes"/> match, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePrefix(
        this string? input,
        StringComparison comparisonType,
        params ReadOnlySpan<string> prefixes
    )
    {
        if (input is null)
        {
            return null;
        }

        if (prefixes.IsEmpty)
        {
            return input;
        }

        foreach (var prefix in prefixes)
        {
            if (input.StartsWith(prefix, comparisonType))
            {
                var len = input.Length - prefix.Length;

                return input.Substring(input.Length - len, len);
            }
        }

        return input;
    }

    /// <summary>
    /// Removes the first matching prefix character from the beginning of the string.
    /// </summary>
    /// <param name="input">The string.</param>
    /// <param name="prefixes">One or more candidate prefix characters; the first match is removed.</param>
    /// <returns>
    /// The string without its leading matched prefix character, the same string if none of
    /// <paramref name="prefixes"/> match, or <see langword="null"/> if <paramref name="input"/> is
    /// <see langword="null"/> or empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePrefix(this string? input, params ReadOnlySpan<char> prefixes)
    {
        if (input.IsNullOrEmpty())
        {
            return null;
        }

        if (prefixes.IsEmpty)
        {
            return input;
        }

        foreach (var prefix in prefixes)
        {
            if (input.StartsWith(prefix))
            {
                var len = input.Length - 1;

                return input.Substring(input.Length - len, len);
            }
        }

        return input;
    }

    /// <summary>Removes the given prefix character from the beginning of the string if it is present.</summary>
    /// <param name="input">The string.</param>
    /// <param name="prefix">The prefix character to remove.</param>
    /// <returns>
    /// The string without its leading <paramref name="prefix"/>, the same string if it does not start with
    /// <paramref name="prefix"/>, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/> or empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemovePrefix(this string? input, char prefix)
    {
        if (input.IsNullOrEmpty())
        {
            return null;
        }

        if (input.StartsWith(prefix))
        {
            var len = input.Length - 1;

            return input.Substring(input.Length - len, len);
        }

        return input;
    }

    /// <summary>Encodes the string to a byte array using <see cref="Encoding.UTF8"/>.</summary>
    /// <param name="input">The string to encode.</param>
    /// <returns>The UTF-8 byte representation of <paramref name="input"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static byte[] GetBytes(this string input)
    {
        return input.GetBytes(Encoding.UTF8);
    }

    /// <summary>Encodes the string to a byte array using the given <paramref name="encoding"/>.</summary>
    /// <param name="input">The string to encode.</param>
    /// <param name="encoding">The encoding used to convert the string.</param>
    /// <returns>The encoded byte representation of <paramref name="input"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static byte[] GetBytes(this string input, Encoding encoding)
    {
        return encoding.GetBytes(input);
    }

    /// <summary>Applies a sequence of string replacements to the input, using <see cref="string.Replace(string,string,StringComparison)"/> for each pair.</summary>
    /// <param name="input">The string to transform.</param>
    /// <param name="replaces">Ordered pairs of (oldValue, newValue) to apply in sequence.</param>
    /// <param name="comparison">The comparison rule used for each replacement. Defaults to <see cref="StringComparison.Ordinal"/>.</param>
    /// <returns>A new string with all specified replacements applied in order.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string Replace(
        this string input,
        IEnumerable<(string oldValue, string newValue)> replaces,
        StringComparison comparison = StringComparison.Ordinal
    )
    {
        var output = input[..];

        foreach (var (oldValue, newValue) in replaces)
        {
            output = output.Replace(oldValue, newValue, comparison);
        }

        return output;
    }

    /// <summary>Convert any digit in the string to the equivalent Arabic digit [0-9].</summary>
    /// <example>"١٢٨" to "128"</example>
    /// <example>"١,٢٨" to "1,28"</example>
    /// <example>"١.٢٨" to "1.28"</example>
    /// <example>"This ١٢٨" to "This 128"</example>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? ToInvariantDigits(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var sb = new StringBuilder();

        foreach (var c in input)
        {
            if (char.IsDigit(c))
            {
                // Map the Unicode decimal digit to its ASCII equivalent without allocating or touching culture.
                sb.Append((char)('0' + (int)char.GetNumericValue(c)));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Converts any Unicode decimal digit in the character sequence to its ASCII equivalent [0-9].</summary>
    /// <param name="input">The character sequence to transform.</param>
    /// <returns>A new string with every Unicode decimal digit replaced by its ASCII equivalent; non-digit characters are unchanged.</returns>
    /// <example>"١٢٨" to "128"</example>
    /// <example>"١,٢٨" to "1,28"</example>
    /// <example>"١.٢٨" to "1.28"</example>
    /// <example>"This ١٢٨" to "This 128"</example>
    [SystemPure]
    [JetBrainsPure]
    public static string ToInvariantDigits(this IEnumerable<char> input)
    {
        var sb = new StringBuilder();

        foreach (var c in input)
        {
            if (char.IsDigit(c))
            {
                // Map the Unicode decimal digit to its ASCII equivalent without allocating or touching culture.
                sb.Append((char)('0' + (int)char.GetNumericValue(c)));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Remove control characters from string.</summary>
    /// <param name="input">The string to strip control characters from.</param>
    /// <returns>A copy of <paramref name="input"/> with all control (hidden) characters removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">Thrown when the match exceeds the pattern's 100ms timeout.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static string RemoveHiddenChars(this string input)
    {
        return RegexPatterns.HiddenChars.Replace(input, replacement: string.Empty);
    }

    /// <summary>Strips any single quotes or double quotes from the beginning and end of a string.</summary>
    /// <param name="s">The string to strip surrounding quotes from.</param>
    /// <returns>A copy of <paramref name="s"/> with leading and trailing single or double quotes removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">Thrown when the match exceeds the pattern's 100ms timeout.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static string StripQuotes(this string s)
    {
        return RegexPatterns.Quotes.Replace(s, "");
    }

    /// <summary>Applies a sequence of character replacements to the input, using <see cref="string.Replace(char,char)"/> for each pair.</summary>
    /// <param name="input">The string to transform.</param>
    /// <param name="replaces">Ordered pairs of (oldValue, newValue) characters to apply in sequence.</param>
    /// <returns>A new string with all specified character replacements applied in order.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string Replace(this string input, IEnumerable<(char oldValue, char newValue)> replaces)
    {
        var output = input[..];

        foreach (var (oldValue, newValue) in replaces)
        {
            output = output.Replace(oldValue, newValue);
        }

        return output;
    }

    /// <summary>Trims the string and returns <see langword="null"/> if the result is <see langword="null"/>, empty, or all white-space.</summary>
    /// <param name="input">The string to trim.</param>
    /// <returns>The trimmed string, or <see langword="null"/> if <paramref name="input"/> is <see langword="null"/>, empty, or exclusively white-space.</returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? NullableTrim(this string? input)
    {
        return input.IsNullOrWhiteSpace() ? null : input.Trim();
    }

    /// <summary>
    /// Replace any white space characters [\r\n\t\f\v ] with one white space.
    /// </summary>
    /// <param name="input">The string whose whitespace runs are collapsed.</param>
    /// <returns>A copy of <paramref name="input"/> with each run of whitespace replaced by a single space.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">Thrown when the match exceeds the pattern's 100ms timeout.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static string OneSpace(this string input)
    {
        return RegexPatterns.Spaces.Replace(input, " ");
    }

    /// <summary>
    /// Converts string to enum value.
    /// </summary>
    /// <typeparam name="T">Type of enum</typeparam>
    /// <param name="value">String value to convert</param>
    /// <param name="ignoreCase">Ignore a case</param>
    /// <returns>Returns enum object</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> does not match a defined name of <typeparamref name="T"/>.</exception>
    /// <exception cref="OverflowException">Thrown when <paramref name="value"/> is outside the range of <typeparamref name="T"/>'s underlying type.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static T ToEnum<T>(this string value, bool ignoreCase = true)
        where T : struct
    {
        return Enum.Parse<T>(Argument.IsNotNull(value), ignoreCase);
    }

    /// <summary>Returns the index of the <paramref name="n"/>th occurrence of <paramref name="c"/> in the string.</summary>
    /// <param name="input">The string to search.</param>
    /// <param name="c">The character to find.</param>
    /// <param name="n">The ordinal number of the occurrence to find (1-based).</param>
    /// <returns>The zero-based index of the <paramref name="n"/>th occurrence of <paramref name="c"/>, or <c>-1</c> if fewer than <paramref name="n"/> occurrences exist.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static int NthIndexOf(this string input, char c, int n)
    {
        var count = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != c)
            {
                continue;
            }

            if (++count == n)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Compares two strings character by character and returns the index of the first differing position.
    /// </summary>
    /// <param name="s1">The first string to compare.</param>
    /// <param name="s2">The second string to compare.</param>
    /// <returns>
    /// The zero-based index of the first position where <paramref name="s1"/> and <paramref name="s2"/> differ;
    /// if one is a prefix of the other, the length of the shorter string; if the strings are identical,
    /// the length of the shorter string.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static int Diff(this string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var len = len1 < len2 ? len1 : len2;

        for (var i = 0; i < len; i++)
        {
            if (s1[i] != s2[i])
            {
                return i;
            }
        }

        return len;
    }

    /// <summary>Determines whether the string is a valid dotted-decimal IPv4 address.</summary>
    /// <param name="s">The string to test.</param>
    /// <returns><see langword="true"/> if <paramref name="s"/> consists of exactly four dot-separated octets each parsable as a <see cref="byte"/>; otherwise, <see langword="false"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static bool IsIp4(this string s)
    {
        // based on https://stackoverflow.com/a/29942932/62600
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        var remaining = s.AsSpan();
        var parts = 0;

        // Span tokenization on '.' avoids the substring array allocated by Split + the LINQ closure.
        while (true)
        {
            var dot = remaining.IndexOf('.');
            var part = dot < 0 ? remaining : remaining[..dot];

            if (++parts > 4 || !byte.TryParse(part, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            if (dot < 0)
            {
                break;
            }

            remaining = remaining[(dot + 1)..];
        }

        return parts == 4;
    }

    /// <summary>
    /// Removes accent (diacritic) characters from the string, leaving only base characters.
    /// <para>"crème brûlée" to "creme brulee"</para>
    /// <para>"بِسْمِ اللَّهِ الرَّحْمَنِ الرَّحِيمِ" to "بسم الله الرحمن الرحيم"</para>
    /// </summary>
    /// <param name="input">The string to strip of accent marks.</param>
    /// <returns>
    /// A copy of <paramref name="input"/> with all non-spacing Unicode marks removed, or
    /// <paramref name="input"/> unchanged if it is <see langword="null"/> or white-space only.
    /// </returns>
    /// <remarks>
    /// <para>Normalizes to FormD to split accented letters into base letters plus combining marks, removes non-spacing marks, then re-normalizes to FormC.</para>
    /// </remarks>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(input))]
    public static string? RemoveAccentCharacters(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        // FormD splits accented letters into base char + combining marks; drop the non-spacing marks, then recompose with FormC.
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) is not UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Checks whether the text contains any right-to-left (Arabic-script) characters.</summary>
    /// <param name="text">Unicode text to test.</param>
    /// <returns><see langword="true"/> if at least one character falls in an RTL (Arabic-script) Unicode range; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">Thrown when the match exceeds the pattern's 100ms timeout.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static bool IsRtlText(this string text)
    {
        return RegexPatterns.RtlCharacters.IsMatch(text);
    }

    /// <summary>
    /// Returns <paramref name="defaultValue"/> when the string is <see langword="null"/>, empty, or consists only of white-space characters; otherwise returns the original string.
    /// </summary>
    /// <param name="str">The string to test.</param>
    /// <param name="defaultValue">The value to return when <paramref name="str"/> is blank.</param>
    /// <returns><paramref name="str"/> if it has non-whitespace content; otherwise <paramref name="defaultValue"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string DefaultIfEmpty(this string? str, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(str) ? defaultValue : str;
    }

    /// <summary>
    /// Normalizes directory separators in <paramref name="path"/> to the current platform's
    /// <see cref="Path.DirectorySeparatorChar"/>.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>
    /// <paramref name="path"/> with <c>/</c> and <c>\</c> replaced by the platform separator; the original value if
    /// it is <see langword="null"/> or empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string NormalizePath(this string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return Path.DirectorySeparatorChar switch
        {
            '\\' => path.Replace('/', Path.DirectorySeparatorChar),
            '/' => path.Replace('\\', Path.DirectorySeparatorChar),
            _ => path,
        };
    }

    /// <summary>Computes the MD5 hash of the UTF-8 bytes of <paramref name="str"/> and returns it as an uppercase hex string.</summary>
    /// <param name="str">The string to hash.</param>
    /// <returns>The MD5 hash of <paramref name="str"/> encoded as an uppercase hexadecimal string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="str"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 is used for file integrity check."
    )]
    [SystemPure]
    [JetBrainsPure]
    public static string ToMd5(this string str)
    {
        var data = MD5.HashData(Encoding.UTF8.GetBytes(str));
        return Convert.ToHexString(data);
    }

    /// <summary>Computes the SHA-256 hash of the UTF-8 bytes of <paramref name="str"/> and returns it as a lowercase hex string.</summary>
    /// <param name="str">The string to hash.</param>
    /// <returns>The SHA-256 hash of <paramref name="str"/> encoded as a lowercase hexadecimal string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="str"/> is <see langword="null"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static string ToSha256(this string str)
    {
        var data = SHA256.HashData(Encoding.UTF8.GetBytes(str));
        return Convert.ToHexStringLower(data);
    }

    /// <summary>Computes the SHA-512 hash of the UTF-8 bytes of <paramref name="str"/> and returns it as a lowercase hex string.</summary>
    /// <param name="str">The string to hash.</param>
    /// <returns>The SHA-512 hash of <paramref name="str"/> encoded as a lowercase hexadecimal string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="str"/> is <see langword="null"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static string ToSha512(this string str)
    {
        var data = SHA512.HashData(Encoding.UTF8.GetBytes(str));
        return Convert.ToHexStringLower(data);
    }

    /// <summary>Parses <paramref name="str"/> into a value of type <typeparamref name="T"/> using <see cref="ISpanParsable{TSelf}"/>.</summary>
    /// <typeparam name="T">The parsable target type.</typeparam>
    /// <param name="str">The string to parse.</param>
    /// <param name="format">(Optional) Culture-specific formatting information. Defaults to <see langword="null"/>.</param>
    /// <returns>The value of type <typeparamref name="T"/> parsed from <paramref name="str"/>.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="str"/> is not in a format recognized by <typeparamref name="T"/>.</exception>
    /// <exception cref="OverflowException">Thrown when <paramref name="str"/> represents a value outside the range of <typeparamref name="T"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static T Parse<T>(this string str, IFormatProvider? format = null)
        where T : ISpanParsable<T>
    {
        return T.Parse(str.AsSpan(), format);
    }

    /// <summary>Attempts to parse <paramref name="str"/> into a value of type <typeparamref name="T"/> using <see cref="ISpanParsable{TSelf}"/>.</summary>
    /// <typeparam name="T">The parsable target type.</typeparam>
    /// <param name="str">The string to parse.</param>
    /// <param name="format">Culture-specific formatting information.</param>
    /// <param name="value">When this method returns, the parsed value if parsing succeeded; otherwise the default value of <typeparamref name="T"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="str"/> was parsed successfully; otherwise, <see langword="false"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static bool TryParse<T>(this string str, IFormatProvider? format, [NotNullWhen(true)] out T? value)
        where T : ISpanParsable<T>
    {
        return T.TryParse(str.AsSpan(), format, out value);
    }

    /// <summary>Attempts to parse <paramref name="str"/> into a value of type <typeparamref name="T"/> using the invariant/default format.</summary>
    /// <typeparam name="T">The parsable target type.</typeparam>
    /// <param name="str">The string to parse.</param>
    /// <param name="value">When this method returns, the parsed value if parsing succeeded; otherwise the default value of <typeparamref name="T"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="str"/> was parsed successfully; otherwise, <see langword="false"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static bool TryParse<T>(this string str, [NotNullWhen(true)] out T? value)
        where T : ISpanParsable<T>
    {
        return str.TryParse(null, out value);
    }

    /// <summary>
    /// Normalizes the format of a property path by ensuring each segment of the path is in camelCase.
    /// </summary>
    /// <param name="propertyName">The property path to normalize.</param>
    /// <returns>The normalized property path, with each segment formatted in camelCase.</returns>
    /// <example>var result = "User.FirstName".CamelizePropertyPath(); // Returns "user.firstName"</example>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(propertyName))]
    public static string? CamelizePropertyPath(this string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var parts = propertyName.Split('.');

        // camelCase all the parts
        var newSpan = new char[propertyName.Length];
        var index = 0;

        foreach (var part in parts)
        {
            if (index > 0)
            {
                newSpan[index++] = '.';
            }

            // Empty segment (consecutive, leading, or trailing dots) has no first char to camelize.
            if (part.Length == 0)
            {
                continue;
            }

            newSpan[index++] = char.ToLowerInvariant(part[0]);
            part.AsSpan(1).CopyTo(newSpan.AsSpan(index));
            index += part.Length - 1;
        }

        return new string(newSpan, 0, index);
    }

    /// <summary>Encodes the UTF-8 bytes of <paramref name="text"/> as a Base64 string.</summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The Base64 representation of the UTF-8 bytes of <paramref name="text"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    public static string ToBase64(this string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Encodes the given bytes as a Base64 string.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The Base64 representation of <paramref name="bytes"/>.</returns>
    public static string ToBase64(this ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Encodes the given bytes as a Base64 string.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The Base64 representation of <paramref name="bytes"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is <see langword="null"/>.</exception>
    public static string ToBase64(this byte[] bytes)
    {
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Decodes a Base64 string into its UTF-8 text representation.</summary>
    /// <param name="base64Value">The Base64-encoded string to decode.</param>
    /// <returns>The UTF-8 text decoded from <paramref name="base64Value"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="base64Value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="base64Value"/> is not a valid Base64 string.</exception>
    public static string DecodeBase64(this string base64Value)
    {
        var bytes = Convert.FromBase64String(base64Value);

        return Encoding.UTF8.GetString(bytes);
    }
}

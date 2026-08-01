// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Text;

internal static class FormatStringTokenizer
{
    /// <summary>
    /// Tokenizes a format string into a collection of tokens, which can represent either constant text
    /// or dynamic value placeholders.
    /// </summary>
    /// <param name="format">The format string to be tokenized. It may contain literal text and placeholders enclosed in curly braces.</param>
    /// <param name="includeBracketsForDynamicValues">
    /// A boolean flag indicating whether to include the curly braces in tokens identified as dynamic values.
    /// If set to true, the dynamic value tokens will retain their enclosing curly braces.
    /// </param>
    /// <returns>
    /// A read-only list of tokens representing the parsed structure of the format string. Each token is
    /// categorized as either a constant text or a dynamic value. The result may be shared between callers,
    /// so it must not be mutated; <see cref="FormatStringToken"/> is an immutable record.
    /// </returns>
    /// <exception cref="FormatException">Thrown if the format string contains invalid syntax, such as mismatched curly braces or nested dynamic value placeholders.</exception>
    public static IReadOnlyList<FormatStringToken> Tokenize(string format, bool includeBracketsForDynamicValues = false)
    {
        var cache = includeBracketsForDynamicValues ? _BracketedTokenCache : _PlainTokenCache;

        if (cache.TryGetValue(format, out var cached))
        {
            return cached;
        }

        var tokens = _Tokenize(format, includeBracketsForDynamicValues);

        // Keyed on the string instance rather than its content: the hot callers tokenize a compile-time
        // constant (a CompositeFormat's Format, always the same reference) on every cache-key parse and
        // always hit, while a caller-built string is collected along with its entry instead of growing an
        // unbounded cache keyed on untrusted input.
        cache.AddOrUpdate(format, tokens);

        return tokens;
    }

    private static readonly ConditionalWeakTable<string, IReadOnlyList<FormatStringToken>> _PlainTokenCache = new();
    private static readonly ConditionalWeakTable<string, IReadOnlyList<FormatStringToken>> _BracketedTokenCache = new();

    private static ImmutableArray<FormatStringToken> _Tokenize(string format, bool includeBracketsForDynamicValues)
    {
        var tokens = new List<FormatStringToken>();

        var currentText = new StringBuilder();
        var inDynamicValue = false;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];

            switch (c)
            {
                case '{':
                    if (inDynamicValue)
                    {
                        FormattableString message =
                            $"Incorrect syntax at char '{i}'! format string can not contain nested dynamic value expression!";
                        throw new FormatException(message.ToString(CultureInfo.InvariantCulture));
                    }

                    inDynamicValue = true;

                    if (currentText.Length > 0)
                    {
                        tokens.Add(new FormatStringToken(currentText.ToString(), FormatStringTokenType.ConstantText));
                        currentText.Clear();
                    }

                    break;
                case '}':
                    if (!inDynamicValue)
                    {
                        FormattableString message =
                            $"Incorrect syntax at char {i}! These is no opening brackets for the closing bracket }}.";

                        throw new FormatException(message.ToString(CultureInfo.InvariantCulture));
                    }

                    inDynamicValue = false;

                    if (currentText.Length <= 0)
                    {
                        FormattableString message =
                            $"Incorrect syntax at char {i}! Brackets does not contain any chars.";

                        throw new FormatException(message.ToString(CultureInfo.InvariantCulture));
                    }

                    var dynamicValue = currentText.ToString();
                    if (includeBracketsForDynamicValues)
                    {
                        dynamicValue = "{" + dynamicValue + "}";
                    }

                    tokens.Add(new FormatStringToken(dynamicValue, FormatStringTokenType.DynamicValue));
                    currentText.Clear();

                    break;
                default:
                    currentText.Append(c);
                    break;
            }
        }

        if (inDynamicValue)
        {
            throw new FormatException("There is no closing } char for an opened { char.");
        }

        if (currentText.Length > 0)
        {
            tokens.Add(new FormatStringToken(currentText.ToString(), FormatStringTokenType.ConstantText));
        }

        return [.. tokens];
    }
}

#region Types

internal sealed record FormatStringToken(string Text, FormatStringTokenType Type);

internal enum FormatStringTokenType
{
    ConstantText,
    DynamicValue,
}

#endregion

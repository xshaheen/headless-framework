// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> or <paramref name="prefix"/> is null,
    /// or an <see cref="ArgumentException" /> if <paramref name="argument" /> does not start with <paramref name="prefix"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="prefix">The prefix the argument must start with.</param>
    /// <param name="comparison">The string comparison rule to use. Defaults to <see cref="StringComparison.Ordinal"/>.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it starts with <paramref name="prefix"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> or <paramref name="prefix"/> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> does not start with <paramref name="prefix"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string StartsWith(
        [SystemNotNull] string? argument,
        string prefix,
        StringComparison comparison = StringComparison.Ordinal,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotNull(prefix);

        if (argument.StartsWith(prefix, comparison))
        {
            return argument;
        }

        _ThrowForStartsWith(message, paramName, prefix);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> or <paramref name="suffix"/> is null,
    /// or an <see cref="ArgumentException" /> if <paramref name="argument" /> does not end with <paramref name="suffix"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="suffix">The suffix the argument must end with.</param>
    /// <param name="comparison">The string comparison rule to use. Defaults to <see cref="StringComparison.Ordinal"/>.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it ends with <paramref name="suffix"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> or <paramref name="suffix"/> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> does not end with <paramref name="suffix"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string EndsWith(
        [SystemNotNull] string? argument,
        string suffix,
        StringComparison comparison = StringComparison.Ordinal,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotNull(suffix);

        if (argument.EndsWith(suffix, comparison))
        {
            return argument;
        }

        _ThrowForEndsWith(message, paramName, suffix);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> or <paramref name="substring"/> is null,
    /// or an <see cref="ArgumentException" /> if <paramref name="argument" /> does not contain <paramref name="substring"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="substring">The substring the argument must contain.</param>
    /// <param name="comparison">The string comparison rule to use. Defaults to <see cref="StringComparison.Ordinal"/>.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it contains <paramref name="substring"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> or <paramref name="substring"/> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> does not contain <paramref name="substring"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Contains(
        [SystemNotNull] string? argument,
        string substring,
        StringComparison comparison = StringComparison.Ordinal,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotNull(substring);

        if (argument.Contains(substring, comparison))
        {
            return argument;
        }

        _ThrowForContains(message, paramName, substring);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> starts or ends with white space.
    /// A <see langword="null"/> or empty argument passes; combine with <see cref="IsNotNullOrWhiteSpace"/> to require
    /// a value.
    /// </summary>
    /// <remarks>
    /// Use it for a string stored as a key. SQL Server pads trailing spaces before it compares strings or enforces a
    /// unique index, under every collation, while PostgreSQL <c>varchar</c> and .NET ordinal comparison keep them, so
    /// <c>"acme"</c> and <c>"acme "</c> are one key on one provider and two on another.
    /// </remarks>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it neither starts nor ends with white space.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> starts or ends with white space.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(argument))]
    public static string? HasNoSurroundingWhiteSpace(
        string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is { Length: > 0 } && (char.IsWhiteSpace(argument[0]) || char.IsWhiteSpace(argument[^1])))
        {
            _ThrowForHasNoSurroundingWhiteSpace(message, paramName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is text that some storage provider
    /// would not store, compare, and return unchanged as a key. A <see langword="null"/> or empty argument passes;
    /// combine with <see cref="IsNotNullOrWhiteSpace"/> to require a value.
    /// </summary>
    /// <remarks>
    /// Use it at a feature's shared validation, before any provider sees the value, so no provider keeps what another
    /// merges, rejects, or rewrites. It refuses:
    /// <list type="bullet">
    /// <item>
    /// Surrounding whitespace: SQL Server pads the shorter string with spaces before comparing, under every collation
    /// and in unique-index uniqueness, so <c>"a"</c> and <c>"a "</c> are one key there and two on PostgreSQL.
    /// </item>
    /// <item>A NUL character: PostgreSQL text cannot hold U+0000 and fails the statement (22021).</item>
    /// <item>
    /// An unpaired UTF-16 surrogate: it is not valid Unicode, so Npgsql's UTF-8 encoder refuses it before sending, and
    /// SqlClient replaces it with U+FFFD before sending, so SQL Server stores and matches a different key than the
    /// caller's and every lone surrogate collapses into one: <c>"a\uD800"</c> and <c>"a\uDBFF"</c> are one key there.
    /// </item>
    /// </list>
    /// Case, accents, precomposed versus decomposed forms, surrogate pairs, and other control characters round-trip and
    /// compare ordinally on every provider with a binary or <c>"C"</c> key collation, so they stay distinct keys.
    /// </remarks>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if every provider keeps it unchanged.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is not portable key text.</exception>
    [DebuggerStepThrough]
    [return: NotNullIfNotNull(nameof(argument))]
    public static string? IsPortableKey(
        string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is not null && _FindUnportableKeyReason(argument) is { } reason)
        {
            _ThrowForIsPortableKey(message, paramName, reason);
        }

        return argument;
    }

    private static string? _FindUnportableKeyReason(string value)
    {
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            return "starts or ends with white space, which SQL Server ignores when comparing keys";
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\0')
            {
                return "contains a NUL character, which PostgreSQL cannot store";
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;

                continue;
            }

            if (char.IsSurrogate(c))
            {
                return "contains an unpaired UTF-16 surrogate, which is not valid Unicode and does not round-trip";
            }
        }

        return null;
    }

    [DoesNotReturn]
    private static void _ThrowForIsPortableKey(string? message, string? paramName, string reason)
    {
        throw new ArgumentException(
            message
                ?? $"The argument {paramName.ToAssertString()} {reason}, so storage providers would disagree about the key it names.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasNoSurroundingWhiteSpace(string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must not start or end with white space.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForStartsWith(string? message, string? paramName, string prefix)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must start with \"{prefix}\".",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForEndsWith(string? message, string? paramName, string suffix)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must end with \"{suffix}\".",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForContains(string? message, string? paramName, string substring)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must contain \"{substring}\".",
            paramName
        );
    }
}

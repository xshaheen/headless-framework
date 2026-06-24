// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

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

        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must start with \"{prefix}\".",
            paramName
        );
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

        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must end with \"{suffix}\".",
            paramName
        );
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

        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must contain \"{substring}\".",
            paramName
        );
    }
}

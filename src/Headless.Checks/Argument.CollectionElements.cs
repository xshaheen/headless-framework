// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> has any null element.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument has not null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> has any null element.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<T> HasNoNulls<T>(
        [SystemNotNull] IReadOnlyCollection<T?>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : class
    {
        IsNotNull(argument, paramName);

        if (!argument.Any(e => e is null))
        {
            return argument!;
        }

        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} cannot contains null elements.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> has any null or empty element.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument has not null or empty element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> has any null or empty element.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<string> HasNoNullOrEmptyElements(
        [SystemNotNull] IReadOnlyCollection<string?>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, paramName);

        if (argument.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                message ?? $"The argument {paramName.ToAssertString()} cannot contains empty elements.",
                paramName
            );
        }

        return argument!;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> has any null or white space element.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument has not null or white space element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> has any null or white space element.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<string> HasNoNullOrWhiteSpaceElements(
        [SystemNotNull] IReadOnlyCollection<string?>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, paramName);

        if (argument.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                message ?? $"The argument {paramName.ToAssertString()} cannot contains empty or white space elements.",
                paramName
            );
        }

        return argument!;
    }
}

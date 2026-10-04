// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="argument" /> has any null element.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument has no null element.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argument" /> has any null element.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<T> HasNoNulls<T>(
        [SystemNotNull] IReadOnlyCollection<T?>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : class
    {
        IsNotNull(argument, message, paramName);

        if (argument.Any(e => e is null))
        {
            _ThrowForHasNoNulls(message, paramName);
        }

        return argument!;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="argument" /> has any null or empty element.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument has no null or empty element.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argument" /> has any null or empty element.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<string> HasNoNullOrEmptyElements(
        [SystemNotNull] IReadOnlyCollection<string?>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Any(string.IsNullOrEmpty))
        {
            _ThrowForHasNoNullOrEmptyElements(message, paramName);
        }

        return argument!;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="argument" /> has any null or white space element.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument has no null or white space element.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argument" /> has any null or white space element.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<string> HasNoNullOrWhiteSpaceElements(
        [SystemNotNull] IReadOnlyCollection<string?>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Any(string.IsNullOrWhiteSpace))
        {
            _ThrowForHasNoNullOrWhiteSpaceElements(message, paramName);
        }

        return argument!;
    }

    [DoesNotReturn]
    private static void _ThrowForHasNoNulls(string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} cannot contains null elements.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasNoNullOrEmptyElements(string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} cannot contains empty elements.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasNoNullOrWhiteSpaceElements(string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} cannot contains empty or white space elements.",
            paramName
        );
    }
}

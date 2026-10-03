// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentNullException" /> when <paramref name="argument" /> is null.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument is not null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: SystemNotNull]
    public static T IsNotNull<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is null)
        {
            _ThrowForIsNotNull(message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentNullException" /> when <paramref name="argument" /> is null.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument is not null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotNull<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        if (argument is null)
        {
            _ThrowForIsNotNull(message, paramName);
        }

        return argument.Value;
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotNull(string? message, string? paramName)
    {
        throw new ArgumentNullException(
            paramName,
            message ?? $"Required argument {paramName.ToAssertString()} was null."
        );
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null.
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is an empty or white space string.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the value is not null, or an empty or white space string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argument" /> is an empty or white space string.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string IsNotNullOrWhiteSpace(
        [SystemNotNull] string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotEmpty(argument, message, paramName);

        if (_IsWhiteSpace(argument))
        {
            _ThrowForIsNotNullOrWhiteSpace(message, paramName);
        }

        return argument;
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _IsWhiteSpace(string value)
    {
        foreach (var t in value)
        {
            if (!char.IsWhiteSpace(t))
            {
                return false;
            }
        }

        return true;
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotNullOrWhiteSpace(string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"Required argument {paramName.ToAssertString()} was empty or white space.",
            paramName
        );
    }
}

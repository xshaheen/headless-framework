// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="argument"/> is NaN.</summary>
    /// <typeparam name="T">An IEEE 754 floating-point type (for example <see cref="float"/>, <see cref="double"/>, or <see cref="Half"/>).</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the value is not NaN.</returns>
    /// <exception cref="ArgumentException"><paramref name="argument" /> is NaN.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotNaN<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IFloatingPointIeee754<T>
    {
        if (T.IsNaN(argument))
        {
            _ThrowForIsNotNaN(message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="argument"/> is not NaN.</summary>
    /// <typeparam name="T">An IEEE 754 floating-point type (for example <see cref="float"/>, <see cref="double"/>, or <see cref="Half"/>).</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the value is NaN.</returns>
    /// <exception cref="ArgumentException"><paramref name="argument" /> is not NaN.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNaN<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IFloatingPointIeee754<T>
    {
        if (!T.IsNaN(argument))
        {
            _ThrowForIsNaN(message, paramName);
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotNaN(string? message, string? paramName)
    {
        throw new ArgumentException(message ?? $"The argument {paramName.ToAssertString()} cannot be NaN.", paramName);
    }

    [DoesNotReturn]
    private static void _ThrowForIsNaN(string? message, string? paramName)
    {
        throw new ArgumentException(message ?? $"The argument {paramName.ToAssertString()} must be a NaN.", paramName);
    }
}

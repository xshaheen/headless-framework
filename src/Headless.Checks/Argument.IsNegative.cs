// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> when <paramref name="argument" /> is not negative.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument is negative.</returns>
    /// <remarks>For floating-point types, non-finite values (<see cref="double.NaN"/>, infinities) are rejected.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> is not negative.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNegative<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        if (!T.IsFinite(argument) || argument >= T.Zero)
        {
            _ThrowForIsNegative(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsNegative<T>(
        T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, INumber<T>
    {
        if (argument is null)
        {
            return null;
        }

        if (!T.IsFinite(argument.Value) || argument.Value >= T.Zero)
        {
            _ThrowForIsNegative(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan IsNegative(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument >= TimeSpan.Zero)
        {
            _ThrowForIsNegative(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan? IsNegative(
        TimeSpan? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is null)
        {
            return null;
        }

        if (argument >= TimeSpan.Zero)
        {
            _ThrowForIsNegative(message, paramName);
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsNegative(string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} cannot be non negative."
        );
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> when <paramref name="argument" /> is not zero.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument is zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> is not zero.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        if (!T.IsZero(argument))
        {
            _ThrowForIsZero(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsZero<T>(
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

        if (!T.IsZero(argument.Value))
        {
            _ThrowForIsZero(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan IsZero(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument != TimeSpan.Zero)
        {
            _ThrowForIsZero(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan? IsZero(
        TimeSpan? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is null)
        {
            return null;
        }

        if (argument.Value != TimeSpan.Zero)
        {
            _ThrowForIsZero(message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> when <paramref name="argument" /> is zero.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the argument is not zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> is zero.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        if (T.IsZero(argument))
        {
            _ThrowForIsNotZero(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsNotZero<T>(
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

        if (T.IsZero(argument.Value))
        {
            _ThrowForIsNotZero(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan IsNotZero(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument == TimeSpan.Zero)
        {
            _ThrowForIsNotZero(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan? IsNotZero(
        TimeSpan? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is null)
        {
            return null;
        }

        if (argument.Value == TimeSpan.Zero)
        {
            _ThrowForIsNotZero(message, paramName);
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsZero(string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} must be zero."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotZero(string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} must not be zero."
        );
    }
}

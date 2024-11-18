// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Framework.Checks.Internals;

namespace Framework.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is negative.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is not negative.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is negative.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsPositiveOrZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        return argument >= T.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsPositiveOrZero<T>(
        T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, INumber<T>
    {
        return argument is null ? null
            : argument >= T.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan IsPositiveOrZero(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= TimeSpan.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan? IsPositiveOrZero(
        TimeSpan? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is null ? null
            : argument >= TimeSpan.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short IsPositiveOrZero(
        short argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsPositiveOrZero(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long IsPositiveOrZero(
        long argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= 0L
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float IsPositiveOrZero(
        float argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= 0F
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double IsPositiveOrZero(
        double argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= 0D
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositiveOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal IsPositiveOrZero(
        decimal argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument >= 0M
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be negative.",
                paramName
            );
    }
}

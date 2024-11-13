// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

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
    public static T IsPositive<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        return argument > T.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsPositive<T>(
        T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, INumber<T>
    {
        return argument is null ? null
            : argument > T.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan IsPositive(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > TimeSpan.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan? IsPositive(
        TimeSpan? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is null ? null
            : argument > TimeSpan.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short IsPositive(
        short argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsPositive(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long IsPositive(
        long argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float IsPositive(
        float argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double IsPositive(
        double argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsPositive{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal IsPositive(
        decimal argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument > 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non positive.",
                paramName
            );
    }
}

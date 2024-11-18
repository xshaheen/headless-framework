// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Framework.Checks.Internals;

namespace Framework.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is non negative.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is negative.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is non negative.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNegative<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        return argument < T.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
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
        return argument is null ? null
            : argument < T.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
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
        return argument < TimeSpan.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
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
        return argument is null ? null
            : argument < TimeSpan.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short IsNegative(
        short argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsNegative(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long IsNegative(
        long argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float IsNegative(
        float argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double IsNegative(
        double argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegative{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal IsNegative(
        decimal argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be non negative.",
                paramName
            );
    }
}

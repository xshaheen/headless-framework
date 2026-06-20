// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is positive.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is negative or zero.</returns>
    /// <remarks>For floating-point types, non-finite values (<see cref="double.NaN"/>, infinities) are rejected.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is positive or non-finite.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNegativeOrZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        if (T.IsFinite(argument) && argument <= T.Zero)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} cannot be positive."
        );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsNegativeOrZero<T>(
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

        if (T.IsFinite(argument.Value) && argument.Value <= T.Zero)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} cannot be positive."
        );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan IsNegativeOrZero(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= TimeSpan.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive."
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan? IsNegativeOrZero(
        TimeSpan? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is null ? null
            : argument <= TimeSpan.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive."
            );
    }
}

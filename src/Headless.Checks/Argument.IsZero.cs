// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not zero.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is not zero.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        return T.IsZero(argument)
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must be zero."
            );
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

        return T.IsZero(argument.Value)
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must be zero."
            );
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
        return argument == TimeSpan.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must be zero."
            );
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
        return argument is null ? null
            : argument.Value == TimeSpan.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must be zero."
            );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is zero.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is not zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is zero.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        return T.IsZero(argument)
            ? throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must not be zero."
            )
            : argument;
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

        return T.IsZero(argument.Value)
            ? throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must not be zero."
            )
            : argument;
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
        return argument == TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(
                paramName,
                message ?? $"The argument {paramName.ToAssertString()} must not be zero."
            )
            : argument;
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
        return argument is null ? null
            : argument.Value == TimeSpan.Zero
                ? throw new ArgumentOutOfRangeException(
                    paramName,
                    message ?? $"The argument {paramName.ToAssertString()} must not be zero."
                )
            : argument;
    }
}

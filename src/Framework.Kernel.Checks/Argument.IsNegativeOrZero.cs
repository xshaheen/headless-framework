using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is positive.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is negative or zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is positive.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNegativeOrZero<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : INumber<T>
    {
        return argument <= T.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsNegativeOrZero<T>(
        T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, INumber<T>
    {
        return argument is null ? null
            : argument <= T.Zero ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
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
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
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
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short IsNegativeOrZero(
        short argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsNegativeOrZero(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long IsNegativeOrZero(
        long argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float IsNegativeOrZero(
        float argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double IsNegativeOrZero(
        double argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero{T}(T,string,string)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal IsNegativeOrZero(
        decimal argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be positive.",
                paramName
            );
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="value"/> is not within <paramref name="delta"/> of
    /// <paramref name="target"/> (that is, <c>|value - target| &gt; delta</c>).
    /// </summary>
    /// <typeparam name="T">A numeric type.</typeparam>
    /// <param name="value">The argument to check.</param>
    /// <param name="target">The value to compare against.</param>
    /// <param name="delta">The maximum allowed (inclusive) absolute distance between <paramref name="value"/> and <paramref name="target"/>. Should be non-negative.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="value" /> if it is within <paramref name="delta"/> of <paramref name="target"/>.</returns>
    /// <remarks>
    /// Use this for floating-point comparisons instead of <see cref="IsEqualTo{T}(T,T,string?,string?)"/> to avoid exact-equality
    /// pitfalls. Non-finite values (<see cref="double.NaN"/>, infinities) are treated as not close. Integer operands whose
    /// true distance exceeds the signed range of <typeparamref name="T"/> are also treated as not close (the wrapped distance
    /// is rejected), so there are no false positives at the extremes.
    /// </remarks>
    /// <exception cref="ArgumentException">if <paramref name="value" /> is not within <paramref name="delta"/> of <paramref name="target"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsCloseTo<T>(
        T value,
        T target,
        T delta,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string? paramName = null
    )
        where T : INumber<T>
    {
        return _IsWithinDelta(value, target, delta)
            ? value
            : _ThrowNotCloseTo(value, target, delta, message, paramName);
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="value"/> is within <paramref name="delta"/> of
    /// <paramref name="target"/> (that is, <c>|value - target| &lt;= delta</c>).
    /// </summary>
    /// <typeparam name="T">A numeric type.</typeparam>
    /// <param name="value">The argument to check.</param>
    /// <param name="target">The value to compare against.</param>
    /// <param name="delta">The (inclusive) absolute distance considered "close". Should be non-negative.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="value" /> if it is not within <paramref name="delta"/> of <paramref name="target"/>.</returns>
    /// <remarks>Non-finite values (<see cref="double.NaN"/>, infinities) are treated as not close, so they pass this check.</remarks>
    /// <exception cref="ArgumentException">if <paramref name="value" /> is within <paramref name="delta"/> of <paramref name="target"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotCloseTo<T>(
        T value,
        T target,
        T delta,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string? paramName = null
    )
        where T : INumber<T>
    {
        return _IsWithinDelta(value, target, delta) ? _ThrowCloseTo(value, target, delta, message, paramName) : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _IsWithinDelta<T>(T value, T target, T delta)
        where T : INumber<T>
    {
        var difference = value >= target ? value - target : target - value;

        // A negative result means the true distance overflowed T's signed range (e.g. int.MaxValue vs int.MinValue),
        // so the operands are far apart; reject it instead of treating the wrapped value as a small distance. NaN
        // differences also fail this comparison, so non-finite operands are treated as not close.
        return difference >= T.Zero && difference <= delta;
    }

    [DoesNotReturn]
    private static T _ThrowNotCloseTo<T>(T value, T target, T delta, string? message, string? paramName)
        where T : INumber<T>
    {
        throw new ArgumentException(
            message
                ?? $"The argument {paramName.ToAssertString()} = {value.ToInvariantString()} must be within {delta.ToInvariantString()} of {target.ToInvariantString()}.",
            paramName
        );
    }

    [DoesNotReturn]
    private static T _ThrowCloseTo<T>(T value, T target, T delta, string? message, string? paramName)
        where T : INumber<T>
    {
        throw new ArgumentException(
            message
                ?? $"The argument {paramName.ToAssertString()} = {value.ToInvariantString()} must not be within {delta.ToInvariantString()} of {target.ToInvariantString()}.",
            paramName
        );
    }
}

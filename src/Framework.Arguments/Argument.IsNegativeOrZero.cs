using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is positive.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is negative or zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is positive.</exception>
    [DebuggerStepThrough]
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
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is positive.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is negative or zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is positive.</exception>
    [DebuggerStepThrough]
    public static int IsNegativeOrZero(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero(int,string?,string?)"/>
    [DebuggerStepThrough]
    public static long IsNegativeOrZero(
        long argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0L
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero(int,string?,string?)"/>
    [DebuggerStepThrough]
    public static float IsNegativeOrZero(
        float argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0F
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero(int,string?,string?)"/>
    [DebuggerStepThrough]
    public static double IsNegativeOrZero(
        double argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0D
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero(int,string?,string?)"/>
    [DebuggerStepThrough]
    public static decimal IsNegativeOrZero(
        decimal argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0M
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero(int,string?,string?)"/>
    [DebuggerStepThrough]
    public static short IsNegativeOrZero(
        short argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }

    /// <inheritdoc cref="IsNegativeOrZero(int,string?,string?)"/>
    [DebuggerStepThrough]
    public static TimeSpan IsNegativeOrZero(
        TimeSpan argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument <= TimeSpan.Zero
            ? argument
            : throw new ArgumentOutOfRangeException(
                message ?? $"The argument {_AssertString(paramName)} cannot be positive.",
                paramName
            );
    }
}

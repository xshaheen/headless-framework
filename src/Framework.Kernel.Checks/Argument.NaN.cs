using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument"/> is NaN.</summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not a NaN.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="argument" /> is NaN.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotNaN<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IFloatingPointIeee754<T>
    {
        return T.IsNaN(argument)
            ? throw new ArgumentException(
                message ?? $"The argument {paramName.ToAssertString()} cannot be NaN.",
                paramName
            )
            : argument;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument"/> is not a NaN.</summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is NaN.</returns>
    /// <exception cref="ArgumentException"><paramref name="argument" /> if the value is NaN.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNaN<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IFloatingPointIeee754<T>
    {
        return T.IsNaN(argument)
            ? argument
            : throw new ArgumentException(
                message ?? $"The argument {paramName.ToAssertString()} must be a NaN.",
                paramName
            );
    }
}

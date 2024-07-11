using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>Throws an <see cref="InvalidEnumArgumentException" /> if <paramref name="argument"/> is not a valid enum value.</summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not out of range.</returns>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="argument" /> if the value is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsInEnum<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, Enum
    {
        if (Enum.IsDefined(typeof(T), argument))
        {
            return argument;
        }

        if (string.IsNullOrEmpty(message))
        {
            throw new InvalidEnumArgumentException(
                paramName,
                Convert.ToInt32(argument, CultureInfo.InvariantCulture),
                typeof(T)
            );
        }

#pragma warning disable MA0015
        throw new InvalidEnumArgumentException(message);
#pragma warning restore MA0015
    }

    /// <inheritdoc cref="IsInEnum{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsInEnum<T>(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, Enum
    {
        if (Enum.IsDefined(typeof(T), argument))
        {
            return argument;
        }

        if (string.IsNullOrEmpty(message))
        {
            throw new InvalidEnumArgumentException(
                paramName,
                Convert.ToInt32(argument, CultureInfo.InvariantCulture),
                typeof(T)
            );
        }

#pragma warning disable MA0015
        throw new InvalidEnumArgumentException(message);
#pragma warning restore MA0015
    }
}

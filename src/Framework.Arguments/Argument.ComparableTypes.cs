using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Framework.Arguments;

// ReSharper disable PossibleMultipleEnumeration
public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not equal to <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is not equal to <paramref name="expected"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is not equal to <paramref name="expected"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsEqualTo<T>(
        T argument,
        T expected,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IComparable, IComparable<T>
    {
        if (argument.CompareTo(expected) == 0)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            message
                ?? $"Expected argument {_AssertString(paramName)} to be equal to {expected._ToInvariantString()}, but found {argument._ToInvariantString()}.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not less than or equal to <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is less than or equal to <paramref name="expected"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is not less than or equal to <paramref name="expected"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsLessThanOrEqualTo<T>(
        T argument,
        T expected,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IComparable, IComparable<T>
    {
        if (argument.CompareTo(expected) <= 0)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            message
                ?? $"Expected argument {_AssertString(paramName)} to be less than or equal to {expected._ToInvariantString()}.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not greater than or equal to <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is greater than or equal to <paramref name="expected"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is not greater than or equal to <paramref name="expected"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsGreaterThanOrEqualTo<T>(
        T argument,
        T expected,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IComparable, IComparable<T>
    {
        if (argument.CompareTo(expected) >= 0)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            message
                ?? $"Expected argument {_AssertString(paramName)} to be greater than or equal to {expected._ToInvariantString()}.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not less than <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is less than <paramref name="expected"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is not less than <paramref name="expected"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsLessThan<T>(
        T argument,
        T expected,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IComparable, IComparable<T>
    {
        if (argument.CompareTo(expected) < 0)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            message ?? $"Expected argument {_AssertString(paramName)} to be less than {expected._ToInvariantString()}.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not greater than <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the argument is greater than <paramref name="expected"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is not greater than <paramref name="expected"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsGreaterThan<T>(
        T argument,
        T expected,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IComparable, IComparable<T>
    {
        if (argument.CompareTo(expected) > 0)
        {
            return argument;
        }

        throw new ArgumentOutOfRangeException(
            message
                ?? $"Expected argument {_AssertString(paramName)} to be greater than {expected._ToInvariantString()}.",
            paramName
        );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="minimumValue">The minimum value of the range.</param>
    /// <param name="maximumValue">The maximum value of the range.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="minimumValueParamName"></param>
    /// <param name="maximumValueParamName"></param>
    /// <exception cref="ArgumentException">if the <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidRange<T>(
        T minimumValue,
        T maximumValue,
        string? message = null,
        [CallerArgumentExpression(nameof(minimumValue))] string? minimumValueParamName = null,
        [CallerArgumentExpression(nameof(maximumValue))] string? maximumValueParamName = null
    )
        where T : IComparable, IComparable<T>
    {
        if (minimumValue.CompareTo(maximumValue) > 0)
        {
            throw new ArgumentException(
                message ?? $"{minimumValueParamName} should be less or equal than {maximumValueParamName}",
                minimumValueParamName
            );
        }
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than <paramref name="minimumValue"/> or greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentParamName"></param>
    /// <param name="minimumValueParamName"></param>
    /// <param name="maximumValueParamName"></param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> if the value is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsInclusiveBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentParamName = null,
        [CallerArgumentExpression(nameof(minimumValue))] string? minimumValueParamName = null,
        [CallerArgumentExpression(nameof(maximumValue))] string? maximumValueParamName = null
    )
        where T : IComparable, IComparable<T>
    {
        ValidRange(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        return argument.CompareTo(minimumValue) >= 0 && argument.CompareTo(maximumValue) <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                argumentParamName,
                message ?? $"Input {argumentParamName} was out of range"
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than or equal to <paramref name="minimumValue"/> or greater than or equal to <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentParamName"></param>
    /// <param name="minimumValueParamName"></param>
    /// <param name="maximumValueParamName"></param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> if the value is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsExclusiveBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentParamName = null,
        [CallerArgumentExpression(nameof(minimumValue))] string? minimumValueParamName = null,
        [CallerArgumentExpression(nameof(maximumValue))] string? maximumValueParamName = null
    )
        where T : IComparable, IComparable<T>
    {
        ValidRange(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        return argument.CompareTo(minimumValue) > 0 && argument.CompareTo(maximumValue) < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                argumentParamName,
                message ?? $"Input {argumentParamName} was out of range"
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than <paramref name="minimumValue"/> or greater than or equal to <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentParamName"></param>
    /// <param name="minimumValueParamName"></param>
    /// <param name="maximumValueParamName"></param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> if the value is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsLeftOpenedBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentParamName = null,
        [CallerArgumentExpression(nameof(minimumValue))] string? minimumValueParamName = null,
        [CallerArgumentExpression(nameof(maximumValue))] string? maximumValueParamName = null
    )
        where T : IComparable, IComparable<T>
    {
        ValidRange(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        return argument.CompareTo(minimumValue) > 0 && argument.CompareTo(maximumValue) <= 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                argumentParamName,
                message ?? $"Input {argumentParamName} was out of range"
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than or equal to <paramref name="minimumValue"/> or greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentParamName"></param>
    /// <param name="minimumValueParamName"></param>
    /// <param name="maximumValueParamName"></param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argument" /> if the value is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsRightOpenedBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentParamName = null,
        [CallerArgumentExpression(nameof(minimumValue))] string? minimumValueParamName = null,
        [CallerArgumentExpression(nameof(maximumValue))] string? maximumValueParamName = null
    )
        where T : IComparable, IComparable<T>
    {
        ValidRange(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        return argument.CompareTo(minimumValue) >= 0 && argument.CompareTo(maximumValue) < 0
            ? argument
            : throw new ArgumentOutOfRangeException(
                argumentParamName,
                message ?? $"Input {argumentParamName} was out of range"
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if  any <paramref name="argument"/>'s item is less than
    /// <paramref name="minimumValue"/> or greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentParamName"></param>
    /// <param name="minimumValueParamName"></param>
    /// <param name="maximumValueParamName"></param>
    /// <returns><paramref name="argument" /> if any item is not out of range.</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HaveAllItemsInRange<T>(
        IEnumerable<T> argument,
        T minimumValue,
        T maximumValue,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentParamName = null,
        [CallerArgumentExpression(nameof(minimumValue))] string? minimumValueParamName = null,
        [CallerArgumentExpression(nameof(maximumValue))] string? maximumValueParamName = null
    )
        where T : IComparable, IComparable<T>
    {
        ValidRange(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        if (!argument.Any(x => x.CompareTo(minimumValue) < 0 || x.CompareTo(maximumValue) > 0))
        {
            return argument;
        }

        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentOutOfRangeException(
                argumentParamName,
                message ?? $"Argument {argumentParamName} had out of range item(s)"
            );
        }

        throw new ArgumentOutOfRangeException(message);
    }

    [return: NotNullIfNotNull(nameof(obj))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? _ToInvariantString(this object? obj)
    {
        return obj switch
        {
            null => null,
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
            IConvertible c => c.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => obj.ToString(),
        };
    }
}

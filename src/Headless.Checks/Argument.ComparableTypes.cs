// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

// ReSharper disable PossibleMultipleEnumeration
public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not less than or equal to <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is less than or equal to <paramref name="expected"/>.</returns>
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
        if (argument.CompareTo(expected) > 0)
        {
            _ThrowForIsLessThanOrEqualTo(expected, message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not greater than or equal to <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is greater than or equal to <paramref name="expected"/>.</returns>
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
        if (argument.CompareTo(expected) < 0)
        {
            _ThrowForIsGreaterThanOrEqualTo(expected, message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not less than <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is less than <paramref name="expected"/>.</returns>
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
        if (argument.CompareTo(expected) >= 0)
        {
            _ThrowForIsLessThan(expected, message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument" /> is not greater than <paramref name="expected"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value to compare with.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is greater than <paramref name="expected"/>.</returns>
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
        if (argument.CompareTo(expected) <= 0)
        {
            _ThrowForIsGreaterThan(expected, message, paramName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="minimumValue">The minimum value of the range.</param>
    /// <param name="maximumValue">The maximum value of the range.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="minimumValueParamName">Parameter name for <paramref name="minimumValue"/> (auto generated).</param>
    /// <param name="maximumValueParamName">Parameter name for <paramref name="maximumValue"/> (auto generated).</param>
    /// <exception cref="ArgumentException">if the <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Range<T>(
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
            _ThrowForRange(message, minimumValueParamName, maximumValueParamName);
        }
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than <paramref name="minimumValue"/> or greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="argumentParamName">Parameter name for <paramref name="argument"/> (auto generated).</param>
    /// <param name="minimumValueParamName">Parameter name for <paramref name="minimumValue"/> (auto generated).</param>
    /// <param name="maximumValueParamName">Parameter name for <paramref name="maximumValue"/> (auto generated).</param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentException">if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is out of range.</exception>
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
        Range(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        if (argument.CompareTo(minimumValue) < 0 || argument.CompareTo(maximumValue) > 0)
        {
            _ThrowForIsInclusiveBetween(argument, minimumValue, maximumValue, message, argumentParamName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than or equal to <paramref name="minimumValue"/> or greater than or equal to <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="argumentParamName">Parameter name for <paramref name="argument"/> (auto generated).</param>
    /// <param name="minimumValueParamName">Parameter name for <paramref name="minimumValue"/> (auto generated).</param>
    /// <param name="maximumValueParamName">Parameter name for <paramref name="maximumValue"/> (auto generated).</param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentException">if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is out of range.</exception>
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
        Range(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        if (argument.CompareTo(minimumValue) <= 0 || argument.CompareTo(maximumValue) >= 0)
        {
            _ThrowForIsExclusiveBetween(argument, minimumValue, maximumValue, message, argumentParamName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than <paramref name="minimumValue"/> or greater than or equal to <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="argumentParamName">Parameter name for <paramref name="argument"/> (auto generated).</param>
    /// <param name="minimumValueParamName">Parameter name for <paramref name="minimumValue"/> (auto generated).</param>
    /// <param name="maximumValueParamName">Parameter name for <paramref name="maximumValue"/> (auto generated).</param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentException">if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is out of range.</exception>
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
        Range(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        if (argument.CompareTo(minimumValue) <= 0 || argument.CompareTo(maximumValue) > 0)
        {
            _ThrowForIsLeftOpenedBetween(argument, minimumValue, maximumValue, message, argumentParamName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="argument"/>
    /// is less than or equal to <paramref name="minimumValue"/> or greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="argumentParamName">Parameter name for <paramref name="argument"/> (auto generated).</param>
    /// <param name="minimumValueParamName">Parameter name for <paramref name="minimumValue"/> (auto generated).</param>
    /// <param name="maximumValueParamName">Parameter name for <paramref name="maximumValue"/> (auto generated).</param>
    /// <returns><paramref name="argument" /> if the value is in range.</returns>
    /// <exception cref="ArgumentException">if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> is out of range.</exception>
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
        Range(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        if (argument.CompareTo(minimumValue) < 0 || argument.CompareTo(maximumValue) >= 0)
        {
            _ThrowForIsRightOpenedBetween(argument, minimumValue, maximumValue, message, argumentParamName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if  any <paramref name="argument"/>'s item is less than
    /// <paramref name="minimumValue"/> or greater than <paramref name="maximumValue"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minimumValue">The minimum valid value of the range.</param>
    /// <param name="maximumValue">The maximum valid value of the range.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="argumentParamName">Parameter name for <paramref name="argument"/> (auto generated).</param>
    /// <param name="minimumValueParamName">Parameter name for <paramref name="minimumValue"/> (auto generated).</param>
    /// <param name="maximumValueParamName">Parameter name for <paramref name="maximumValue"/> (auto generated).</param>
    /// <returns><paramref name="argument" /> if every item is within range.</returns>
    /// <exception cref="ArgumentException">if <paramref name="minimumValue"/> is greater than <paramref name="maximumValue"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if any item in <paramref name="argument" /> is out of range.</exception>
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
        Range(minimumValue, maximumValue, message: null, minimumValueParamName, maximumValueParamName);

        if (argument.Any(x => x.CompareTo(minimumValue) < 0 || x.CompareTo(maximumValue) > 0))
        {
            _ThrowForHaveAllItemsInRange(minimumValue, maximumValue, message, argumentParamName);
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsLessThanOrEqualTo<T>(T expected, string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? $"The argument {paramName.ToAssertString()} must be less than or equal to {expected.ToInvariantString()}."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsGreaterThanOrEqualTo<T>(T expected, string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? $"The argument {paramName.ToAssertString()} must be greater than or equal to {expected.ToInvariantString()}."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsLessThan<T>(T expected, string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} must be less than {expected.ToInvariantString()}."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsGreaterThan<T>(T expected, string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message ?? $"The argument {paramName.ToAssertString()} must be greater than {expected.ToInvariantString()}."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForRange(string? message, string? minimumValueParamName, string? maximumValueParamName)
    {
        throw new ArgumentException(
            message
                ?? $"The argument {minimumValueParamName.ToAssertString()} should be less or equal than {maximumValueParamName.ToAssertString()}.",
            minimumValueParamName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsInclusiveBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message,
        string? argumentParamName
    )
    {
        throw new ArgumentOutOfRangeException(
            argumentParamName,
            message
                ?? $"The argument {argumentParamName} = {argument.ToInvariantString()} must be between {minimumValue.ToInvariantString()} and {maximumValue.ToInvariantString()} inclusively ({minimumValue.ToInvariantString()}, {maximumValue.ToInvariantString()})."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsExclusiveBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message,
        string? argumentParamName
    )
    {
        throw new ArgumentOutOfRangeException(
            argumentParamName,
            message
                ?? $"The argument {argumentParamName} = {argument.ToInvariantString()} must be between {minimumValue.ToInvariantString()} and {maximumValue.ToInvariantString()} exclusively ({minimumValue.ToInvariantString()}, {maximumValue.ToInvariantString()})."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsLeftOpenedBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message,
        string? argumentParamName
    )
    {
        throw new ArgumentOutOfRangeException(
            argumentParamName,
            message
                ?? $"The argument {argumentParamName} = {argument.ToInvariantString()} must be between {minimumValue.ToInvariantString()} and {maximumValue.ToInvariantString()} ({minimumValue.ToInvariantString()}, {maximumValue.ToInvariantString()}]."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsRightOpenedBetween<T>(
        T argument,
        T minimumValue,
        T maximumValue,
        string? message,
        string? argumentParamName
    )
    {
        throw new ArgumentOutOfRangeException(
            argumentParamName,
            message
                ?? $"The argument {argumentParamName} = {argument.ToInvariantString()} must be between {minimumValue.ToInvariantString()} and {maximumValue.ToInvariantString()} [{minimumValue.ToInvariantString()}, {maximumValue.ToInvariantString()})."
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHaveAllItemsInRange<T>(
        T minimumValue,
        T maximumValue,
        string? message,
        string? argumentParamName
    )
    {
        throw new ArgumentOutOfRangeException(
            argumentParamName,
            message
                ?? $"The argument {argumentParamName.ToAssertString()} had out of range item(s). (Range [{minimumValue.ToInvariantString()}, {maximumValue.ToInvariantString()}])"
        );
    }
}

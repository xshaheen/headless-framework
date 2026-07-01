// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is not exactly <paramref name="length"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="length">The required length.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is exactly <paramref name="length"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is not <paramref name="length"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasLength(
        [SystemNotNull] string? argument,
        int length,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Length == length)
        {
            return argument;
        }

        _ThrowForHasLength(message, paramName, length, argument.Length);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is less than <paramref name="minLength"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minLength">The minimum allowed length (inclusive).</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is at least <paramref name="minLength"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is less than <paramref name="minLength"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasMinLength(
        [SystemNotNull] string? argument,
        int minLength,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Length >= minLength)
        {
            return argument;
        }

        _ThrowForHasMinLength(message, paramName, minLength, argument.Length);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is greater than <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="maxLength">The maximum allowed length (inclusive).</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is at most <paramref name="maxLength"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is greater than <paramref name="maxLength"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasMaxLength(
        [SystemNotNull] string? argument,
        int maxLength,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Length <= maxLength)
        {
            return argument;
        }

        _ThrowForHasMaxLength(message, paramName, maxLength, argument.Length);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is outside the inclusive range
    /// [<paramref name="minLength"/>, <paramref name="maxLength"/>].
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minLength">The minimum allowed length (inclusive).</param>
    /// <param name="maxLength">The maximum allowed length (inclusive).</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is within range.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="minLength"/> is greater than <paramref name="maxLength"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasLengthBetween(
        [SystemNotNull] string? argument,
        int minLength,
        int maxLength,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        Range(minLength, maxLength);

        if (argument.Length >= minLength && argument.Length <= maxLength)
        {
            return argument;
        }

        _ThrowForHasLengthBetween(message, paramName, minLength, maxLength, argument.Length);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is not strictly greater than <paramref name="length"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="length">The exclusive lower bound for the length.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is greater than <paramref name="length"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is not greater than <paramref name="length"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasLengthGreaterThan(
        [SystemNotNull] string? argument,
        int length,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Length > length)
        {
            return argument;
        }

        _ThrowForHasLengthGreaterThan(message, paramName, length, argument.Length);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is not strictly less than <paramref name="length"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="length">The exclusive upper bound for the length.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is less than <paramref name="length"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is not less than <paramref name="length"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasLengthLessThan(
        [SystemNotNull] string? argument,
        int length,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Length < length)
        {
            return argument;
        }

        _ThrowForHasLengthLessThan(message, paramName, length, argument.Length);
        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its length is exactly <paramref name="length"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="length">The length the argument must not have.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its length is not <paramref name="length"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the length of <paramref name="argument" /> is <paramref name="length"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string HasLengthNotEqualTo(
        [SystemNotNull] string? argument,
        int length,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        if (argument.Length != length)
        {
            return argument;
        }

        _ThrowForHasLengthNotEqualTo(message, paramName, length);
        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForHasLength(string? message, string? paramName, int length, int actualLength)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must have a length of {length} (Actual length {actualLength})."
                )
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasMinLength(string? message, string? paramName, int minLength, int actualLength)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must have a length of at least {minLength} (Actual length {actualLength})."
                )
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasMaxLength(string? message, string? paramName, int maxLength, int actualLength)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must have a length of at most {maxLength} (Actual length {actualLength})."
                )
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasLengthBetween(
        string? message,
        string? paramName,
        int minLength,
        int maxLength,
        int actualLength
    )
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must have a length between {minLength} and {maxLength} (Actual length {actualLength})."
                )
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasLengthGreaterThan(string? message, string? paramName, int length, int actualLength)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must have a length greater than {length} (Actual length {actualLength})."
                )
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasLengthLessThan(string? message, string? paramName, int length, int actualLength)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must have a length less than {length} (Actual length {actualLength})."
                )
        );
    }

    [DoesNotReturn]
    private static void _ThrowForHasLengthNotEqualTo(string? message, string? paramName, int length)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} must not have a length of {length}."
                )
        );
    }
}

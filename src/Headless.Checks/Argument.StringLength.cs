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

        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? $"The argument {paramName.ToAssertString()} must have a length of {length} (Actual length {argument.Length})."
        );
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

        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? $"The argument {paramName.ToAssertString()} must have a length of at least {minLength} (Actual length {argument.Length})."
        );
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

        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? $"The argument {paramName.ToAssertString()} must have a length of at most {maxLength} (Actual length {argument.Length})."
        );
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

        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? $"The argument {paramName.ToAssertString()} must have a length between {minLength} and {maxLength} (Actual length {argument.Length})."
        );
    }
}

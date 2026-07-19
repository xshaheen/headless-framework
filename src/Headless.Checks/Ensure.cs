// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

/// <summary>Common runtime checks that throw exceptions upon failure.</summary>
/// <remarks>
/// The auto-captured <c>expression</c> parameter is the source text of the checked condition or value and is
/// embedded in the exception <em>message</em>; unlike <see cref="Argument"/>'s <c>paramName</c>, it is not an
/// <see cref="ArgumentException.ParamName"/> (state checks throw exceptions that carry no parameter name).
/// </remarks>
[PublicAPI]
public static class Ensure
{
    /// <summary>Throws an <see cref="InvalidOperationException"/> if <paramref name="condition"/> is <see langword="false"/>.</summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="expression">The captured text of <paramref name="condition"/> (auto generated, no need to pass it).</param>
    /// <exception cref="InvalidOperationException">if <paramref name="condition"/> is <see langword="false"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void True(
        [DoesNotReturnIf(false)] bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))] string? expression = null
    )
    {
        if (!condition)
        {
            _ThrowForTrue(message, expression);
        }
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> if <paramref name="condition"/> is <see langword="true"/>.</summary>
    /// <param name="condition">The condition that must not hold.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="expression">The captured text of <paramref name="condition"/> (auto generated, no need to pass it).</param>
    /// <exception cref="InvalidOperationException">if <paramref name="condition"/> is <see langword="true"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void False(
        [DoesNotReturnIf(true)] bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))] string? expression = null
    )
    {
        if (condition)
        {
            _ThrowForFalse(message, expression);
        }
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if <paramref name="value"/> is null. Use this for runtime state
    /// (fields, lazily-initialized members) that must be present; use <see cref="Argument.IsNotNull{T}(T,string?,string?)"/>
    /// for caller arguments.
    /// </summary>
    /// <param name="value">The value that must not be null.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="expression">The captured text of <paramref name="value"/> (auto generated, no need to pass it).</param>
    /// <returns><paramref name="value"/> if it is not null.</returns>
    /// <exception cref="InvalidOperationException">if <paramref name="value"/> is null.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: SystemNotNull]
    public static T NotNull<T>(
        [SystemNotNull] T? value,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null
    )
    {
        if (value is null)
        {
            _ThrowForNotNull(message, expression);
        }

        return value;
    }

    /// <inheritdoc cref="NotNull{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T NotNull<T>(
        [SystemNotNull] T? value,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string? expression = null
    )
        where T : struct
    {
        if (value is null)
        {
            _ThrowForNotNull(message, expression);
        }

        return value.Value;
    }

    /// <summary>Throws an <see cref="ObjectDisposedException"/> if <paramref name="disposed"/> is <see langword="true"/>.</summary>
    /// <param name="disposed">Whether the object has already been disposed.</param>
    /// <param name="disposedValue">The disposed instance; its runtime type name is used as the object name in the exception.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="expression">The captured text of <paramref name="disposed"/> (auto generated, no need to pass it); used as the object name when <paramref name="disposedValue"/> is null.</param>
    /// <exception cref="ObjectDisposedException">if <paramref name="disposed"/> is <see langword="true"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotDisposed(
        [DoesNotReturnIf(true)] bool disposed,
        object? disposedValue,
        string? message = null,
        [CallerArgumentExpression(nameof(disposed))] string? expression = null
    )
    {
        if (disposed)
        {
            _ThrowObjectDisposed(disposedValue, message, expression);
        }
    }

    [DoesNotReturn]
    private static void _ThrowForTrue(string? message, string? expression)
    {
        throw new InvalidOperationException(message ?? $"The condition {expression.ToAssertString()} must be true.");
    }

    [DoesNotReturn]
    private static void _ThrowForFalse(string? message, string? expression)
    {
        throw new InvalidOperationException(message ?? $"The condition {expression.ToAssertString()} must be false.");
    }

    [DoesNotReturn]
    private static void _ThrowForNotNull(string? message, string? expression)
    {
        throw new InvalidOperationException(message ?? $"Expected {expression.ToAssertString()} to not be null.");
    }

    [DoesNotReturn]
    private static void _ThrowObjectDisposed(object? disposedValue, string? message, string? expression)
    {
        var objectName = disposedValue?.GetType().FullName ?? expression ?? string.Empty;
        if (message != null)
        {
            throw new ObjectDisposedException(objectName, message);
        }

        throw new ObjectDisposedException(objectName);
    }
}

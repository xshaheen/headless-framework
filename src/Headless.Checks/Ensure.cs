// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

/// <summary>Common runtime checks that throw exceptions upon failure.</summary>
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
            throw new InvalidOperationException(
                message ?? $"The condition {expression.ToAssertString()} must be true."
            );
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
            throw new InvalidOperationException(
                message ?? $"The condition {expression.ToAssertString()} must be false."
            );
        }
    }

    /// <summary>Throws an <see cref="ObjectDisposedException"/> if <paramref name="disposed"/> is <see langword="true"/>.</summary>
    /// <param name="disposed">Whether the object has already been disposed.</param>
    /// <param name="disposedValue">The disposed instance; its runtime type name is used as the object name in the exception.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <exception cref="ObjectDisposedException">if <paramref name="disposed"/> is <see langword="true"/>.</exception>
    [DebuggerStepThrough]
    public static void NotDisposed([DoesNotReturnIf(true)] bool disposed, object? disposedValue, string? message = null)
    {
        if (disposed)
        {
            var objectName = disposedValue != null ? (disposedValue.GetType().FullName ?? string.Empty) : string.Empty;
            if (message != null)
            {
                throw new ObjectDisposedException(objectName, message);
            }

            throw new ObjectDisposedException(objectName);
        }
    }
}

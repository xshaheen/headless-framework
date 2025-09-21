// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Framework.Checks.Internals;

namespace Framework.Checks;

/// <summary>Common runtime checks that throw exceptions upon failure.</summary>
public static class Ensure
{
    /// <summary>Throws an <see cref="InvalidOperationException"/> if a condition is false.</summary>
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

    /// <summary>Throws an <see cref="InvalidOperationException"/> if a condition is true.</summary>
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

    /// <summary>Throws an <see cref="ObjectDisposedException"/> if a condition is false.</summary>
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

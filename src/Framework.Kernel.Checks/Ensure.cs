// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

public static partial class Ensure
{
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void True(
        [DoesNotReturnIf(false)] bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))] string? expression = null
    )
    {
        if (condition)
        {
            return;
        }

        throw new InvalidOperationException(message ?? $"The condition {expression.ToAssertString()} must be true");
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void False(
        [DoesNotReturnIf(false)] bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))] string? expression = null
    )
    {
        if (!condition)
        {
            return;
        }

        throw new InvalidOperationException(message ?? $"The condition {expression.ToAssertString()} must be false.");
    }
}

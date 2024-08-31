using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Framework.Arguments.Internals;

namespace Framework.Arguments;

public static class Ensure
{
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsTrue(
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
    public static void IsFalse(
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

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if <paramref name="condition"/> is <see langword="false"/>. Use this
    /// for argument preconditions that cannot be expressed by a dedicated guard (for example a multi-clause validity
    /// check); prefer the specific guards (<c>IsNotNull</c>, <c>Matches</c>, etc.) when one applies.
    /// </summary>
    /// <param name="condition">The argument precondition that must hold.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">
    /// The offending parameter name. Pass <c>nameof(arg)</c> explicitly; when omitted the captured text of
    /// <paramref name="condition"/> is used.
    /// </param>
    /// <exception cref="ArgumentException">if <paramref name="condition"/> is <see langword="false"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Is(
        [DoesNotReturnIf(false)] bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))] string? paramName = null
    )
    {
        if (!condition)
        {
            throw new ArgumentException(
                message ?? $"The condition {paramName.ToAssertString()} must be true.",
                paramName
            );
        }
    }
}

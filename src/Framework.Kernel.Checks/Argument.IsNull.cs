// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is not null.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is null.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? IsNull<T>(
        [JetBrainsNoEnumeration] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is not null)
        {
            throw new ArgumentNullException(
                paramName,
                message ?? $"The argument '{paramName.ToAssertString()}' must be null."
            );
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is not null.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is null.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNull<T>(
        [JetBrainsNoEnumeration] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        return argument
            ?? throw new ArgumentNullException(
                paramName,
                message ?? $"The argument '{paramName.ToAssertString()}' must be null."
            );
    }
}

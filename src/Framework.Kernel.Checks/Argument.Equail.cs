// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

public static partial class Argument
{
    /// <summary>Asserts that the input value must be the same instance as the target value.</summary>
    /// <typeparam name="T">The type of input values to compare.</typeparam>
    /// <param name="argument">The input <typeparamref name="T"/> value to test.</param>
    /// <param name="target">The target <typeparamref name="T"/> value to test for.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentName">The name of the input parameter being tested.</param>
    /// <param name="targetName">The name of the target parameter being tested.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="argument"/> is not the same instance as <paramref name="target"/>.</exception>
    /// <remarks>The method is generic to prevent using it with value types.</remarks>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsReferenceEqualTo<T>(
        T argument,
        T target,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentName = null,
        [CallerArgumentExpression(nameof(target))] string? targetName = null
    )
        where T : class
    {
        if (ReferenceEquals(argument, target))
        {
            return;
        }

        throw new ArgumentException(
            message
                ?? $"The argument {argumentName.ToAssertString()} must be the same instance as {targetName.ToAssertString()}.",
            argumentName
        );
    }

    /// <summary>Asserts that the input value must not be the same instance as the target value.</summary>
    /// <typeparam name="T">The type of input values to compare.</typeparam>
    /// <param name="argument">The input <typeparamref name="T"/> value to test.</param>
    /// <param name="target">The target <typeparamref name="T"/> value to test for.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="argumentName">The name of the input parameter being tested.</param>
    /// <param name="targetName">The name of the target parameter being tested.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="argument"/> is the same instance as <paramref name="target"/>.</exception>
    /// <remarks>The method is generic to prevent using it with value types.</remarks>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsReferenceNotEqualTo<T>(
        T argument,
        T target,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? argumentName = null,
        [CallerArgumentExpression(nameof(target))] string? targetName = null
    )
        where T : class
    {
        if (!ReferenceEquals(argument, target))
        {
            return;
        }

        throw new ArgumentException(
            message
                ?? $"The argument {argumentName.ToAssertString()} must not be the same instance as {targetName.ToAssertString()}.",
            argumentName
        );
    }
}

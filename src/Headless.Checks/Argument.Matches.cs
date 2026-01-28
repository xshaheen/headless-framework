// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if  <paramref name="argument"/> doesn't match the <paramref name="pattern"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="pattern"></param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value matches <paramref name="pattern"/>.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is not match <paramref name="pattern"/>.</exception>
    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Global
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Matches(
        string argument,
        Regex pattern,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (pattern.IsMatch(argument))
        {
            return argument;
        }

        throw new ArgumentException(
            message ?? $"Argument {paramName.ToAssertString()} was not in required format.",
            paramName
        );
    }
}

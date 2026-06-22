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
    /// <param name="pattern">The compiled regular expression the argument must match.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value matches <paramref name="pattern"/>.</returns>
    /// <remarks>
    /// The caller owns the <paramref name="pattern"/>. Matching runs with the pattern's own <see cref="Regex.MatchTimeout"/>;
    /// supply a <see cref="Regex"/> constructed with a finite timeout when the pattern or input is untrusted to avoid
    /// catastrophic-backtracking (ReDoS) hangs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> or <paramref name="pattern"/> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> does not match <paramref name="pattern"/>.</exception>
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
        IsNotNull(argument, message, paramName);
        IsNotNull(pattern);

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

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Framework.Arguments.Internals;

namespace Framework.Arguments;

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
    [return: SystemNotNull]
    public static T IsNotNull<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is null
            ? throw new ArgumentNullException(
                paramName,
                message ?? $"Required argument {paramName.ToAssertString()} was null."
            )
            : argument;
    }

    /// <summary>Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is not null.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is null.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotNull<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        return argument
            ?? throw new ArgumentNullException(
                paramName,
                message ?? $"Required argument {paramName.ToAssertString()} was null."
            );
    }
}

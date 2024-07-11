using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NoEnumeration = JetBrains.Annotations.NoEnumerationAttribute;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the argument is not null.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is null.</exception>
    [return: NotNull]
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotNull<T>(
        [NoEnumeration] [NotNull] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is null ? throw new ArgumentNullException(paramName, message) : argument;
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
        [NoEnumeration] [NotNull] T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        return argument ?? throw new ArgumentNullException(paramName, message);
    }
}

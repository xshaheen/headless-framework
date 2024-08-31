using System.Diagnostics;
using System.Runtime.CompilerServices;
using Framework.Arguments.Internals;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null.
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is an empty or white space string.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the value is not null, or an empty or white space string.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is an empty or white space string.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string IsNotNullOrWhiteSpace(
        [SystemNotNull] string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotEmpty(argument, message, paramName);

        if (_IsWhiteSpace(argument))
        {
            throw new ArgumentException(
                message ?? $"Required argument {paramName.ToAssertString()} was empty.",
                paramName
            );
        }

        return argument;
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _IsWhiteSpace(string value)
    {
        foreach (var t in value)
        {
            if (!char.IsWhiteSpace(t))
            {
                return false;
            }
        }

        return true;
    }
}

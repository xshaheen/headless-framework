using System.Diagnostics;
using System.Runtime.CompilerServices;
using Framework.Arguments.Internals;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument"/> is not one of the <paramref name="validValues"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="validValues">The valid values.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if value is not one of the <paramref name="validValues"/>.</returns>
    /// <exception cref="ArgumentException"></exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsOneOf(
        int argument,
        IReadOnlyCollection<int> validValues,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument))
        {
            return argument;
        }

        throw new ArgumentException(
            message
                ?? $"Expected {paramName.ToAssertString()} to be one of [{validValues.Aggregate("", (p, c) => p + "," + c.ToString(CultureInfo.InvariantCulture))}], but found {argument.ToString(CultureInfo.InvariantCulture)}.",
            paramName
        );
    }

    /// <inheritdoc cref="IsOneOf(int,IReadOnlyCollection{int},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long IsOneOf(
        long argument,
        IReadOnlyCollection<long> validValues,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument))
        {
            return argument;
        }

        throw new ArgumentException(
            message
                ?? $"Expected {paramName.ToAssertString()} to be one of [{validValues.Aggregate("", (p, c) => p + "," + c.ToString(CultureInfo.InvariantCulture))}], but found {argument.ToString(CultureInfo.InvariantCulture)}.",
            paramName
        );
    }

    /// <inheritdoc cref="IsOneOf(int,IReadOnlyCollection{int},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal IsOneOf(
        decimal argument,
        IReadOnlyCollection<decimal> validValues,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument))
        {
            return argument;
        }

        throw new ArgumentException(
            message
                ?? $"Expected {paramName.ToAssertString()} to be one of [{validValues.Aggregate("", (p, c) => p + "," + c.ToString(CultureInfo.InvariantCulture))}], but found {argument.ToString(CultureInfo.InvariantCulture)}.",
            paramName
        );
    }

    /// <inheritdoc cref="IsOneOf(int,IReadOnlyCollection{int},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double IsOneOf(
        double argument,
        IReadOnlyCollection<double> validValues,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument))
        {
            return argument;
        }

        throw new ArgumentException(
            message
                ?? $"Expected {paramName.ToAssertString()} to be one of [{validValues.Aggregate("", (p, c) => p + "," + c.ToString(CultureInfo.InvariantCulture))}], but found {argument.ToString(CultureInfo.InvariantCulture)}.",
            paramName
        );
    }

    /// <inheritdoc cref="IsOneOf(int,IReadOnlyCollection{int},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float IsOneOf(
        float argument,
        IReadOnlyCollection<float> validValues,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument))
        {
            return argument;
        }

        throw new ArgumentException(
            message
                ?? $"Expected {paramName.ToAssertString()} to be one of [{validValues.Aggregate("", (p, c) => p + "," + c.ToString(CultureInfo.InvariantCulture))}], but found {argument.ToString(CultureInfo.InvariantCulture)}.",
            paramName
        );
    }

    /// <inheritdoc cref="IsOneOf(int,IReadOnlyCollection{int},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string IsOneOf(
        string argument,
        IReadOnlyList<string> validValues,
        StringComparer? comparer = null,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument, comparer ?? StringComparer.Ordinal))
        {
            return argument;
        }

        throw new ArgumentException(
            message
                ?? $"Expected {paramName.ToAssertString()} was out of range to be one of [{validValues.Aggregate("", (p, c) => p + "," + c)}], but found {argument}.",
            paramName
        );
    }
}

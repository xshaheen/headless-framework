// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Framework.Checks.Internals;

namespace Framework.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument"/> is not one of the <paramref name="values"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="values">The valid values.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if value is not one of the <paramref name="values"/>.</returns>
    /// <exception cref="ArgumentException"></exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsOneOf<T>(
        T argument,
        ReadOnlySpan<T> values,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : IEquatable<T>?
    {
        if (values.Contains(argument))
        {
            return argument;
        }

        message ??= _GetDefaultOneOfMessage(paramName, argument, values);

        throw new ArgumentException(message, paramName);
    }

    /// <inheritdoc cref="IsOneOf{T}(T,System.ReadOnlySpan{T},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsOneOf<T>(
        T argument,
        HashSet<T> values,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (values.Contains(argument))
        {
            return argument;
        }

        message ??= _GetDefaultOneOfMessage(paramName, argument, values);

        throw new ArgumentException(message, paramName);
    }

    /// <inheritdoc cref="IsOneOf{T}(T,System.ReadOnlySpan{T},string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsOneOf<T>(
        T argument,
        List<T> values,
        IEqualityComparer<T>? comparer = null,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (comparer is null ? values.Contains(argument) : values.Contains(argument, comparer))
        {
            return argument;
        }

        message ??= _GetDefaultOneOfMessage(paramName, argument, CollectionsMarshal.AsSpan(values));

        throw new ArgumentException(message, paramName);
    }

    /// <inheritdoc cref="IsOneOf{T}(T,List{T},IEqualityComparer{T}?,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsOneOf<T>(
        T argument,
        IReadOnlyCollection<T> validValues,
        IEqualityComparer<T>? comparer = null,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (validValues.Contains(argument, comparer))
        {
            return argument;
        }

        throw new ArgumentException(message ?? _GetDefaultOneOfMessage(paramName, argument, validValues), paramName);
    }

    private const int _PrintableItems = 5;

    private static string _GetDefaultOneOfMessage<T>(string? paramName, T argument, ReadOnlySpan<T> values)
    {
        var sb = new StringBuilder("The argument ");

        sb.Append(paramName.ToAssertString());
        sb.Append('=');
        sb.Append(argument.ToAssertString());

        sb.Append(" must be one of [");

        var loopBoundary = Math.Min(_PrintableItems, values.Length);

        for (var i = 0; i < loopBoundary; i++)
        {
            sb.Append(values[i]);

            if (i < loopBoundary - 1)
            {
                sb.Append(',');
            }
        }

        if (values.Length > _PrintableItems)
        {
            sb.Append(",...");
        }

        sb.Append("].");

        return sb.ToString();
    }

    private static string _GetDefaultOneOfMessage<T>(string? paramName, T argument, IEnumerable<T> values)
    {
        var sb = new StringBuilder("The argument ");

        sb.Append(paramName.ToAssertString());
        sb.Append('=');
        sb.Append(argument.ToAssertString());

        sb.Append(" must be one of [");

        var i = 0;

        foreach (var value in values)
        {
            if (i >= _PrintableItems)
            {
                break;
            }

            sb.Append(value);

            if (i < _PrintableItems - 1)
            {
                sb.Append(',');
            }

            i++;
        }

        if (i >= _PrintableItems)
        {
            sb.Append(",...");
        }

        sb.Append("].");

        return sb.ToString();
    }
}

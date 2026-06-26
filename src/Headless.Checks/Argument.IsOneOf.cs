// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument"/> is not one of the <paramref name="values"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="values">The valid values.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is one of the <paramref name="values"/>.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is not one of the <paramref name="values"/>.</exception>
    // Preferred overload so a collection-expression call site — IsOneOf(x, [a, b, c]) — binds here without a cast,
    // instead of being ambiguous with the HashSet/List/IReadOnlyCollection overloads (all collection-expression
    // targets). Explicit-typed callers are unaffected: only one overload is applicable for a concrete collection.
    [OverloadResolutionPriority(1)]
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

        _ThrowForIsOneOf(message, argument, values, paramName);
        return argument;
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

        _ThrowForIsOneOf(message, argument, values, paramName);
        return argument;
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

        _ThrowForIsOneOf(message, argument, CollectionsMarshal.AsSpan(values), paramName);
        return argument;
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

        _ThrowForIsOneOf(message, argument, validValues, paramName);
        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsOneOf<T>(string? message, T argument, ReadOnlySpan<T> values, string? paramName)
    {
        throw new ArgumentException(message ?? _GetDefaultOneOfMessage(paramName, argument, values), paramName);
    }

    [DoesNotReturn]
    private static void _ThrowForIsOneOf<T>(string? message, T argument, IEnumerable<T> values, string? paramName)
    {
        throw new ArgumentException(message ?? _GetDefaultOneOfMessage(paramName, argument, values), paramName);
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

        var emitted = 0;
        var hasMore = false;

        foreach (var value in values)
        {
            if (emitted >= _PrintableItems)
            {
                hasMore = true;
                break;
            }

            if (emitted > 0)
            {
                sb.Append(',');
            }

            sb.Append(value);
            emitted++;
        }

        if (hasMore)
        {
            sb.Append(",...");
        }

        sb.Append("].");

        return sb.ToString();
    }
}

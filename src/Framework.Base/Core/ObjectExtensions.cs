// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

public static class ObjectExtensions
{
    /// <summary>
    /// Returns a string that represents the current object, using CultureInfo.InvariantCulture where possible.
    /// Dates are represented in IS0 8601.
    /// </summary>
    [JetBrainsPure]
    [SystemPure]
    [return: NotNullIfNotNull(nameof(obj))]
    public static string? ToInvariantString(this object? obj)
    {
        // Taken from Flurl, which inspired by: http://stackoverflow.com/a/19570016/62600
        return obj switch
        {
            null => null,
            DateTime dt => dt.ToString(format: "o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(format: "o", CultureInfo.InvariantCulture),
            IConvertible c => c.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(format: null, CultureInfo.InvariantCulture),
            _ => obj.ToString(),
        };
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    [JetBrainsPure]
    [SystemPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#nullable disable
    public static bool In<T>(this T item, params T[] collection)
#nullable restore
    {
        return collection.Contains(item);
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    [JetBrainsPure]
    [SystemPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool In<T>(this T item, params ReadOnlySpan<T> collection)
        where T : IEquatable<T>?
    {
        return collection.Contains(item);
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    [JetBrainsPure]
    [SystemPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(0)]
    public static bool In<T>(this T item, IEnumerable<T> collection)
    {
        return collection.Contains(item);
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    [JetBrainsPure]
    [SystemPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(5)]
    public static bool In<T>(this T item, ICollection<T> collection)
    {
        Argument.IsNotNull(collection);

        return collection.Contains(item);
    }

    /// <summary>Safely casts the specified object to the type specified through <typeparamref name="TTo"/>.</summary>
    /// <remarks>Has been introduced to allow casting objects without breaking the fluent API.</remarks>
    /// <typeparam name="TTo">The <see cref="Type"/> to cast <paramref name="subject"/> to</typeparam>
    [JetBrainsPure]
    [SystemPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TTo? As<TTo>(this object? subject)
    {
        return subject is TTo to ? to : default;
    }

    /// <summary>Directly casts the specified object to the type specified through <typeparamref name="TTo"/>.</summary>
    /// <remarks>Has been introduced to allow casting objects without breaking the fluent API.</remarks>
    /// <typeparam name="TTo">The <see cref="Type"/> to cast <paramref name="subject"/> to</typeparam>
    public static TTo Cast<TTo>(this object subject)
    {
        return (TTo)subject;
    }
}

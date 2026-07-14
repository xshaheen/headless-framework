// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extension methods for <see cref="ITuple"/> values.</summary>
[PublicAPI]
public static class HeadlessTupleExtensions
{
    /// <summary>Copies the elements of <paramref name="tuple"/>, in order, into a new object array.</summary>
    /// <typeparam name="T">The tuple type, which must implement <see cref="ITuple"/>.</typeparam>
    /// <param name="tuple">The tuple whose elements are copied.</param>
    /// <returns>
    /// A new array containing each element of <paramref name="tuple"/> boxed as <see cref="object"/>, or an empty array
    /// when the tuple has no elements.
    /// </returns>
    public static object?[] ToArray<T>(this T tuple)
        where T : ITuple
    {
        if (tuple.Length == 0)
        {
            return [];
        }

        var result = new object?[tuple.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = tuple[i];
        }

        return result;
    }
}

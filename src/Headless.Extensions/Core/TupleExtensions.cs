// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class TupleExtensions
{
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

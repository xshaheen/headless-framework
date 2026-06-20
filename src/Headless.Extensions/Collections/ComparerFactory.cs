// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Collections;

[PublicAPI]
public static class ComparerFactory
{
    /// <summary>
    /// Create a key based equality comparer implementation. Two instances are considered equal when the keys
    /// projected by <paramref name="keyGetter"/> are equal.
    /// </summary>
    /// <param name="keyGetter">A function that projects an instance to the key used for comparison.</param>
    /// <typeparam name="T">Type of the compared instances.</typeparam>
    /// <typeparam name="TKey">Type of the key.</typeparam>
    /// <returns>An <see cref="IEqualityComparer{T}"/> implementation that compares by the projected key.</returns>
    public static IEqualityComparer<T> Create<T, TKey>(Func<T, TKey> keyGetter)
    {
        return new KeyBasedEqualityComparer<T, TKey>(keyGetter);
    }

    /// <summary>
    /// Create an equality comparer implementation using a comparison function
    /// and hash code generator function.
    /// </summary>
    /// <param name="comparisonFunc">Equality comparison function used when both instances are non-null and of the same runtime type.</param>
    /// <param name="getHashCode">A function that produces the hash code for an instance.</param>
    /// <typeparam name="T">Type of the compared instances.</typeparam>
    /// <returns>An <see cref="IEqualityComparer{T}"/> implementation that compares using the supplied functions.</returns>
    public static IEqualityComparer<T> Create<T>(Func<T, T, bool> comparisonFunc, Func<T, int> getHashCode)
    {
        return new ComparisonFuncComparer<T>(comparisonFunc, getHashCode);
    }
}

file sealed class ComparisonFuncComparer<T>(Func<T, T, bool> func, Func<T, int> hashFunc) : IEqualityComparer<T>
{
    public bool Equals(T? x, T? y)
    {
        if (x is null && y is null)
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x.GetType() != y.GetType())
        {
            return false;
        }

        return func(x, y);
    }

    public int GetHashCode(T obj)
    {
        Argument.IsNotNull(obj);

        return hashFunc(obj);
    }
}

file sealed class KeyBasedEqualityComparer<T, TKey>(Func<T, TKey> keyGetter) : IEqualityComparer<T>
{
    public bool Equals(T? x, T? y)
    {
        if (x is null && y is null)
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x.GetType() != y.GetType())
        {
            return false;
        }

        return EqualityComparer<TKey>.Default.Equals(keyGetter(x), keyGetter(y));
    }

    public int GetHashCode(T obj)
    {
        var key = keyGetter(obj);

        return key is null ? 0 : key.GetHashCode();
    }
}

using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.Collections;

[PublicAPI]
public static class ComparerFactory
{
    /// <summary>
    /// Create a key based equability comparer implementation.
    /// </summary>
    /// <param name="keyGetter"></param>
    /// <typeparam name="T">Type</typeparam>
    /// <typeparam name="TKey">Type of the key.</typeparam>
    /// <returns>IEqualityComparer implementation.</returns>
    public static IEqualityComparer<T> Create<T, TKey>(Func<T, TKey> keyGetter)
    {
        return new KeyBasedEqualityComparer<T, TKey>(keyGetter);
    }

    /// <summary>
    /// Create an equability comparer implementation using comparision function
    /// and hash code generator function.
    /// </summary>
    /// <param name="comparisonFunc">Equality comparision function.</param>
    /// <param name="getHashCode"></param>
    /// <returns></returns>
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

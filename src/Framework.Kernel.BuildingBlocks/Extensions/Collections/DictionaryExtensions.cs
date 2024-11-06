// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class DictionaryExtensions
{
    [SystemPure]
    [JetBrainsPure]
    public static bool DictionaryEqual<TKey, TValue>(
        this IDictionary<TKey, TValue>? first,
        IDictionary<TKey, TValue>? second,
        IEqualityComparer<TValue>? valueComparer = null
    )
    {
        if (Equals(first, second))
        {
            return true;
        }

        if (first is null || second is null)
        {
            return false;
        }

        if (first.Count != second.Count)
        {
            return false;
        }

        valueComparer ??= EqualityComparer<TValue>.Default;

        foreach (var (key, value) in first)
        {
            if (!second.TryGetValue(key, out var secondValue))
            {
                return false;
            }

            if (!valueComparer.Equals(value, secondValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets the value associated with the specified key, or a default value if the key was not found.</summary>
    /// <param name="dictionary">The dictionary to get value from.</param>
    /// <param name="key">The key of the value to get.</param>
    /// <typeparam name="TKey">The type of keys in the <paramref name="dictionary"/>.</typeparam>
    /// <typeparam name="TValue">The type of values in the <paramref name="dictionary"/>.</typeparam>
    /// <returns>The value associated with the specified key, if the key is found; otherwise, the default value for the <typeparamref name="TValue"/> type.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var obj) ? obj : default;
    }

    /// <summary>Gets the value associated with the specified key, or a default value if the key was not found.</summary>
    /// <param name="dictionary">The dictionary to get value from.</param>
    /// <param name="key">The key of the value to get.</param>
    /// <typeparam name="TKey">The type of keys in the <paramref name="dictionary"/>.</typeparam>
    /// <typeparam name="TValue">The type of values in the <paramref name="dictionary"/>.</typeparam>
    /// <returns>The value associated with the specified key, if the key is found; otherwise, the default value for the <typeparamref name="TValue"/> type.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TValue? GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var obj) ? obj : default;
    }

    /// <summary>
    /// Gets a value from the dictionary with given key. Returns default value if can not find.
    /// </summary>
    /// <param name="dictionary">Dictionary to check and get</param>
    /// <param name="key">Key to find the value</param>
    /// <param name="factory">A factory method used to create the value if not found in the dictionary</param>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <returns>Value if found, default if can not found.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TValue GetOrAdd<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TKey, TValue> factory
    )
        where TKey : notnull
    {
        if (dictionary.TryGetValue(key, out var obj))
        {
            return obj;
        }

        return dictionary[key] = factory(key);
    }

    /// <summary>
    /// Gets a value from the dictionary with given key. Returns default value if can not find.
    /// </summary>
    /// <param name="dictionary">Dictionary to check and get</param>
    /// <param name="key">Key to find the value</param>
    /// <param name="factory">A factory method used to create the value if not found in the dictionary</param>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <returns>Value if found, default if can not found.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TValue GetOrAdd<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TValue> factory
    )
        where TKey : notnull
    {
        return dictionary.GetOrAdd(key, _ => factory());
    }
}

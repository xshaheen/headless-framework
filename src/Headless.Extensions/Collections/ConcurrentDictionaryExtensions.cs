// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Concurrent;

[PublicAPI]
public static class ConcurrentDictionaryExtensions
{
    /// <summary>Gets the value associated with the specified key, or a default value if the key was not found.</summary>
    /// <param name="dictionary">The dictionary to get value from.</param>
    /// <param name="key">The key of the value to get.</param>
    /// <typeparam name="TKey">The type of keys in the <paramref name="dictionary"/>.</typeparam>
    /// <typeparam name="TValue">The type of values in the <paramref name="dictionary"/>.</typeparam>
    /// <returns>The value associated with the specified key, if the key is found; otherwise, the default value for the <typeparamref name="TValue"/> type.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TValue? GetOrDefault<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var obj) ? obj : default;
    }
}

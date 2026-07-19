// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class DictionaryExtensions
{
    /// <summary>
    /// Determines whether two dictionaries contain the same keys mapped to equal values. Two <see langword="null"/>
    /// references are considered equal.
    /// </summary>
    /// <typeparam name="TKey">The non-null type of keys in the dictionaries.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionaries.</typeparam>
    /// <param name="first">The first dictionary to compare.</param>
    /// <param name="second">The second dictionary to compare.</param>
    /// <param name="valueComparer">The comparer used to compare values, or <see langword="null"/> to use the default comparer for <typeparamref name="TValue"/>.</param>
    /// <returns>
    /// <see langword="true"/> if both dictionaries have the same count and every key in <paramref name="first"/> maps to
    /// an equal value in <paramref name="second"/>; otherwise, <see langword="false"/>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static bool DictionaryEqual<TKey, TValue>(
        this IDictionary<TKey, TValue>? first,
        IDictionary<TKey, TValue>? second,
        IEqualityComparer<TValue>? valueComparer = null
    )
        where TKey : notnull
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
    /// Gets the value associated with <paramref name="key"/>; if the key is absent, adds <paramref name="value"/> under
    /// that key and returns it.
    /// </summary>
    /// <param name="dictionary">Dictionary to check and get</param>
    /// <param name="key">Key to find the value</param>
    /// <param name="value">The value to add if not exist.</param>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <returns>The existing value if the key is present; otherwise, the newly added <paramref name="value"/>.</returns>
    public static TValue? GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue?> dictionary, TKey key, TValue? value)
        where TKey : notnull
    {
        ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out var exist);

        if (exist)
        {
            return val;
        }

        val = value;

        return value;
    }

    /// <summary>
    /// Gets the value associated with <paramref name="key"/>; if the key is absent, creates a value with
    /// <paramref name="factory"/>, adds it under that key, and returns it.
    /// </summary>
    /// <param name="dictionary">Dictionary to check and get</param>
    /// <param name="key">Key to find the value</param>
    /// <param name="factory">A factory method used to create the value if not found in the dictionary</param>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <returns>The existing value if the key is present; otherwise, the value produced by <paramref name="factory"/>.</returns>
    public static TValue? GetOrAdd<TKey, TValue>(
        this Dictionary<TKey, TValue?> dictionary,
        TKey key,
        Func<TKey, TValue?> factory
    )
        where TKey : notnull
    {
        ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out var exist);

        if (exist)
        {
            return val;
        }

        var newValue = factory(key);

        val = newValue;

        return newValue;
    }

    /// <summary>
    /// Updates the value associated with <paramref name="key"/> using <paramref name="factory"/>, only if the key is
    /// already present. Absent keys are not added.
    /// </summary>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <param name="dictionary">The dictionary to update.</param>
    /// <param name="key">The key whose value should be updated.</param>
    /// <param name="factory">A factory that produces the new value from the key.</param>
    /// <returns><see langword="true"/> if the key was present and its value was updated; otherwise, <see langword="false"/>.</returns>
    public static bool TryUpdate<TKey, TValue>(
        this Dictionary<TKey, TValue?> dictionary,
        TKey key,
        Func<TKey, TValue?> factory
    )
        where TKey : notnull
    {
        ref var val = ref CollectionsMarshal.GetValueRefOrNullRef(dictionary, key);

        if (Unsafe.IsNullRef(ref val))
        {
            return false;
        }

        val = factory(key);

        return true;
    }

    /// <summary>
    /// Updates the value associated with <paramref name="key"/> to <paramref name="value"/>, only if the key is already
    /// present. Absent keys are not added.
    /// </summary>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <param name="dictionary">The dictionary to update.</param>
    /// <param name="key">The key whose value should be updated.</param>
    /// <param name="value">The new value to set.</param>
    /// <returns><see langword="true"/> if the key was present and its value was updated; otherwise, <see langword="false"/>.</returns>
    public static bool TryUpdate<TKey, TValue>(this Dictionary<TKey, TValue?> dictionary, TKey key, TValue? value)
        where TKey : notnull
    {
        ref var val = ref CollectionsMarshal.GetValueRefOrNullRef(dictionary, key);

        if (Unsafe.IsNullRef(ref val))
        {
            return false;
        }

        val = value;

        return true;
    }

    /// <summary>
    /// Adds the specified key/value pair to the dictionary and returns the dictionary to allow call chaining.
    /// </summary>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <param name="dictionary">The dictionary to add to.</param>
    /// <param name="key">The key of the pair to add.</param>
    /// <param name="value">The value of the pair to add.</param>
    /// <returns>The same <paramref name="dictionary"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> already exists in <paramref name="dictionary"/>.</exception>
    public static Dictionary<TKey, TValue?> AddPair<TKey, TValue>(
        this Dictionary<TKey, TValue?> dictionary,
        TKey key,
        TValue? value
    )
        where TKey : notnull
    {
        dictionary.Add(key, value);

        return dictionary;
    }

    /// <summary>
    /// Adds every pair from <paramref name="otherDictionary"/> into <paramref name="dictionary"/> and returns the target
    /// dictionary to allow call chaining.
    /// </summary>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <param name="dictionary">The target dictionary that receives the pairs.</param>
    /// <param name="otherDictionary">The dictionary whose pairs are copied.</param>
    /// <returns>The same <paramref name="dictionary"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when a key in <paramref name="otherDictionary"/> already exists in <paramref name="dictionary"/>.</exception>
    public static Dictionary<TKey, TValue?> AddDictionary<TKey, TValue>(
        this Dictionary<TKey, TValue?> dictionary,
        Dictionary<TKey, TValue?> otherDictionary
    )
        where TKey : notnull
    {
        dictionary.EnsureCapacity(dictionary.Count + otherDictionary.Count);

        foreach (var (key, value) in otherDictionary)
        {
            dictionary.AddPair(key, value);
        }

        return dictionary;
    }

    /// <summary>
    /// Adds every pair from <paramref name="otherDictionary"/> into <paramref name="dictionary"/> and returns the target
    /// dictionary to allow call chaining.
    /// </summary>
    /// <typeparam name="TKey">Type of the key</typeparam>
    /// <typeparam name="TValue">Type of the value</typeparam>
    /// <param name="dictionary">The target dictionary that receives the pairs.</param>
    /// <param name="otherDictionary">The read-only dictionary whose pairs are copied.</param>
    /// <returns>The same <paramref name="dictionary"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when a key in <paramref name="otherDictionary"/> already exists in <paramref name="dictionary"/>.</exception>
    public static Dictionary<TKey, TValue?> AddDictionary<TKey, TValue>(
        this Dictionary<TKey, TValue?> dictionary,
        ReadOnlyDictionary<TKey, TValue?> otherDictionary
    )
        where TKey : notnull
    {
        dictionary.EnsureCapacity(dictionary.Count + otherDictionary.Count);

        foreach (var (key, value) in otherDictionary)
        {
            dictionary.AddPair(key, value);
        }

        return dictionary;
    }
}

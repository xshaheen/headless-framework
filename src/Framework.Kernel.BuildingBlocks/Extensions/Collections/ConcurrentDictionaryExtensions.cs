using Framework.Kernel.Checks;

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
    [SystemPure, JetBrainsPure]
    public static TValue? GetOrDefault<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var obj) ? obj : default;
    }

    /// <summary>
    /// Attempts to update the value associated with the specified key in the <see cref="ConcurrentDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <param name="concurrentDictionary">The dictionary to update.</param>
    /// <param name="key">The key of the value to update.</param>
    /// <param name="updateValueFactory">A function to generate a new value for the key based on the current value.</param>
    /// <returns>
    /// <see langword="true"/> if the value with the specified key was updated successfully; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryUpdate<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> concurrentDictionary,
        TKey key,
        Func<TKey, TValue, TValue> updateValueFactory
    )
        where TKey : notnull
    {
        Argument.IsNotNull(key);
        Argument.IsNotNull(updateValueFactory);

        TValue? comparisonValue;
        TValue newValue;
        do
        {
            if (!concurrentDictionary.TryGetValue(key, out comparisonValue))
            {
                return false;
            }

            newValue = updateValueFactory(key, comparisonValue);
        } while (!concurrentDictionary.TryUpdate(key, newValue, comparisonValue));

        return true;
    }
}

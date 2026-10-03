// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Primitives.Internal;

namespace Headless.Primitives;

/// <summary>Extension members for reading and comparing <see cref="ExtraProperties"/> values.</summary>
[PublicAPI]
public static class ExtraPropertyExtensions
{
    extension(ExtraProperties extraProperties)
    {
        /// <summary>
        /// Reads the value at <paramref name="key"/> and returns it as the enum type <typeparamref name="T"/>,
        /// parsing it from its string representation when necessary.
        /// </summary>
        /// <typeparam name="T">The enum type to convert the stored value to.</typeparam>
        /// <param name="key">The key whose value is read.</param>
        /// <returns>
        /// The value as <typeparamref name="T"/>, or <see langword="default"/> when the stored value is
        /// <see langword="null"/> or its string representation is <see langword="null"/>.
        /// </returns>
        /// <exception cref="KeyNotFoundException">Thrown when <paramref name="key"/> is not present in the dictionary.</exception>
        /// <exception cref="ArgumentException">Thrown when the stored text does not match a defined name or value of <typeparamref name="T"/>.</exception>
        public T? ToEnum<T>(string key)
            where T : Enum
        {
            var value = extraProperties[key];

            if (value is null)
            {
                return default;
            }

            if (value.GetType() == typeof(T))
            {
                return (T)value;
            }

            var text = value.ToString();

            if (text is null)
            {
                return default;
            }

            // Do not write the parsed value back into the dictionary: ToEnum is a read accessor and a write here is not
            // thread-safe and throws "collection modified" if the bag is being enumerated concurrently.
            return (T)Enum.Parse(typeof(T), text, ignoreCase: true);
        }

        /// <summary>
        /// Determines whether this instance and <paramref name="otherDictionary"/> contain the same keys with values
        /// that are equal under the default equality comparer.
        /// </summary>
        /// <param name="otherDictionary">The dictionary to compare against.</param>
        /// <returns><see langword="true"/> if both contain the same keys and equivalent values; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the source instance or <paramref name="otherDictionary"/> is <see langword="null"/>.</exception>
        public bool HasSameItems(ExtraProperties otherDictionary)
        {
            Argument.IsNotNull(extraProperties);
            Argument.IsNotNull(otherDictionary);

            if (extraProperties.Count != otherDictionary.Count)
            {
                return false;
            }

            foreach (var key in extraProperties.Keys)
            {
                if (
                    !otherDictionary.TryGetValue(key, out var value)
                    || !EqualityComparer<object?>.Default.Equals(extraProperties[key], value)
                )
                {
                    return false;
                }
            }

            return true;
        }
    }
}

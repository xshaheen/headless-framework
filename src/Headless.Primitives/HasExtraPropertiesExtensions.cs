// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Primitives.Internal;

namespace Headless.Primitives;

/// <summary>Extension members for reading, writing, and comparing the <see cref="ExtraProperties"/> of an <see cref="IHasExtraProperties"/>.</summary>
[PublicAPI]
public static class HasExtraPropertiesExtensions
{
    extension(IHasExtraProperties source)
    {
        /// <summary>Determines whether the extra-properties bag contains a property with the given name.</summary>
        /// <param name="name">The property name to look for.</param>
        /// <returns><see langword="true"/> if a property with <paramref name="name"/> exists; otherwise, <see langword="false"/>.</returns>
        public bool HasProperty(string name)
        {
            return source.ExtraProperties.ContainsKey(name);
        }

        /// <summary>
        /// Reads the property at <paramref name="name"/> and converts it to <typeparamref name="TProperty"/>,
        /// returning <paramref name="defaultValue"/> when the property is absent or <see langword="null"/>.
        /// </summary>
        /// <typeparam name="TProperty">The primitive (or enum / <see cref="Guid"/>) type to convert the value to.</typeparam>
        /// <param name="name">The property name to read.</param>
        /// <param name="defaultValue">The value returned when the property is missing or <see langword="null"/>.</param>
        /// <returns>The converted property value, or <paramref name="defaultValue"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="TProperty"/> is not a supported primitive type.</exception>
        /// <exception cref="ArgumentException">Thrown when <typeparamref name="TProperty"/> is an enum and the stored value does not match a defined name or value.</exception>
        /// <exception cref="FormatException">Thrown when the stored value cannot be parsed into <typeparamref name="TProperty"/>.</exception>
        [RequiresUnreferencedCode("Uses TypeDescriptor which is not compatible with trimming.")]
        public TProperty? GetProperty<TProperty>(string name, TProperty? defaultValue = default)
        {
            var value = source.GetProperty(name);
            if (value == null)
            {
                return defaultValue;
            }

            if (typeof(TProperty).IsPrimitiveExtended(includeEnums: true))
            {
                var conversionType = typeof(TProperty);

                if (conversionType.IsNullableValueType())
                {
                    conversionType = conversionType.GetGenericArguments()[0];
                }

                if (conversionType == typeof(Guid))
                {
                    return (TProperty)
                        TypeDescriptor.GetConverter(conversionType).ConvertFromInvariantString(value.ToString()!)!;
                }

                if (conversionType.IsEnum)
                {
                    return (TProperty)Enum.Parse(conversionType, value.ToString()!);
                }

                return (TProperty)Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException(
                "GetProperty<TProperty> does not support non-primitive types. Use non-generic GetProperty method and handle type casting manually."
            );
        }

        /// <summary>Reads the raw property value at <paramref name="name"/> without type conversion.</summary>
        /// <param name="name">The property name to read.</param>
        /// <param name="defaultValue">The value returned when the property is missing.</param>
        /// <returns>The stored value, or <paramref name="defaultValue"/> when the property is absent.</returns>
        public object? GetProperty(string name, object? defaultValue = null)
        {
            return source.ExtraProperties.TryGetValue(name, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Copies each extra property whose name matches a (settable, possibly non-public) regular property onto that
        /// property, then removes the copied entries from the extra-properties bag.
        /// </summary>
        [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
        public void SetExtraPropertiesToRegularProperties()
        {
            var properties = source
                .GetType()
                .GetProperties()
                .Where(info =>
                    source.ExtraProperties.ContainsKey(info.Name) && info.GetSetMethod(nonPublic: true) is not null
                );

            foreach (var property in properties)
            {
                property.SetValue(source, source.ExtraProperties[property.Name]);
                source.RemoveProperty(property.Name);
            }
        }

        /// <summary>Determines whether this instance and <paramref name="other"/> have equivalent extra-properties bags.</summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns><see langword="true"/> if both bags contain the same keys and equivalent values; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the source instance or <paramref name="other"/> is <see langword="null"/>.</exception>
        public bool HasSameExtraProperties(IHasExtraProperties other)
        {
            Argument.IsNotNull(source);
            Argument.IsNotNull(other);

            return source.ExtraProperties.HasSameItems(other.ExtraProperties);
        }
    }

    extension<TSource>(TSource source)
        where TSource : IHasExtraProperties
    {
        /// <summary>Sets (or overwrites) the extra property <paramref name="name"/> to <paramref name="value"/>.</summary>
        /// <param name="name">The property name to set.</param>
        /// <param name="value">The value to store.</param>
        /// <returns>The same <paramref name="source"/> instance, to allow fluent chaining.</returns>
        public TSource SetProperty(string name, object? value)
        {
            source.ExtraProperties[name] = value;

            return source;
        }

        /// <summary>Removes the extra property <paramref name="name"/> if present.</summary>
        /// <param name="name">The property name to remove.</param>
        /// <returns>The same <paramref name="source"/> instance, to allow fluent chaining.</returns>
        public TSource RemoveProperty(string name)
        {
            source.ExtraProperties.Remove(name);

            return source;
        }
    }
}

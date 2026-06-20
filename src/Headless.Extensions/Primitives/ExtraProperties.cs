// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Reflection;

namespace Headless.Primitives;

/// <summary>
/// An ordinal-keyed string-to-object dictionary for storing arbitrary extra properties on a model
/// (for example, dynamic or extensible fields not represented by first-class properties).
/// </summary>
[PublicAPI]
public sealed class ExtraProperties : Dictionary<string, object?>
{
    /// <summary>Initializes an empty <see cref="ExtraProperties"/> using ordinal key comparison.</summary>
    public ExtraProperties()
        : base(StringComparer.Ordinal) { }

    /// <summary>Initializes an <see cref="ExtraProperties"/> seeded from an existing dictionary, using ordinal key comparison.</summary>
    /// <param name="dictionary">The dictionary whose entries are copied into the new instance.</param>
    public ExtraProperties(IDictionary<string, object?> dictionary)
        : base(dictionary, StringComparer.Ordinal) { }
}

/// <summary>Extension members for reading and comparing <see cref="ExtraProperties"/> values.</summary>
[PublicAPI]
public static class ExtraPropertyExtensions
{
    extension(ExtraProperties extraProperties)
    {
        /// <summary>
        /// Reads the value at <paramref name="key"/> and returns it as the enum type <typeparamref name="T"/>,
        /// parsing it from its string representation when necessary and caching the parsed value back into the
        /// dictionary so subsequent reads take the typed fast path.
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

            var parsed = (T)Enum.Parse(typeof(T), text, ignoreCase: true);

            // Cache the parsed enum so subsequent reads take the typed fast path above.
            extraProperties[key] = parsed;

            return parsed;
        }

        /// <summary>
        /// Determines whether this instance and <paramref name="otherDictionary"/> contain the same keys with values
        /// whose string representations are ordinally equal.
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
                    || !string.Equals(extraProperties[key]?.ToString(), value?.ToString(), StringComparison.Ordinal)
                )
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>Marks a type that carries a bag of arbitrary <see cref="ExtraProperties"/>.</summary>
[PublicAPI]
public interface IHasExtraProperties
{
    /// <summary>The bag of arbitrary extra properties associated with this instance.</summary>
    ExtraProperties ExtraProperties { get; }
}

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
            return source.ExtraProperties.TryGetValue(name, out var value) ? value : value ?? defaultValue;
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

/// <summary>
/// A <c>System.Text.Json</c> type-info modifier that makes the otherwise read-only
/// <see cref="IHasExtraProperties.ExtraProperties"/> property settable during deserialization.
/// </summary>
[PublicAPI]
public static class IncludeExtraPropertiesModifiers
{
    /// <summary>
    /// Wires up a setter for the <see cref="IHasExtraProperties.ExtraProperties"/> property on
    /// <paramref name="jsonTypeInfo"/>. Has no effect when the type does not implement
    /// <see cref="IHasExtraProperties"/> or has no matching read-only extra-properties property.
    /// </summary>
    /// <param name="jsonTypeInfo">The JSON type-info contract to modify.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonTypeInfo"/> is <see langword="null"/>.</exception>
    public static void Modify(JsonTypeInfo jsonTypeInfo)
    {
        Argument.IsNotNull(jsonTypeInfo);

        if (!typeof(IHasExtraProperties).IsAssignableFrom(jsonTypeInfo.Type))
        {
            return;
        }

        var propertyJsonInfo = jsonTypeInfo.Properties.FirstOrDefault(x =>
            x.AttributeProvider is MemberInfo memberInfo
            && x.PropertyType == typeof(ExtraProperties)
            && string.Equals(memberInfo.Name, nameof(IHasExtraProperties.ExtraProperties), StringComparison.Ordinal)
            && x.Set is null
        );

        if (propertyJsonInfo is null)
        {
            return;
        }

        propertyJsonInfo.Set = (obj, value) =>
            ObjectPropertiesHelper.TrySetProperty((IHasExtraProperties)obj, x => x.ExtraProperties, () => value);
    }
}

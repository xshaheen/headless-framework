// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Primitives.Internal;

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

            foreach (var (key, value) in extraProperties)
            {
                if (
                    !otherDictionary.TryGetValue(key, out var otherValue)
                    || !EqualityComparer<object?>.Default.Equals(value, otherValue)
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
        /// <remarks>
        /// The stored value may have a different CLR type than the one written, because a bag loaded from JSON holds
        /// what the JSON could express: strings for dates, times, <see cref="Guid"/> and <see cref="TimeSpan"/> values,
        /// <see langword="long"/>, <see langword="decimal"/> or <see langword="double"/> for numbers, or
        /// <see cref="System.Text.Json.JsonElement"/> values when deserialized without an inferring converter. Each is
        /// converted with the invariant culture. A <see cref="DateTime"/> string keeps its kind (a <c>Z</c> suffix reads
        /// back as UTC), a <see cref="DateTimeOffset"/> string without an offset is read as UTC, and enum names match
        /// case-insensitively.
        /// </remarks>
        /// <typeparam name="TProperty">The primitive, <see cref="string"/>, <see cref="decimal"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="TimeSpan"/>, <see cref="Guid"/>, or enum type to convert the value to (optionally nullable).</typeparam>
        /// <param name="name">The property name to read.</param>
        /// <param name="defaultValue">The value returned when the property is missing or <see langword="null"/>.</param>
        /// <returns>The converted property value, or <paramref name="defaultValue"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="TProperty"/> is not a supported primitive type.</exception>
        /// <exception cref="ArgumentException">Thrown when <typeparamref name="TProperty"/> is an enum and the stored value does not match a defined name or value.</exception>
        /// <exception cref="FormatException">Thrown when the stored value cannot be parsed into <typeparamref name="TProperty"/>.</exception>
        /// <exception cref="InvalidCastException">Thrown when the stored value's type cannot be converted to <typeparamref name="TProperty"/>.</exception>
        public TProperty? GetProperty<TProperty>(string name, TProperty? defaultValue = default)
        {
            var value = _Unwrap(source.GetProperty(name));

            if (value is null)
            {
                return defaultValue;
            }

            if (!typeof(TProperty).IsPrimitiveExtended(includeEnums: true))
            {
                throw new InvalidOperationException(
                    "GetProperty<TProperty> does not support non-primitive types. Use non-generic GetProperty method and handle type casting manually."
                );
            }

            var conversionType = Nullable.GetUnderlyingType(typeof(TProperty)) ?? typeof(TProperty);

            return (TProperty)_ConvertTo(value, conversionType);
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

    private static object? _Unwrap(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            // The raw text keeps every written digit; it is parsed into the requested numeric type below.
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element,
        };
    }

    private static object _ConvertTo(object value, Type conversionType)
    {
        if (conversionType.IsInstanceOfType(value))
        {
            return value;
        }

        if (conversionType == typeof(string))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        if (conversionType.IsEnum)
        {
            // Name matching ignores case because JSON options commonly write enum names camelCase.
            return value is string text
                ? Enum.Parse(conversionType, text, ignoreCase: true)
                : Enum.ToObject(conversionType, value);
        }

        if (value is string s)
        {
            return _Parse(s, conversionType);
        }

        if (conversionType == typeof(DateTimeOffset) && value is DateTime dateTime)
        {
            return new DateTimeOffset(dateTime);
        }

        return Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
    }

    private static object _Parse(string text, Type conversionType)
    {
        var culture = CultureInfo.InvariantCulture;

        if (conversionType == typeof(Guid))
        {
            return Guid.Parse(text, culture);
        }

        if (conversionType == typeof(DateTime))
        {
            return DateTime.Parse(text, culture, DateTimeStyles.RoundtripKind);
        }

        if (conversionType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(text, culture, DateTimeStyles.AssumeUniversal);
        }

        if (conversionType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(text, culture);
        }

        if (conversionType == typeof(decimal))
        {
            // Convert.ToDecimal rejects exponent notation, which JSON numbers may use.
            return decimal.Parse(text, NumberStyles.Float, culture);
        }

        return Convert.ChangeType(text, conversionType, culture);
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

        // Only wire a setter when the CLR property actually has one (including a non-public setter). This is the
        // read-only-property-during-deserialization case the modifier exists to unblock.
        if (
            propertyJsonInfo?.AttributeProvider is not PropertyInfo clrProperty
            || clrProperty.GetSetMethod(nonPublic: true) is null
        )
        {
            return;
        }

        propertyJsonInfo.Set = (obj, value) => clrProperty.SetValue(obj, value);
    }
}

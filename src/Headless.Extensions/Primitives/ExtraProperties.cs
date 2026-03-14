// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Reflection;

namespace Headless.Primitives;

[PublicAPI]
public sealed class ExtraProperties : Dictionary<string, object?>
{
    public ExtraProperties()
        : base(StringComparer.InvariantCulture) { }

    public ExtraProperties(IDictionary<string, object?> dictionary)
        : base(dictionary, StringComparer.InvariantCulture) { }
}

[PublicAPI]
public static class ExtraPropertyExtensions
{
    extension(ExtraProperties extraProperties)
    {
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

            extraProperties[key] = Enum.Parse(typeof(T), text, ignoreCase: true);

            return (T)value;
        }

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

[PublicAPI]
public interface IHasExtraProperties
{
    ExtraProperties ExtraProperties { get; }
}

[PublicAPI]
public static class HasExtraPropertiesExtensions
{
    extension(IHasExtraProperties source)
    {
        public bool HasProperty(string name)
        {
            return source.ExtraProperties.ContainsKey(name);
        }

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

        public object? GetProperty(string name, object? defaultValue = null)
        {
            return source.ExtraProperties.TryGetValue(name, out var value) ? value : value ?? defaultValue;
        }

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
        public TSource SetProperty(string name, object? value)
        {
            source.ExtraProperties[name] = value;

            return source;
        }

        public TSource RemoveProperty(string name)
        {
            source.ExtraProperties.Remove(name);

            return source;
        }
    }
}

[PublicAPI]
public static class IncludeExtraPropertiesModifiers
{
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

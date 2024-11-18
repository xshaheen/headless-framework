// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Framework.Checks;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

[PublicAPI]
public static class HasExtraPropertiesExtensions
{
    public static bool HasProperty(this IHasExtraProperties source, string name)
    {
        return source.ExtraProperties.ContainsKey(name);
    }

    public static TProperty? GetProperty<TProperty>(
        this IHasExtraProperties source,
        string name,
        TProperty? defaultValue = default
    )
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

    public static object? GetProperty(this IHasExtraProperties source, string name, object? defaultValue = null)
    {
        return source.ExtraProperties.TryGetValue(name, out var value) ? value : value ?? defaultValue;
    }

    public static TSource SetProperty<TSource>(this TSource source, string name, object? value)
        where TSource : IHasExtraProperties
    {
        source.ExtraProperties[name] = value;

        return source;
    }

    public static TSource RemoveProperty<TSource>(this TSource source, string name)
        where TSource : IHasExtraProperties
    {
        source.ExtraProperties.Remove(name);

        return source;
    }

    public static void SetExtraPropertiesToRegularProperties(this IHasExtraProperties source)
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

    public static bool HasSameExtraProperties(this IHasExtraProperties source, IHasExtraProperties other)
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(other);

        return source.ExtraProperties.HasSameItems(other.ExtraProperties);
    }
}

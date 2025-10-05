// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Framework.Serializer;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class ToObjectExtensions
{
    /// <summary>Converts given an object to a value type using <see cref="Convert.ChangeType(object,Type)"/> method.</summary>
    /// <param name="obj">Object to be converted</param>
    /// <typeparam name="T">Type of the target object</typeparam>
    /// <returns>Converted object</returns>
    public static T? To<T>(this object? obj, JsonSerializerOptions? options = null)
    {
        if (obj is null)
        {
            return default;
        }

        var text = obj.ToString();
        Debug.Assert(text is not null);

        var baseType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(baseType) ?? baseType;

        if (underlyingType == typeof(Guid) || underlyingType == typeof(string))
        {
            return (T?)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text);
        }

        if (underlyingType.IsEnum)
        {
            return (T?)Enum.Parse(underlyingType, text);
        }

        return obj switch
        {
            IConvertible => (T)Convert.ChangeType(obj, underlyingType, CultureInfo.InvariantCulture),
            JsonElement element => element.Deserialize<T>(options ?? JsonConstants.DefaultInternalJsonOptions),
            JsonDocument document => document.Deserialize<T>(options ?? JsonConstants.DefaultInternalJsonOptions),
            JsonNode node => node.Deserialize<T>(options ?? JsonConstants.DefaultInternalJsonOptions),
            _ => (T)obj,
        };
    }
}

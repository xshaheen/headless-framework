// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Framework.Constants;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class BuildBlocksObjectExtensions
{
    /// <summary>
    /// Converts the value of a specified type into a JSON string.
    /// </summary>
    /// <param name="obj">The value to convert.</param>
    /// <param name="options">Options to control the conversion behavior.</param>
    /// <returns>The JSON string representation of the value.</returns>
    public static string ToJson(this object obj, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(obj, options ?? FrameworkJsonConstants.DefaultInternalJsonOptions);
    }

    /// <summary>
    /// Converts the value of a specified object into a JSON string and return
    /// byte representation of the string.
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static byte[] ToBytes(this object obj)
    {
        return Encoding.Default.GetBytes(obj.ToJson());
    }

    /// <summary>
    /// Converts the value of a specified object into a JSON string and return
    /// byte representation of the string.
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    public static byte[] ToBytes(this object obj, JsonSerializerOptions options)
    {
        return Encoding.Default.GetBytes(obj.ToJson(options));
    }

    /// <summary>Converts given an object to a value type using <see cref="Convert.ChangeType(object,Type)"/> method.</summary>
    /// <param name="obj">Object to be converted</param>
    /// <typeparam name="T">Type of the target object</typeparam>
    /// <returns>Converted object</returns>
    public static T? To<T>(this object? obj)
    {
        if (obj is null || (obj is string && string.IsNullOrWhiteSpace(obj.ToString())))
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
            JsonElement element => element.Deserialize<T>(FrameworkJsonConstants.DefaultInternalJsonOptions),
            JsonDocument document => document.Deserialize<T>(FrameworkJsonConstants.DefaultInternalJsonOptions),
            JsonNode node => node.Deserialize<T>(FrameworkJsonConstants.DefaultInternalJsonOptions),
            _ => (T)obj,
        };
    }
}

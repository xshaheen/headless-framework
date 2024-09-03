using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class ObjectExtensions
{
    /// <summary>
    /// Converts the value of a specified type into a JSON string.
    /// </summary>
    /// <param name="obj">The value to convert.</param>
    /// <param name="options">Options to control the conversion behavior.</param>
    /// <returns>The JSON string representation of the value.</returns>
    public static string ToJson(this object obj, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(obj, options ?? PlatformJsonConstants.DefaultInternalJsonOptions);
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

    /// <summary>
    /// Returns a string that represents the current object, using CultureInfo.InvariantCulture where possible.
    /// Dates are represented in IS0 8601.
    /// </summary>
    [return: NotNullIfNotNull(nameof(obj))]
    public static string? ToInvariantString(this object? obj)
    {
        // Taken from Flurl which inspired by: http://stackoverflow.com/a/19570016/62600
        return obj switch
        {
            null => null,
            DateTime dt => dt.ToString(format: "o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(format: "o", CultureInfo.InvariantCulture),
            IConvertible c => c.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(format: null, CultureInfo.InvariantCulture),
            _ => obj.ToString(),
        };
    }

    /// <summary>
    /// Can be used to conditionally perform an action on an object and return the original object.
    /// It is useful for chained calls on the object.
    /// </summary>
    /// <param name="obj">An object</param>
    /// <param name="condition">A condition</param>
    /// <param name="action">An action that is executed only if the condition is <see langword="true"/></param>
    /// <typeparam name="T">Type of the object</typeparam>
    /// <returns>
    /// Returns the original object.
    /// </returns>
    public static T If<T>(this T obj, bool condition, Action<T> action)
    {
        if (condition)
        {
            action(obj);
        }

        return obj;
    }

    /// <summary>
    /// Can be used to conditionally perform a function on an object and return the modified or the
    /// original object.
    /// It is useful for chained calls.
    /// </summary>
    /// <param name="obj">An object</param>
    /// <param name="condition">A condition</param>
    /// <param name="func">A function that is executed only if the condition is <see langword="true"/></param>
    /// <typeparam name="T">Type of the object</typeparam>
    /// <returns>
    /// Returns the modified object (by the <paramref name="func"/> if the <paramref name="condition"/> is
    /// <see langword="true"/>)
    /// or the original object if the <paramref name="condition"/> is <see langword="false"/>
    /// </returns>
    public static T If<T>(this T obj, bool condition, Func<T, T> func)
    {
        return condition ? func(obj) : obj;
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    public static bool In<T>(this T item, IEnumerable<T> collection)
    {
        return collection.Contains(item);
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    public static bool In<T>(this T item, ICollection<T> collection)
    {
        return collection.Contains(item);
    }

    /// <summary>Check if an item is in a list.</summary>
    /// <param name="item">Item to check</param>
    /// <param name="collection">List of items</param>
    /// <typeparam name="T">Type of the items</typeparam>
    public static bool In<T>(this T item, params T[] collection)
    {
        return collection.Contains(item);
    }

    /// <summary>
    /// Used to simplify and beautify casting an object to a type.
    /// </summary>
    /// <typeparam name="T">Type to be casted</typeparam>
    /// <param name="obj">Object to cast</param>
    /// <returns>Casted object</returns>
    public static T As<T>(this object obj)
    {
        return (T)obj;
    }

    /// <summary>
    /// Converts given object to a value type using <see cref="Convert.ChangeType(object,Type)"/> method.
    /// </summary>
    /// <param name="obj">Object to be converted</param>
    /// <typeparam name="T">Type of the target object</typeparam>
    /// <returns>Converted object</returns>
    public static T? To<T>(this object? obj)
    {
        if (obj is null)
        {
            return default;
        }

        if (obj is string && string.IsNullOrWhiteSpace(obj.ToString()))
        {
            return default;
        }

        var text = obj.ToString();
        Debug.Assert(text is not null, nameof(text) + " is not null");

        var baseType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(baseType) ?? baseType;

        if (underlyingType == typeof(Guid) || underlyingType == typeof(string))
        {
            return (T?)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text);
        }

        if (obj is IConvertible)
        {
            return (T)Convert.ChangeType(obj, baseType, CultureInfo.InvariantCulture);
        }

        if (underlyingType.IsEnum)
        {
            return (T?)Enum.Parse(underlyingType, text);
        }

        if (obj is JsonElement element)
        {
            return element.Deserialize<T>(PlatformJsonConstants.DefaultInternalJsonOptions);
        }

        return (T)obj;
    }
}

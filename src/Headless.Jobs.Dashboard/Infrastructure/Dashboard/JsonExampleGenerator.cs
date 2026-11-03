// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;

namespace Headless.Jobs.Infrastructure.Dashboard;

internal static class JsonExampleGenerator
{
    private static object? _GenerateExample(Type type)
    {
        return _Generate(type);
    }

    private static object? _Generate(Type type)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            return _Generate(underlyingType);
        }

        // Handle primitive types
        if (type.IsPrimitive || type == typeof(string))
        {
            return _GetDefaultValue(type);
        }

        // Handle arrays
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            var array = Array.CreateInstance(elementType!, 1);
            array.SetValue(_Generate(elementType!), 0);
            return array;
        }

        // Handle generic lists (List<T>)
        if (
            (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
        )
        {
            var elementType = type.GetGenericArguments()[0];
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            list.Add(_Generate(elementType));
            return list;
        }

        // Handle complex objects
        if (type.IsClass || type.IsValueType)
        {
            var instance = Activator.CreateInstance(type)!;
            foreach (var property in type.GetProperties())
            {
                if (property.CanWrite)
                {
                    var value = _Generate(property.PropertyType);
                    property.SetValue(instance, value);
                }
            }

            return instance;
        }

        return _GetDefaultValue(type);
    }

    private static object? _GetDefaultValue(Type type)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => true,
            TypeCode.Byte => (byte)1,
            TypeCode.Char => 'a',
            TypeCode.DateTime => new DateTime(2023, 1, 1),
            TypeCode.DBNull => DBNull.Value,
            TypeCode.Decimal => 123.45m,
            TypeCode.Double => 123.45,
            TypeCode.Empty => null,
            TypeCode.Int16 => (short)1,
            TypeCode.Int32 => 123,
            TypeCode.Int64 => 123L,
            TypeCode.Object => Activator.CreateInstance(type),
            TypeCode.SByte => (sbyte)1,
            TypeCode.Single => 123.45f,
            TypeCode.String => "string",
            TypeCode.UInt16 => (ushort)1,
            TypeCode.UInt32 => 123u,
            TypeCode.UInt64 => 123ul,
            _ => Activator.CreateInstance(type),
        };
    }

    // The example is written with the host's request options, the ones the job deserializes its payload with: an
    // operator submits it verbatim, so a naming policy or converter it ignored would bind nothing and the job would run
    // on a defaulted request. It is re-indented afterwards for display, which keeps every name and value as written.
    private static string _GenerateExampleJson(Type type, JsonSerializerOptions requestOptions)
    {
        var element = JsonSerializer.SerializeToElement(_GenerateExample(type), type, requestOptions);
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Example JSON depends only on the request Type's shape and the request options, so it is computed once per pair
    // instead of once per dashboard HTTP request. An empty string is never a successful serialization, so it doubles
    // as the cached failure marker and a Type that cannot be exemplified is not retried per request.
    private static readonly ConcurrentDictionary<(Type, JsonSerializerOptions), string> _ExampleJsonCache = new();

    public static bool TryGenerateExampleJson(Type type, JsonSerializerOptions requestOptions, out string json)
    {
        json = _ExampleJsonCache.GetOrAdd(
            (type, requestOptions),
            static key =>
            {
                return _TryGenerateExampleJson(key.Item1, key.Item2, out var example) ? example : string.Empty;
            }
        );

        return json.Length != 0;
    }

    private static bool _TryGenerateExampleJson(Type type, JsonSerializerOptions requestOptions, out string json)
    {
        try
        {
            json = _GenerateExampleJson(type, requestOptions);
            return true;
        }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
        catch (Exception)
        {
            json = string.Empty;
            return false;
        }
#pragma warning restore ERP022
    }
}

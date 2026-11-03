// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;

namespace Headless.Jobs.Infrastructure.Dashboard;

internal static class JsonExampleGenerator
{
    // A request type may reference itself, directly or through another type (a tree node with a list of child
    // nodes). Recursion here runs on the dashboard request thread, and a stack overflow cannot be caught, so it would
    // end the host. The example therefore stops at a type already being built on the current path and leaves that
    // member unset or its collection empty. The depth cap bounds generic shapes that never repeat an exact type
    // (Node<T> holding Node<List<T>>).
    private const int _MaxDepth = 16;

    private static object? _GenerateExample(Type type)
    {
        return _Generate(type, []);
    }

    private static object? _Generate(Type type, HashSet<Type> path)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            return _Generate(underlyingType, path);
        }

        // Handle primitive types
        if (type.IsPrimitive || type == typeof(string))
        {
            return _GetDefaultValue(type);
        }

        // Handle arrays
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var element = _Generate(elementType, path);
            var array = Array.CreateInstance(elementType, element is null ? 0 : 1);

            if (element is not null)
            {
                array.SetValue(element, 0);
            }

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
            var element = _Generate(elementType, path);

            if (element is not null)
            {
                list.Add(element);
            }

            return list;
        }

        // Handle complex objects
        if (type.IsClass || type.IsValueType)
        {
            if (path.Count >= _MaxDepth || !path.Add(type))
            {
                return null;
            }

            try
            {
                var instance = Activator.CreateInstance(type)!;
                foreach (var property in type.GetProperties())
                {
                    if (property.CanWrite)
                    {
                        var value = _Generate(property.PropertyType, path);

                        // A null value is a cut cycle; the constructor's own default reads better than an explicit
                        // null, and a value-type property keeps its zero value either way.
                        if (value is not null)
                        {
                            property.SetValue(instance, value);
                        }
                    }
                }

                return instance;
            }
            finally
            {
                path.Remove(type);
            }
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

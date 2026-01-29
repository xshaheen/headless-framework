using System.Collections;
using System.Text.Json;

namespace Headless.Ticker.Infrastructure.Dashboard;

internal static class JsonExampleGenerator
{
    private static readonly JsonSerializerOptions _JsonOptions = new() { WriteIndented = true };

    private static object? _GenerateExample(Type type) => _Generate(type);

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
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>)
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
            TypeCode.Object => Activator.CreateInstance(type)!,
            TypeCode.SByte => (sbyte)1,
            TypeCode.Single => 123.45f,
            TypeCode.String => "string",
            TypeCode.UInt16 => (ushort)1,
            TypeCode.UInt32 => 123u,
            TypeCode.UInt64 => 123ul,
            _ => Activator.CreateInstance(type),
        };
    }

    private static string _GenerateExampleJson(Type type)
    {
        return JsonSerializer.Serialize(_GenerateExample(type), _JsonOptions);
    }

    public static bool TryGenerateExampleJson(Type type, out string json)
    {
        try
        {
            json = _GenerateExampleJson(type);
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

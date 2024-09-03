using System.Reflection;

namespace Framework.Kernel.BuildingBlocks.Helpers.Reflection;

[PublicAPI]
public static class TypeHelper
{
    public static bool IsNonNullablePrimitiveType(Type type)
    {
        return type == typeof(byte)
            || type == typeof(short)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(sbyte)
            || type == typeof(ushort)
            || type == typeof(uint)
            || type == typeof(ulong)
            || type == typeof(bool)
            || type == typeof(float)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    public static bool IsFunc(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        var type = obj.GetType();

        return type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<>);
    }

    public static bool IsFunc<TReturn>(object? obj)
    {
        return obj is not null && obj.GetType() == typeof(Func<TReturn>);
    }

    public static T GetDefaultValue<T>()
    {
        return default!;
    }

    public static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    public static Type GetType<T>()
    {
        return GetType(typeof(T));
    }

    public static Type GetType(Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }
}

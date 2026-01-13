// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Framework.Reflection;

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

    [RequiresUnreferencedCode("Uses Activator.CreateInstance which may not work correctly with trimming.")]
    public static object? GetDefaultValue([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
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

    [RequiresUnreferencedCode("Uses reflection to get constructible defined types from assembly.")]
    public static IEnumerable<Type> GetDerivedTypes<TAction>(IEnumerable<Assembly> assemblies)
    {
        var types = new List<Type>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var values = assembly
                    .GetLoadableDefinedTypes()
                    .Where(type =>
                        type is { IsClass: true, IsNotPublic: false, IsAbstract: false }
                        && typeof(TAction).IsAssignableFrom(type)
                    );

                types.AddRange(values);
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaderMessages = string.Join(
                    ", ",
                    ex.LoaderExceptions.Where(x => x?.Message is not null).Select(le => le!.Message)
                );

                Trace.TraceInformation(
                    "Unable to search types from assembly \"{0}\" for plugins of type \"{1}\": {2}",
                    assembly.FullName,
                    typeof(TAction).Name,
                    loaderMessages
                );
            }
        }

        return types;
    }
}

using System.Diagnostics;
using System.Reflection;

namespace Framework.Kernel.BuildingBlocks.Helpers.Reflection;

[PublicAPI]
public static class TypeHelper
{
    public static readonly Type ObjectType = typeof(object);
    public static readonly Type StringType = typeof(string);
    public static readonly Type CharType = typeof(char);
    public static readonly Type NullableCharType = typeof(char?);
    public static readonly Type DateTimeType = typeof(DateTime);
    public static readonly Type NullableDateTimeType = typeof(DateTime?);
    public static readonly Type BoolType = typeof(bool);
    public static readonly Type NullableBoolType = typeof(bool?);
    public static readonly Type ByteArrayType = typeof(byte[]);
    public static readonly Type ByteType = typeof(byte);
    public static readonly Type SByteType = typeof(sbyte);
    public static readonly Type SingleType = typeof(float);
    public static readonly Type DecimalType = typeof(decimal);
    public static readonly Type Int16Type = typeof(short);
    public static readonly Type UInt16Type = typeof(ushort);
    public static readonly Type Int32Type = typeof(int);
    public static readonly Type UInt32Type = typeof(uint);
    public static readonly Type Int64Type = typeof(long);
    public static readonly Type UInt64Type = typeof(ulong);
    public static readonly Type DoubleType = typeof(double);

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

    public static IEnumerable<Type> GetDerivedTypes<TAction>(IEnumerable<Assembly>? assemblies = null)
    {
        assemblies ??= AppDomain.CurrentDomain.GetAssemblies();

        var types = new List<Type>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var values = assembly
                    .GetTypes()
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

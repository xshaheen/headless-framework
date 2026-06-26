// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;

namespace Headless.Reflection;

/// <summary>
/// Helpers for inspecting types: primitive detection, <see cref="Func{TResult}"/> detection, default-value
/// construction, nullable unwrapping, and assembly type scanning.
/// </summary>
[PublicAPI]
public static class TypeHelper
{
    /// <summary>
    /// Determines whether the type is one of the supported non-nullable primitive-like types (integral and floating
    /// numerics, <see cref="char"/>, <see cref="bool"/>, <see cref="decimal"/>, <see cref="DateTime"/>,
    /// <see cref="DateTimeOffset"/>, <see cref="TimeSpan"/>, and <see cref="Guid"/>).
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is one of the supported non-nullable primitive-like types; otherwise <see langword="false"/>.</returns>
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
            || type == typeof(char)
            || type == typeof(bool)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    /// <summary>Determines whether the object is an instance of the open generic <see cref="Func{TResult}"/> (single type parameter).</summary>
    /// <param name="obj">The object to inspect. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="Func{TResult}"/>; otherwise <see langword="false"/>.</returns>
    public static bool IsFunc(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        var type = obj.GetType();

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<>);
    }

    /// <summary>Determines whether the object is a <see cref="Func{TResult}"/> returning <typeparamref name="TReturn"/>.</summary>
    /// <typeparam name="TReturn">The expected return type of the delegate.</typeparam>
    /// <param name="obj">The object to inspect. May be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="Func{TReturn}"/>; otherwise <see langword="false"/>.</returns>
    public static bool IsFunc<TReturn>(object? obj)
    {
        return obj is not null && obj.GetType() == typeof(Func<TReturn>);
    }

    /// <summary>Gets the default value of <typeparamref name="T"/> (<c>default(T)</c>).</summary>
    /// <typeparam name="T">The type whose default value is returned.</typeparam>
    /// <returns>The default value of <typeparamref name="T"/>.</returns>
    public static T GetDefaultValue<T>()
    {
        return default!;
    }

    /// <summary>
    /// Gets the default value for the type: a zero-initialized boxed value for value types, or <see langword="null"/>
    /// for reference types.
    /// </summary>
    /// <param name="type">The type whose default value is produced.</param>
    /// <returns>The boxed default value for a value type, or <see langword="null"/> for a reference type.</returns>
    [RequiresUnreferencedCode("Uses Activator.CreateInstance which may not work correctly with trimming.")]
    public static object? GetDefaultValue(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type
    )
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    /// <summary>Gets the non-nullable form of <typeparamref name="T"/>, unwrapping <see cref="Nullable{T}"/>.</summary>
    /// <typeparam name="T">The type to unwrap.</typeparam>
    /// <returns>The underlying type if <typeparamref name="T"/> is a <see cref="Nullable{T}"/>; otherwise <typeparamref name="T"/>.</returns>
    public static Type GetType<T>()
    {
        return GetType(typeof(T));
    }

    /// <summary>Gets the non-nullable form of the type, unwrapping <see cref="Nullable{T}"/>.</summary>
    /// <param name="type">The type to unwrap.</param>
    /// <returns>The underlying type if <paramref name="type"/> is a <see cref="Nullable{T}"/>; otherwise <paramref name="type"/>.</returns>
    public static Type GetType(Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <summary>
    /// Scans the given assemblies for concrete public classes assignable to <typeparamref name="TAction"/>. Assemblies
    /// whose types cannot be loaded are traced and skipped rather than aborting the scan.
    /// </summary>
    /// <typeparam name="TAction">The base type or interface the discovered types must be assignable to.</typeparam>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The concrete public, non-abstract classes assignable to <typeparamref name="TAction"/>.</returns>
    [RequiresUnreferencedCode("Uses assembly scanning which is not compatible with trimming.")]
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

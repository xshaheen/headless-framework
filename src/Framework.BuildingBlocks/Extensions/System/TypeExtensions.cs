using System.Runtime.CompilerServices;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Provides a set of extension methods for operations on <see cref="Type"/>.</summary>
[PublicAPI]
public static class TypeExtensions
{
    [MustUseReturnValue]
    public static bool IsNullableValueType(this Type type)
    {
        return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    [MustUseReturnValue]
    public static bool IsNullableType(this Type type)
    {
        return !type.IsValueType || type.IsNullableValueType();
    }

    [MustUseReturnValue]
    public static Type UnwrapNullableType(this Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    [MustUseReturnValue]
    public static Type MakeNullable(this Type type, bool nullable = true)
    {
        return type.IsNullableType() == nullable
            ? type
            : nullable
                ? typeof(Nullable<>).MakeGenericType(type)
                : type.UnwrapNullableType();
    }

    [MustUseReturnValue]
    public static Type UnwrapEnumType(this Type type)
    {
        var isNullable = type.IsNullableType();
        var underlyingNonNullableType = isNullable ? type.UnwrapNullableType() : type;

        if (!underlyingNonNullableType.IsEnum)
        {
            return type;
        }

        var underlyingEnumType = Enum.GetUnderlyingType(underlyingNonNullableType);

        return isNullable ? MakeNullable(underlyingEnumType) : underlyingEnumType;
    }

    /// <summary>
    /// Checks if a <param name="testType"></param> is the same/instance type of <param name="sourceType"></param> even if it's nullable type
    /// </summary>
    [MustUseReturnValue]
    public static bool IsOfType(this Type sourceType, Type testType)
    {
        if (!sourceType.IsGenericType)
        {
            return sourceType == testType || sourceType.IsSubclassOf(testType);
        }

        if (sourceType.GetGenericTypeDefinition() != typeof(Nullable<>))
        {
            return false;
        }

        var innerType = sourceType.GetInnerType()!;

        return testType == innerType || innerType.IsSubclassOf(sourceType);
    }

    [MustUseReturnValue]
    public static bool IsInstantiable(this Type type)
    {
        return type is { IsAbstract: false, IsInterface: false }
            && (!type.IsGenericType || !type.IsGenericTypeDefinition);
    }

    [MustUseReturnValue]
    public static bool IsAnonymousType(this Type type)
    {
        return type.Name.StartsWith("<>", StringComparison.Ordinal)
            && type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), inherit: false).Length > 0
            && type.Name.Contains("AnonymousType", StringComparison.Ordinal);
    }

    [MustUseReturnValue]
    public static Type? GetInnerType(this Type type)
    {
        return !type.IsGenericType || type.GetGenericArguments().Length > 1 ? null : type.GetGenericArguments()[0];
    }

    [MustUseReturnValue]
    public static object? GetDefaultValue(this Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    [MustUseReturnValue]
    public static bool IsDefaultValue(this object? obj)
    {
        return obj?.Equals(GetDefaultValue(obj.GetType())) is not false;
    }
}

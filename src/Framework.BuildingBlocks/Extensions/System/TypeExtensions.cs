// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Provides a set of extension methods for operations on <see cref="Type"/>.</summary>
[PublicAPI]
public static class TypeExtensions
{
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    [MustUseReturnValue]
    public static IEnumerable<Type> GetConstructibleTypes(this Assembly assembly)
    {
        return assembly.GetLoadableTypes().Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
    }

    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    [MustUseReturnValue]
    public static Type[] GetLoadableTypes(this Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).ToArray()!;
        }
    }

    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    [MustUseReturnValue]
    public static IEnumerable<TypeInfo> GetConstructibleDefinedTypes(this Assembly assembly)
    {
        return assembly
            .GetLoadableDefinedTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
    }

    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    [MustUseReturnValue]
    public static IEnumerable<TypeInfo> GetLoadableDefinedTypes(this Assembly assembly)
    {
        try
        {
            return assembly.DefinedTypes;
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Select(IntrospectionExtensions.GetTypeInfo!);
        }
    }

    [MustUseReturnValue]
    public static string GetFullNameWithAssemblyName(this Type type)
    {
        return type.FullName + ", " + type.Assembly.GetName().Name;
    }

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
        return type.IsNullableType() == nullable ? type
            : nullable ? typeof(Nullable<>).MakeGenericType(type)
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

    [MustUseReturnValue]
    public static bool IsPrimitiveExtended(this Type type, bool includeNullables = true, bool includeEnums = false)
    {
        if (isPrimitive(type, includeEnums))
        {
            return true;
        }

        if (includeNullables && IsNullableValueType(type) && type.GenericTypeArguments.Length != 0)
        {
            return isPrimitive(type.GenericTypeArguments[0], includeEnums);
        }

        return false;

        static bool isPrimitive(Type type, bool includeEnums)
        {
            if (type.IsPrimitive)
            {
                return true;
            }

            if (includeEnums && type.IsEnum)
            {
                return true;
            }

            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }
    }

    #region TPL

    /// <summary>Checks if given method is an async method.</summary>
    /// <param name="method">A method to check</param>
    [MustUseReturnValue]
    public static bool IsAsync(this MethodInfo method)
    {
        Argument.IsNotNull(method);

        return method.ReturnType.IsTaskOrTaskOfT();
    }

    [MustUseReturnValue]
    public static bool IsTaskOrTaskOfT(this Type type)
    {
        return type == typeof(Task)
            || (type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>));
    }

    [MustUseReturnValue]
    public static bool IsTaskOfT(this Type type)
    {
        return type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);
    }

    #endregion

    #region Assignable

    /// <summary>
    /// <para>
    /// Determines whether an instance of this type can be assigned to
    /// an instance of the <typeparamref name="TTarget"></typeparamref>.
    /// </para>
    /// <para>Internally uses <see cref="Type.IsAssignableFrom"/>.</para>
    /// </summary>
    /// <typeparam name="TTarget">Target type</typeparam> (as reverse).
    [MustUseReturnValue]
    public static bool IsAssignableTo<TTarget>(this Type type)
    {
        Argument.IsNotNull(type);

        return type.IsAssignableTo(typeof(TTarget));
    }

    /// <summary>
    /// Determines whether an instance of this type can be assigned to
    /// an instance of the <paramref name="targetType"></paramref>.
    /// Internally uses <see cref="Type.IsAssignableFrom"/> (as reverse).
    /// </summary>
    /// <param name="type">this type</param>
    /// <param name="targetType">Target type</param>
    [MustUseReturnValue]
    public static bool IsAssignableTo(this Type type, Type targetType)
    {
        Argument.IsNotNull(type);
        Argument.IsNotNull(targetType);

        return targetType.IsAssignableFrom(type);
    }

    #endregion

    #region Base Classes

    /// <summary>Gets all base classes of this type.</summary>
    /// <param name="type">The type to get its base classes.</param>
    /// <param name="includeObject">True, to include the standard <see cref="object"/> type in the returned array.</param>
    public static IReadOnlyList<Type> GetBaseClasses(this Type type, bool includeObject = true)
    {
        Argument.IsNotNull(type);

        var types = new List<Type>();
        _AddTypeAndBaseTypesRecursively(types, type.BaseType, includeObject);

        return types;
    }

    /// <summary>Gets all base classes of this type.</summary>
    /// <param name="type">The type to get its base classes.</param>
    /// <param name="stoppingType">A type to stop going to the deeper base classes. This type will be included in the returned array</param>
    /// <param name="includeObject">True, to include the standard <see cref="object"/> type in the returned array.</param>
    public static IReadOnlyList<Type> GetBaseClasses(this Type type, Type stoppingType, bool includeObject = true)
    {
        Argument.IsNotNull(type);

        var types = new List<Type>();
        _AddTypeAndBaseTypesRecursively(types, type.BaseType, includeObject, stoppingType);

        return types;
    }

    private static void _AddTypeAndBaseTypesRecursively(
        List<Type> types,
        Type? type,
        bool includeObject,
        Type? stoppingType = null
    )
    {
        if (type is null || type == stoppingType)
        {
            return;
        }

        if (!includeObject && type == typeof(object))
        {
            return;
        }

        _AddTypeAndBaseTypesRecursively(types, type.BaseType, includeObject, stoppingType);
        types.Add(type);
    }

    #endregion
}

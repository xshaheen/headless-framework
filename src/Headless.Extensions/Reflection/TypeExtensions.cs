// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Reflection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>
/// Extension methods over <see cref="Type"/> for friendly naming, nullability, enum/primitive inspection, TPL type
/// detection, assignability, and base-class enumeration.
/// </summary>
[PublicAPI]
public static class TypeExtensions
{
    // The friendly name is a pure function of the type and is requested repeatedly (logging, diagnostics), so memoize
    // it. Nested generics recurse through this same cached entry point.
    private static readonly ConcurrentDictionary<Type, string> _FriendlyTypeNameCache = new();

    /// <summary>
    /// Gets a human-readable type name, rendering generic types with angle-bracket argument lists (for example
    /// <c>List&lt;Int32&gt;</c>) instead of the CLR backtick form.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The friendly type name.</returns>
    public static string GetFriendlyTypeName(this Type type)
    {
        return _FriendlyTypeNameCache.GetOrAdd(type, static t => _BuildFriendlyTypeName(t));
    }

    private static string _BuildFriendlyTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var typeName = type.Name;
        var backtickIndex = typeName.IndexOf('`', StringComparison.Ordinal);

        if (backtickIndex > 0)
        {
            typeName = typeName[..backtickIndex];
        }

        // Build the "Name<Arg1, Arg2>" form with a StringBuilder instead of Select + string.Join + interpolation to
        // avoid the intermediate enumerator/array and joined-string allocations. Produces the identical string.
        var builder = new StringBuilder(typeName).Append('<');
        var genericArguments = type.GetGenericArguments();

        for (var i = 0; i < genericArguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(genericArguments[i].GetFriendlyTypeName());
        }

        return builder.Append('>').ToString();
    }

    /// <summary>Gets the type's full name combined with its (simple) assembly name, suitable for assembly-qualified lookups.</summary>
    /// <param name="type">The type to format.</param>
    /// <returns>A string of the form <c>{FullName}, {AssemblySimpleName}</c>.</returns>
    [MustUseReturnValue]
    public static string GetFullNameWithAssemblyName(this Type type)
    {
        return type.FullName + ", " + type.Assembly.GetName().Name;
    }

    /// <summary>Determines whether the type is a closed <see cref="Nullable{T}"/> value type.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is <see cref="Nullable{T}"/>; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsNullableValueType(this Type type)
    {
        return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    /// <summary>Determines whether a value of the type can be <see langword="null"/> (a reference type or a <see cref="Nullable{T}"/>).</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is a reference type or <see cref="Nullable{T}"/>; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsNullableType(this Type type)
    {
        return !type.IsValueType || type.IsNullableValueType();
    }

    /// <summary>Gets the underlying type of a <see cref="Nullable{T}"/>, or returns the type unchanged if it is not nullable.</summary>
    /// <param name="type">The type to unwrap.</param>
    /// <returns>The underlying non-nullable type, or <paramref name="type"/> itself when it is not a <see cref="Nullable{T}"/>.</returns>
    [MustUseReturnValue]
    public static Type UnwrapNullableType(this Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <summary>Returns the nullable or non-nullable form of the type according to <paramref name="nullable"/>.</summary>
    /// <param name="type">The type to convert.</param>
    /// <param name="nullable">When <see langword="true"/>, returns the <see cref="Nullable{T}"/> form; when <see langword="false"/>, returns the non-nullable form.</param>
    /// <returns>The requested nullable/non-nullable form of <paramref name="type"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="nullable"/> is <see langword="true"/> and <paramref name="type"/> is not a valid value type argument for <see cref="Nullable{T}"/> (for example a reference type or an open generic).</exception>
    [MustUseReturnValue]
    [RequiresDynamicCode("Making generic types may require dynamic code generation.")]
    public static Type MakeNullable(this Type type, bool nullable = true)
    {
        return type.IsNullableType() == nullable ? type
            : nullable ? typeof(Nullable<>).MakeGenericType(type)
            : type.UnwrapNullableType();
    }

    /// <summary>
    /// Gets the integral underlying type of an enum (preserving nullability), or returns the type unchanged when it is
    /// not an enum.
    /// </summary>
    /// <param name="type">The type to unwrap.</param>
    /// <returns>The enum's underlying integral type (made nullable if the input was a nullable enum), or <paramref name="type"/> when it is not an enum.</returns>
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

        return isNullable ? underlyingEnumType.MakeNullable() : underlyingEnumType;
    }

    /// <summary>
    /// Checks whether <paramref name="testType"/> is the same as, or a base type of, <paramref name="sourceType"/>,
    /// unwrapping <paramref name="sourceType"/> when it is a <see cref="Nullable{T}"/>.
    /// </summary>
    /// <param name="sourceType">The source type, possibly a <see cref="Nullable{T}"/>.</param>
    /// <param name="testType">The type to compare against.</param>
    /// <returns><see langword="true"/> when <paramref name="sourceType"/> (or its nullable inner type) matches or derives from <paramref name="testType"/>; otherwise <see langword="false"/>.</returns>
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

        return testType == innerType || innerType.IsSubclassOf(testType);
    }

    /// <summary>
    /// Determines whether the type can be instantiated: it is non-abstract, not an interface, and not an open generic
    /// type definition.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if an instance of <paramref name="type"/> can be created; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsInstantiable(this Type type)
    {
        return type is { IsAbstract: false, IsInterface: false }
            && (!type.IsGenericType || !type.IsGenericTypeDefinition);
    }

    /// <summary>Determines whether the type is a compiler-generated anonymous type.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is an anonymous type; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsAnonymousType(this Type type)
    {
        // IsDefined is a presence check that avoids materializing the attribute array that GetCustomAttributes(...) +
        // Length > 0 allocates; equivalent here since we only test for the attribute's presence.
        return type.Name.StartsWith("<>", StringComparison.Ordinal)
            && type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            && type.Name.Contains("AnonymousType", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the first generic type argument if the type is a generic type with exactly one type parameter.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>
    /// The first generic type argument if the type has exactly one generic parameter;
    /// otherwise, returns null.
    /// </returns>
    [MustUseReturnValue]
    public static Type? GetInnerType(this Type type)
    {
        return type.IsGenericType && type.GetGenericArguments() is { Length: 1 } args ? args[0] : null;
    }

    /// <summary>
    /// Retrieves the inner types of a type if it is a generic type; otherwise, returns an empty array.
    /// </summary>
    /// <param name="type">The type to retrieve the inner types from.</param>
    /// <returns>An array of <see cref="System.Type"/> representing the inner types of the generic type, or an empty array if the type is not generic or has no generic arguments.</returns>
    [MustUseReturnValue]
    public static Type[] GetInnerTypes(this Type type)
    {
        return type.IsGenericType && type.GetGenericArguments() is { Length: > 0 } args ? args : [];
    }

    /// <summary>
    /// Gets the default value for the type: a zero-initialized boxed value for value types, or <see langword="null"/>
    /// for reference types.
    /// </summary>
    /// <param name="type">The type whose default value is produced.</param>
    /// <returns>The boxed default value for a value type, or <see langword="null"/> for a reference type.</returns>
    [MustUseReturnValue]
    [RequiresUnreferencedCode("Uses Activator.CreateInstance which may not work correctly with trimming.")]
    public static object? GetDefaultValue(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] this Type type
    )
    {
        return TypeHelper.GetDefaultValue(type);
    }

    /// <summary>Determines whether the object equals the default value of its runtime type.</summary>
    /// <param name="obj">The object to test. A <see langword="null"/> reference is treated as a default value.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is <see langword="null"/> or equal to the default value of its type; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    [RequiresUnreferencedCode("Uses GetDefaultValue which uses Activator.CreateInstance.")]
    public static bool IsDefaultValue(this object? obj)
    {
        return obj?.Equals(obj.GetType().GetDefaultValue()) is not false;
    }

    /// <summary>
    /// Determines whether the type is "primitive" in an extended sense: a CLR primitive or one of
    /// <see cref="string"/>, <see cref="decimal"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="TimeSpan"/>, or <see cref="Guid"/>, optionally including nullable forms and enums.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="includeNullables">When <see langword="true"/>, a <see cref="Nullable{T}"/> wrapping an extended-primitive type also matches.</param>
    /// <param name="includeEnums">When <see langword="true"/>, enum types also match.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> qualifies as an extended primitive; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsPrimitiveExtended(this Type type, bool includeNullables = true, bool includeEnums = false)
    {
        if (isPrimitive(type, includeEnums))
        {
            return true;
        }

        if (includeNullables && type.IsNullableValueType() && type.GenericTypeArguments.Length != 0)
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

    /// <summary>Determines whether the type is <see cref="Task{TResult}"/>.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is a closed <see cref="Task{TResult}"/>; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsTaskOfT(this Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);
    }

    /// <summary>Determines whether the type is <see cref="Task"/> or <see cref="Task{TResult}"/>.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is <see cref="Task"/> or a closed <see cref="Task{TResult}"/>; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsTaskOrTaskOfT(this Type type)
    {
        return type == typeof(Task) || type.IsTaskOfT();
    }

    /// <summary>Determines whether the type is <see cref="ValueTask{TResult}"/>.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is a closed <see cref="ValueTask{TResult}"/>; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsValueTaskOfT(this Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
    }

    /// <summary>Determines whether the type is <see cref="ValueTask"/> or <see cref="ValueTask{TResult}"/>.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is <see cref="ValueTask"/> or a closed <see cref="ValueTask{TResult}"/>; otherwise <see langword="false"/>.</returns>
    [MustUseReturnValue]
    public static bool IsValueTaskOrValueTaskOfT(this Type type)
    {
        return type == typeof(ValueTask) || type.IsValueTaskOfT();
    }

    /// <summary>Returns void if given type is Task. Return T, if given type is Task{T}. Returns given type otherwise.</summary>
    /// <param name="type">The type to unwrap.</param>
    /// <returns>void for <see cref="Task"/>, the result type for <see cref="Task{TResult}"/>, or <paramref name="type"/> itself otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    public static Type UnwrapTask(this Type type)
    {
        Argument.IsNotNull(type);

        if (type == typeof(Task))
        {
            return typeof(void);
        }

        if (type.IsTaskOfT())
        {
            return type.GenericTypeArguments[0];
        }

        return type;
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
    /// <typeparam name="TTarget">Target type</typeparam>
    /// <param name="type">The source type.</param>
    /// <returns><see langword="true"/> if an instance of <paramref name="type"/> can be assigned to <typeparamref name="TTarget"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    [MustUseReturnValue]
    public static bool IsAssignableTo<TTarget>(this Type type)
    {
        Argument.IsNotNull(type);

        return type.IsAssignableTo(typeof(TTarget));
    }

    #endregion

    #region Base Classes

    /// <summary>Gets all base classes of this type, ordered from the least-derived ancestor up to the immediate base class.</summary>
    /// <param name="type">The type to get its base classes.</param>
    /// <param name="includeObject">True, to include the standard <see cref="object"/> type in the returned array.</param>
    /// <returns>The base classes of <paramref name="type"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<Type> GetBaseClasses(this Type type, bool includeObject = true)
    {
        Argument.IsNotNull(type);

        var types = new List<Type>();
        _AddTypeAndBaseTypesRecursively(types, type.BaseType, includeObject);

        return types;
    }

    /// <summary>Gets all base classes of this type, stopping before <paramref name="stoppingType"/>.</summary>
    /// <param name="type">The type to get its base classes.</param>
    /// <param name="stoppingType">A type to stop going to the deeper base classes. This type will be excluded from the returned array.</param>
    /// <param name="includeObject">True, to include the standard <see cref="object"/> type in the returned array.</param>
    /// <returns>The base classes of <paramref name="type"/> up to (but not including) <paramref name="stoppingType"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
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

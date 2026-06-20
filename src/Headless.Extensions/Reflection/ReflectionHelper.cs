// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;

namespace Headless.Reflection;

/// <summary>
/// Helpers for reading attributes from members and inspecting type relationships (nullable, flags enum, generic
/// inheritance, and assignability).
/// </summary>
[PublicAPI]
public static class ReflectionHelper
{
    /// <summary>
    /// Gets the first attribute of type <typeparamref name="TAttribute"/> defined on the member (optionally including
    /// inherited attributes), or <paramref name="defaultValue"/> if none is declared.
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="defaultValue">Default value (null as default)</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    /// <returns>The matching attribute, or <paramref name="defaultValue"/> when none is found.</returns>
    public static TAttribute? GetFirstOrDefaultAttribute<TAttribute>(
        this MemberInfo memberInfo,
        TAttribute? defaultValue = null,
        bool inherit = true
    )
        where TAttribute : Attribute
    {
        return memberInfo.GetCustomAttributes<TAttribute>(inherit).FirstOrDefault() ?? defaultValue;
    }

    /// <summary>
    /// Gets the first attribute of type <typeparamref name="TAttribute"/> declared on the member or, failing that, on
    /// the member's declaring type (optionally including inherited attributes), or <paramref name="defaultValue"/> if
    /// none is declared.
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="defaultValue">Default value (null as default)</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    /// <returns>The matching attribute from the member or its declaring type, or <paramref name="defaultValue"/> when none is found.</returns>
    public static TAttribute? GetSingleAttributeOfMemberOrDeclaringTypeOrDefault<TAttribute>(
        this MemberInfo memberInfo,
        TAttribute? defaultValue = null,
        bool inherit = true
    )
        where TAttribute : class
    {
        // TAttribute is constrained to `class` (not `Attribute`), so the typed GetCustomAttributes<T>
        // overload is unavailable here; use the object[] overload + OfType to preserve that flexibility.
        return memberInfo.GetCustomAttributes(inherit).OfType<TAttribute>().FirstOrDefault()
            ?? memberInfo
                .DeclaringType?.GetTypeInfo()
                .GetCustomAttributes(inherit)
                .OfType<TAttribute>()
                .FirstOrDefault()
            ?? defaultValue;
    }

    /// <summary>
    /// Gets the distinct attributes of type <typeparamref name="TAttribute"/> declared on the member combined with
    /// those declared on the member's declaring type (optionally including inherited attributes).
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    /// <returns>The distinct matching attributes from the member and its declaring type.</returns>
    public static IEnumerable<TAttribute> GetAttributesOfMemberOrDeclaringType<TAttribute>(
        this MemberInfo memberInfo,
        bool inherit = true
    )
        where TAttribute : class
    {
        // TAttribute is constrained to `class` (not `Attribute`); use the object[] overload + OfType.
        var customAttributes = memberInfo.GetCustomAttributes(inherit).OfType<TAttribute>();

        var declaringTypeCustomAttributes = memberInfo
            .DeclaringType?.GetTypeInfo()
            .GetCustomAttributes(inherit)
            .OfType<TAttribute>();

        return declaringTypeCustomAttributes is not null
            ? customAttributes.Concat(declaringTypeCustomAttributes).Distinct()
            : customAttributes;
    }

    /// <summary>Determines whether the type is a closed <see cref="Nullable{T}"/> value type.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is <see cref="Nullable{T}"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    public static bool IsNullableOfT(this Type type)
    {
        Argument.IsNotNull(type);

        return Nullable.GetUnderlyingType(type) is not null;
    }

    /// <summary>Determines whether <typeparamref name="T"/> is an enum decorated with <see cref="FlagsAttribute"/>.</summary>
    /// <typeparam name="T">The type to inspect.</typeparam>
    /// <returns><see langword="true"/> if <typeparamref name="T"/> is a flags enum; otherwise <see langword="false"/>.</returns>
    public static bool IsFlagsEnum<T>()
    {
        return typeof(T).IsFlagsEnum();
    }

    /// <summary>Determines whether the type is an enum decorated with <see cref="FlagsAttribute"/>.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is a flags enum; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    public static bool IsFlagsEnum(this Type type)
    {
        Argument.IsNotNull(type);

        return type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: true);
    }

    /// <summary>
    /// Determines whether <paramref name="child"/> derives from (or implements) the generic type
    /// <paramref name="parent"/>, matching against open generic definitions where applicable.
    /// </summary>
    /// <param name="child">The candidate descendant type.</param>
    /// <param name="parent">The generic base type or interface to test against.</param>
    /// <returns><see langword="true"/> if <paramref name="child"/> is a subclass of <paramref name="parent"/>; otherwise <see langword="false"/>. Returns <see langword="false"/> when the two types are equal.</returns>
    [RequiresUnreferencedCode("Uses Type.GetInterfaces() which is not compatible with trimming.")]
    public static bool IsSubClassOfGeneric(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type child,
        Type parent
    )
    {
        if (child == parent)
        {
            return false;
        }

        if (child.IsSubclassOf(parent))
        {
            return true;
        }

        var parameters = parent.GetGenericArguments();

        var isParameterLessGeneric = !(
            parameters is { Length: > 0 }
            && (parameters[0].Attributes & TypeAttributes.BeforeFieldInit) == TypeAttributes.BeforeFieldInit
        );

        while (child is not null && child != typeof(object))
        {
            var cur = _GetFullTypeDefinition(child);

            if (
                parent == cur
                || (
                    isParameterLessGeneric
                    && cur.GetInterfaces().Select(_GetFullTypeDefinition).Contains(_GetFullTypeDefinition(parent))
                )
            )
            {
                return true;
            }

            if (!isParameterLessGeneric)
            {
                if (_GetFullTypeDefinition(parent) == cur && !cur.IsInterface)
                {
                    if (_VerifyGenericArguments(_GetFullTypeDefinition(parent), cur))
                    {
                        if (_VerifyGenericArguments(parent, child))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    foreach (
                        var item in child
                            .GetInterfaces()
                            .Where(i => _GetFullTypeDefinition(parent) == _GetFullTypeDefinition(i))
                    )
                    {
                        if (_VerifyGenericArguments(parent, item))
                        {
                            return true;
                        }
                    }
                }
            }

            child = child.BaseType!;
        }

        return false;
    }

    private static Type _GetFullTypeDefinition(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }

    private static bool _VerifyGenericArguments(Type parent, Type child)
    {
        var childArguments = child.GetGenericArguments();
        var parentArguments = parent.GetGenericArguments();

        if (childArguments.Length != parentArguments.Length)
        {
            return true;
        }

        for (var i = 0; i < childArguments.Length; i++)
        {
            if (
                childArguments[i].Assembly == parentArguments[i].Assembly
                && string.Equals(childArguments[i].Name, parentArguments[i].Name, StringComparison.Ordinal)
                && string.Equals(childArguments[i].Namespace, parentArguments[i].Namespace, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (!childArguments[i].IsSubclassOf(parentArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether the type is assignable to the open generic type <paramref name="genericType"/>, walking the
    /// type's implemented interfaces and base-type chain.
    /// </summary>
    /// <param name="type">The type to test.</param>
    /// <param name="genericType">The open generic type definition (for example <c>typeof(IList&lt;&gt;)</c>) to test against.</param>
    /// <returns><see langword="true"/> if <paramref name="type"/> is, implements, or inherits a closed form of <paramref name="genericType"/>; otherwise <see langword="false"/>.</returns>
    [RequiresUnreferencedCode("Uses Type.GetInterfaces() which is not compatible with trimming.")]
    public static bool IsAssignableToGenericType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type type,
        Type genericType
    )
    {
        while (true)
        {
            var info = type.GetTypeInfo();

            if (info.IsGenericType && type.GetGenericTypeDefinition() == genericType)
            {
                return true;
            }

            foreach (var interfaceType in info.GetInterfaces())
            {
                if (
                    interfaceType.GetTypeInfo().IsGenericType
                    && interfaceType.GetGenericTypeDefinition() == genericType
                )
                {
                    return true;
                }
            }

            if (info.BaseType == null)
            {
                return false;
            }

            type = info.BaseType;
        }
    }
}
